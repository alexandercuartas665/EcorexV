using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Base de los tests E2E: contexto de navegador NUEVO por test (cookies aisladas),
/// helpers de login y del wizard de actividades. Selectores por rol/texto accesible
/// o por las clases CSS estables del prototipo (el producto NO tiene data-testid y
/// esta suite no puede agregarlos).
/// </summary>
[Collection("e2e")]
public abstract class E2eTestBase : IAsyncLifetime
{
    // Credenciales demo del vault (CREDENCIALES - Usuarios y claves): el seeder
    // siembra owner/admin/operator/viewer@sky-system.local. Sobreescribibles por
    // variables de entorno para correr contra BDs con seeds anteriores.
    public static readonly string DemoEmail =
        Environment.GetEnvironmentVariable("ECOREX_E2E_EMAIL") ?? "owner@sky-system.local";
    public static readonly string DemoPassword =
        Environment.GetEnvironmentVariable("ECOREX_E2E_PASSWORD") ?? "Demo123*";

    protected E2eAppFixture Fx { get; }

    /// <summary>Sufijo unico por instancia de test: idempotencia de datos entre corridas.</summary>
    protected string Sfx { get; } = Guid.NewGuid().ToString("N")[..8];

    private readonly List<IBrowserContext> _contexts = new();

    protected E2eTestBase(E2eAppFixture fx)
    {
        Fx = fx;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts)
        {
            await context.CloseAsync();
        }
    }

    /// <summary>Salta el test (con motivo claro) si la app o Playwright no estan disponibles.</summary>
    protected void RequireApp() => Skip.If(Fx.SkipReason is not null, Fx.SkipReason);

    protected async Task<IPage> NewPageAsync()
    {
        var context = await Fx.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Fx.BaseUrl + "/",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            Locale = "es-CO"
        });
        context.SetDefaultTimeout(15_000);
        _contexts.Add(context);
        return await context.NewPageAsync();
    }

    // ---- Login ----

    protected async Task<IPage> LoginAsync(string? email = null, string? password = null)
    {
        email ??= DemoEmail;
        password ??= DemoPassword;
        var page = await NewPageAsync();
        var ok = await TryLoginAsync(page, email, password);
        Assert.True(ok, $"El login con {email} no aterrizo en /inicio (url: {page.Url}).");
        return page;
    }

    /// <summary>Intenta el login; true si aterrizo en /inicio, false si volvio a /login con error.</summary>
    protected static async Task<bool> TryLoginAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("login");
        await page.FillAsync("#login-email", email);
        await page.FillAsync("#login-password", password);
        await page.ClickAsync(".auth-pane-login button.auth-submit");
        await page.WaitForURLAsync(new Regex("(/inicio|/login\\?|/$)"));
        return page.Url.Contains("/inicio", StringComparison.Ordinal);
    }

    // ---- Wizard "Actividad completa" (3 pasos con tipo/flujo BPMN) ----

    /// <summary>
    /// Crea una actividad completa por el wizard de 3 pasos y devuelve el numero T#####
    /// que anuncia el toast. Ola 2: el wizard vive en el INDICE de tableros de
    /// /actividades detras del boton "Actividad completa" (la tarea creada NO cuelga de
    /// ningun tablero; para tarjetas del tablero usar QuickCreateTaskAsync). Los labels
    /// del formulario no estan asociados con for/id, por eso se ancla cada control a su
    /// div.field por el texto del label.
    /// </summary>
    protected static async Task<string> CreateActivityAsync(
        IPage page, string category, string type, string title,
        string priority = "Alta",
        string requesterName = "Cliente E2E",
        string requesterEmail = "cliente.e2e@ejemplo.com")
    {
        await page.GotoAsync("actividades");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Actividad completa" }).ClickAsync();

        var wizard = page.Locator(".tk-wizard");
        await wizard.WaitForAsync();

        // Paso 1: tipo, titulo, prioridad.
        await FieldIn(wizard, "Categoria").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = category });
        await FieldIn(wizard, "Tipo de actividad").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = type });
        await FieldIn(wizard, "Titulo").Locator("input").FillAsync(title);
        await wizard.Locator(".tk-priority-opt")
            .Filter(new LocatorFilterOptions { HasText = priority }).ClickAsync();
        await wizard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Siguiente" }).ClickAsync();

        // Paso 2: contacto minimo.
        await FieldIn(wizard, "Nombre del solicitante").Locator("input").FillAsync(requesterName);
        await FieldIn(wizard, "Email del solicitante").Locator("input").FillAsync(requesterEmail);
        await wizard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Siguiente" }).ClickAsync();

        // Paso 3: confirmar (el resumen debe mostrar el titulo).
        await Assertions.Expect(wizard.Locator(".tk-summary")).ToContainTextAsync(title);
        await wizard.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Crear actividad" }).ClickAsync();

        // Toast "Actividad T00042 creada." (se auto-descarta a los ~4s: leerlo de una).
        var toast = page.Locator(".tk-toast.ok").Filter(new LocatorFilterOptions { HasText = "creada" });
        await toast.WaitForAsync();
        var text = await toast.InnerTextAsync();
        var match = Regex.Match(text, @"T\d+");
        Assert.True(match.Success, $"El toast de creacion no trae numero T#####: '{text}'");
        return match.Value;
    }

    /// <summary>div.field del wizard/modales anclado por el texto de su label.</summary>
    protected static ILocator FieldIn(ILocator scope, string label)
        => scope.Locator(".field").Filter(new LocatorFilterOptions
        {
            Has = scope.Page.Locator("label.form-label", new PageLocatorOptions { HasText = label })
        }).First;

    // ---- Tableros de actividades (ola 2, pantalla 'work' del prototipo) ----

    /// <summary>
    /// Abre un tablero desde el indice de /actividades haciendo clic en su tarjeta
    /// (ancla por el nombre del tablero) y espera el kanban por columnas.
    /// </summary>
    protected static async Task OpenBoardAsync(IPage page, string boardName)
    {
        await page.GotoAsync("actividades");
        await page.Locator(".ab-board-card").Filter(new LocatorFilterOptions { HasText = boardName })
            .First.ClickAsync();
        await page.Locator(".ab-kanban").WaitForAsync();
    }

    /// <summary>
    /// Crea una tarea con el modal de creacion rapida del tablero abierto (boton "Tarea")
    /// y devuelve el numero T##### del toast. type/column/priority/assignee opcionales.
    /// </summary>
    protected static async Task<string> QuickCreateTaskAsync(
        IPage page, string title,
        string? column = null,
        string? priority = null,
        string? typeLabel = null)
    {
        await page.Locator(".ab-btn-task").ClickAsync();
        var modal = page.Locator(".ab-quick-modal");
        await modal.WaitForAsync();

        await AbFieldIn(modal, "Titulo").Locator("input").FillAsync(title);
        if (column is not null)
        {
            await AbFieldIn(modal, "Columna").Locator("select")
                .SelectOptionAsync(new SelectOptionValue { Label = column });
        }
        if (priority is not null)
        {
            await modal.Locator(".ab-prio-opt")
                .Filter(new LocatorFilterOptions { HasText = priority }).ClickAsync();
        }
        if (typeLabel is not null)
        {
            await AbFieldIn(modal, "Tipo de actividad").Locator("select")
                .SelectOptionAsync(new SelectOptionValue { Label = typeLabel });
        }
        await modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Crear tarea" }).ClickAsync();

        var toast = page.Locator(".tk-toast.ok").Filter(new LocatorFilterOptions { HasText = "creada" });
        await toast.WaitForAsync();
        var text = await toast.InnerTextAsync();
        var match = Regex.Match(text, @"T\d+");
        Assert.True(match.Success, $"El toast de creacion rapida no trae numero T#####: '{text}'");
        return match.Value;
    }

    /// <summary>div.ab-field de los modales de tablero anclado por el texto de su label.</summary>
    protected static ILocator AbFieldIn(ILocator scope, string label)
        => scope.Locator(".ab-field").Filter(new LocatorFilterOptions
        {
            Has = scope.Page.Locator("label", new PageLocatorOptions { HasText = label })
        }).First;

    /// <summary>Columna del kanban del tablero abierto, anclada por su nombre.</summary>
    protected static ILocator BoardColumn(IPage page, string columnName)
        => page.Locator(".ab-kanban > div").Filter(new LocatorFilterOptions
        {
            Has = page.Locator(".ab-col-name", new PageLocatorOptions { HasText = columnName })
        });

    protected static ILocator CardIn(ILocator column, string title)
        => column.Locator(".ab-card").Filter(new LocatorFilterOptions { HasText = title });

    /// <summary>Abre el detalle de la tarea haciendo clic en su tarjeta del tablero.</summary>
    protected static async Task<ILocator> OpenTaskDetailAsync(IPage page, string title)
    {
        await page.Locator(".ab-card").Filter(new LocatorFilterOptions { HasText = title })
            .First.ClickAsync();
        var detail = page.Locator(".tk-detail");
        await detail.WaitForAsync();
        return detail;
    }

    protected static async Task CloseTaskDetailAsync(IPage page)
    {
        await page.Locator(".tb-modal-close").ClickAsync();
        await page.Locator(".tk-detail").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    /// <summary>Pill de estado del detalle (el unico pill que contiene un span .tk-status).</summary>
    protected static ILocator StatusPill(IPage page)
        => page.Locator(".tk-detail-pills button.tk-pill").Filter(new LocatorFilterOptions
        {
            Has = page.Locator(".tk-status")
        });
}
