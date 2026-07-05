using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E del modulo de plantillas HSM de WhatsApp (ADR-0029): entra al editor, crea una plantilla
/// eligiendo la linea demo y verifica que aparece en la tabla. Selectores por las clases estables
/// de la pagina (.module-head, .field/label.form-label, .wa-templates-table). El nombre lleva el
/// sufijo unico del test (Sfx) para idempotencia entre corridas; el servicio lo normaliza a
/// formato tecnico (minusculas, guion_bajo).
/// </summary>
public sealed class WhatsAppTemplatesTests : E2eTestBase
{
    public WhatsAppTemplatesTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task CrearPlantilla_ApareceEnLaTabla()
    {
        RequireApp();
        var page = await LoginAsync();

        // Nombre visible tras normalizar (minusculas + guion_bajo).
        var rawName = $"E2E Plantilla {Sfx}";
        var normalized = $"e2e_plantilla_{Sfx.ToLowerInvariant()}";

        await page.GotoAsync("plantillas-whatsapp");
        await page.Locator(".module-head").GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Nueva plantilla" }).ClickAsync();

        var modal = page.Locator(".modal-dialog");
        await modal.WaitForAsync();

        await FieldIn(modal, "Nombre").Locator("input").FillAsync(rawName);
        await FieldIn(modal, "Cuerpo").Locator("textarea")
            .FillAsync("Hola {{cliente}}, gracias por contactar a {{empresa}}.");
        // La linea demo (primer option no vacio) queda preseleccionada; se ancla explicito por indice.
        await FieldIn(modal, "Linea de WhatsApp").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Index = 1 });

        await modal.GetByRole(AriaRole.Button,
            new LocatorGetByRoleOptions { Name = "Crear plantilla" }).ClickAsync();
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var row = page.Locator("table.wa-templates-table tbody tr").Filter(
            new LocatorFilterOptions { HasText = normalized });
        await Assertions.Expect(row).ToBeVisibleAsync();
        // La plantilla nueva empieza como borrador.
        await Assertions.Expect(row).ToContainTextAsync("Borrador");
    }
}
