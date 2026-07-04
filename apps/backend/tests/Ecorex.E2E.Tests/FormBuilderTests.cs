using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario del CONSTRUCTOR de formularios (ADR-0021): desde el indice /formularios se
/// crea un formulario nuevo (abre el constructor del prototipo), se agregan un campo de
/// texto y una lista desplegable con 2 opciones desde la paleta, se ACTIVA y se llena en
/// la vista previa (renderer real en modo Fill) hasta un submit valido.
/// Selectores por clases estables del prototipo (fb-*, fx-*, dfr-*), sin data-testid.
/// </summary>
public sealed class FormBuilderTests : E2eTestBase
{
    public FormBuilderTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task CrearFormulario_DisenarActivarYLlenar_EnVistaPrevia()
    {
        RequireApp();
        var page = await LoginAsync();

        // 1. Indice -> "Nuevo formulario" crea y abre el constructor.
        await page.GotoAsync("formularios");
        for (var attempt = 0; ; attempt++)
        {
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Nuevo formulario" }).ClickAsync();
            try
            {
                await page.WaitForURLAsync(new Regex("/formularios/.+/disenar"), new PageWaitForURLOptions { Timeout = 5000 });
                break;
            }
            catch (TimeoutException) when (attempt < 3)
            {
            }
            catch (PlaywrightException) when (attempt < 3)
            {
            }
        }
        await page.Locator(".fb-shell").WaitForAsync();

        // 2. Agregar un campo de TEXTO desde la paleta (tarjeta "Input").
        var inputCard = page.Locator(".fb-pal-card").Filter(new LocatorFilterOptions { HasText = "Input" });
        await inputCard.ClickAsync();
        await Assertions.Expect(page.Locator(".fb-node-control")).ToHaveCountAsync(1);

        // Renombrar via propiedades (el @onchange requiere blur).
        var etiqueta = PropField(page, "Etiqueta visible").Locator("input");
        await etiqueta.FillAsync($"Nombre E2E {Sfx}");
        await etiqueta.BlurAsync();
        await Assertions.Expect(page.Locator(".fb-ctrl-label").First).ToContainTextAsync($"Nombre E2E {Sfx}");

        // 3. Agregar una LISTA: segunda tarjeta Input + cambio de tipo en el panel.
        await inputCard.ClickAsync();
        await Assertions.Expect(page.Locator(".fb-node-control")).ToHaveCountAsync(2);
        await PropField(page, "Tipo de elemento").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = "Lista desplegable" });
        // El cambio de tipo siembra 2 opciones por defecto (Opcion 1 / Opcion 2): se ven
        // en el tab Datos como chips editables.
        await page.Locator(".fb-props-tab").Filter(new LocatorFilterOptions { HasText = "Datos" }).ClickAsync();
        await Assertions.Expect(page.Locator(".fb-chip")).ToHaveCountAsync(2);
        // La etiqueta vive en el tab Diseno; volver para renombrar la lista.
        await page.Locator(".fb-props-tab").Filter(new LocatorFilterOptions { HasText = "Diseno" }).ClickAsync();
        var etiquetaLista = PropField(page, "Etiqueta visible").Locator("input");
        await etiquetaLista.FillAsync($"Opcion E2E {Sfx}");
        await etiquetaLista.BlurAsync();

        // 4. Activar (Draft -> Publicado).
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Activar" }).ClickAsync();
        await Assertions.Expect(page.Locator(".fb-status")).ToHaveTextAsync("Publicado");

        // 5. Vista previa = renderer REAL en modo Fill: llenar y enviar.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Vista previa" }).ClickAsync();
        var modal = page.Locator(".modal-dialog").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("h3", new PageLocatorOptions { HasText = "Vista previa" })
        });
        await modal.Locator(".dfr-root").WaitForAsync();

        var texto = DfrGroup(modal, $"Nombre E2E {Sfx}").Locator("input.form-control");
        await texto.FillAsync("Cliente del constructor");
        await texto.BlurAsync();
        await DfrGroup(modal, $"Opcion E2E {Sfx}").Locator("select.form-control")
            .SelectOptionAsync(new SelectOptionValue { Label = "Opcion 1" });

        await modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Enviar" }).ClickAsync();
        await Assertions.Expect(modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Enviado" }))
            .ToBeVisibleAsync();
    }

    /// <summary>div.fb-field del panel de propiedades anclado por el texto de su label.</summary>
    private static ILocator PropField(IPage page, string label)
        => page.Locator(".fb-right .fb-field").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("label", new PageLocatorOptions { HasText = label })
        }).First;

    /// <summary>Columna del renderer (div col-*) anclada por el texto del label del campo.</summary>
    private static ILocator DfrGroup(ILocator scope, string label)
        => scope.Locator(".dfr-root [class*='col-']").Filter(new LocatorFilterOptions
        {
            Has = scope.Page.Locator("label.dfr-label", new PageLocatorOptions { HasText = label })
        }).First;
}
