using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario de la FICHA DE EMPRESA (/admin/empresas, modulo 000072 adm_empresas, ADR-0026):
/// el operador de plataforma abre la ficha de la empresa demo (SKY SYSTEM), edita un campo
/// seguro (telefono), guarda, ve el aviso de exito y el cambio persiste al recargar.
/// Verifica ademas que la estructura del proto esta presente (topbar MOD 000072, header-card,
/// usuarios reales) y que una seccion legacy peligrosa queda como "Pendiente".
/// Selectores por clases estables del modulo (ae-*).
/// </summary>
public sealed class AdmEmpresasTests : E2eTestBase
{
    // El modulo es de operador de plataforma (policy AdmEmpresas.Ver / platform_role):
    // se loguea con el Super Admin sembrado, que aterriza en "/" (no en /inicio).
    private static readonly string OperatorEmail =
        Environment.GetEnvironmentVariable("ECOREX_E2E_ADMIN_EMAIL") ?? "admin@ecorex.local";
    private static readonly string OperatorPassword =
        Environment.GetEnvironmentVariable("ECOREX_E2E_ADMIN_PASSWORD") ?? "Admin123*";

    public AdmEmpresasTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task AbrirFichaEmpresaDemo_EditarCampoSeguro_Guardar_YPersiste()
    {
        RequireApp();
        var page = await LoginOperatorAsync();

        // 1. La pagina real carga con la estructura del proto: topbar MOD 000072 + layout.
        await page.GotoAsync("admin/empresas");
        await page.Locator(".ae-layout").WaitForAsync();
        await Assertions.Expect(page.Locator(".ae-mod-code")).ToContainTextAsync("MOD 000072");

        // 2. Selecciona la empresa demo SKY SYSTEM en el panel izquierdo.
        await page.Locator(".ae-item").Filter(new LocatorFilterOptions { HasText = "SKY SYSTEM" })
            .First.ClickAsync();
        await Assertions.Expect(page.Locator(".ae-company-name")).ToContainTextAsync("SKY SYSTEM");

        // Usuarios REALES del tenant demo (owner/admin/operator/viewer): la tabla no esta vacia.
        await Assertions.Expect(page.Locator(".ae-grid tbody tr").First).ToBeVisibleAsync();

        // Una seccion legacy peligrosa (Cargar datos) queda visible como "Pendiente".
        await Assertions.Expect(
            page.Locator(".ae-section-pending").Filter(new LocatorFilterOptions { HasText = "Cargar datos" }))
            .ToContainTextAsync("Pendiente");

        // 3. Edita un campo seguro: telefono, con un valor unico de esta corrida.
        var phone = $"+57 601 {Sfx[..3]} {Sfx[3..7]}";
        var phoneField = FieldByLabel(page, "Telefono").Locator("input");
        await phoneField.FillAsync(phone);

        // 4. Guarda y ve el aviso de exito.
        await page.Locator(".ae-form-actions .primary").ClickAsync();
        await Assertions.Expect(page.Locator(".ae-flash.ok")).ToContainTextAsync("guardada");

        // 5. Recarga la pagina y reselecciona la empresa: el telefono persistio.
        await page.ReloadAsync();
        await page.Locator(".ae-item").Filter(new LocatorFilterOptions { HasText = "SKY SYSTEM" })
            .First.ClickAsync();
        await Assertions.Expect(FieldByLabel(page, "Telefono").Locator("input"))
            .ToHaveValueAsync(phone);
    }

    /// <summary>Login del operador de plataforma; aterriza en "/" (dashboard operador), no /inicio.</summary>
    private async Task<IPage> LoginOperatorAsync()
    {
        var page = await NewPageAsync();
        await page.GotoAsync("login");
        await page.FillAsync("#login-email", OperatorEmail);
        await page.FillAsync("#login-password", OperatorPassword);
        await page.ClickAsync(".auth-pane-login button.auth-submit");
        await page.WaitForURLAsync(new Regex("(/|/inicio)$"));
        Assert.DoesNotContain("/login", page.Url);
        return page;
    }

    /// <summary>div.ae-field anclado por el texto de su label.</summary>
    private static ILocator FieldByLabel(IPage page, string label)
        => page.Locator(".ae-field").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("label", new PageLocatorOptions { HasText = label })
        }).First;
}
