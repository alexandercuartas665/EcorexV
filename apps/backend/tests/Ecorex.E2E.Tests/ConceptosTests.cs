using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario del modulo CONCEPTOS (/conceptos, 000270, proto tar_conceptos): abrir el
/// catalogo (split categorias/detalle + MOD 000270), crear un concepto con + Nuevo
/// concepto (modal de acordeones), verlo en el grid de su categoria y en el tab Detalle,
/// y comprobar que el combo "Tipo de actividad" del wizard de actividades lo ofrece.
/// Selectores por clases estables del modulo (cn-*), sin data-testid.
/// </summary>
public sealed class ConceptosTests : E2eTestBase
{
    public ConceptosTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task CrearConcepto_VerloEnCatalogo_YEnElComboDelWizard()
    {
        RequireApp();
        var page = await LoginAsync();
        var conceptName = $"Concepto E2E {Sfx}";

        // 1. El modulo carga con el split categorias/detalle y el badge del modulo legacy.
        await page.GotoAsync("conceptos");
        await page.Locator(".cn-split").WaitForAsync();
        await Assertions.Expect(page.Locator(".cn-mod-code")).ToContainTextAsync("000270");
        // "Exportar" queda deshabilitado (pendiente declarado).
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Exportar" }))
            .ToBeDisabledAsync();

        // 2. Crear el concepto en la categoria demo "Direccion Comercial".
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Nuevo concepto" }).ClickAsync();
        var modal = page.Locator(".cn-modal");
        await modal.WaitForAsync();
        await CnField(page, "Categoria").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = "Direccion Comercial" });
        var nombre = CnField(page, "Nombre").Locator("input");
        await nombre.FillAsync(conceptName);
        await nombre.BlurAsync();
        // El campo Codigo esta deshabilitado (gap declarado del modelo).
        await Assertions.Expect(CnField(page, "Codigo").Locator("input")).ToBeDisabledAsync();
        await modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Crear concepto" }).ClickAsync();

        // 3. Feedback de guardado y fila visible en el grid de la categoria.
        await Assertions.Expect(page.Locator(".cn-ok")).ToContainTextAsync("guardado");
        await Assertions.Expect(page.Locator(".cn-detail-title")).ToContainTextAsync("Direccion Comercial");
        var row = page.Locator(".cn-grid tbody tr").Filter(new LocatorFilterOptions { HasText = conceptName });
        await Assertions.Expect(row).ToHaveCountAsync(1);
        await Assertions.Expect(row.Locator(".cn-badge.on")).ToContainTextAsync("Activo");

        // 4. El tab Detalle (grid maestro Categoria x Concepto) tambien lo muestra.
        await page.Locator(".cn-tab").Filter(new LocatorFilterOptions { HasText = "Detalle" }).ClickAsync();
        await page.Locator(".cn-filter-input").FillAsync(conceptName);
        var detailRow = page.Locator(".cn-grid tbody tr").Filter(new LocatorFilterOptions { HasText = conceptName });
        await Assertions.Expect(detailRow).ToHaveCountAsync(1);
        await Assertions.Expect(detailRow).ToContainTextAsync("Direccion Comercial");

        // 5. El wizard de actividades ofrece el concepto nuevo en el combo de tipos.
        await page.GotoAsync("actividades");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Actividad completa" }).ClickAsync();
        var wizard = page.Locator(".tk-wizard");
        await wizard.WaitForAsync();
        await FieldIn(wizard, "Categoria").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Label = "Direccion Comercial" });
        var typeSelect = FieldIn(wizard, "Tipo de actividad").Locator("select");
        await Assertions.Expect(typeSelect.Locator("option").Filter(
            new LocatorFilterOptions { HasText = conceptName })).ToHaveCountAsync(1);
        // Y se puede seleccionar de verdad (queda como valor del combo).
        await typeSelect.SelectOptionAsync(new SelectOptionValue { Label = conceptName });
    }

    /// <summary>Fila .cn-field del modal de concepto anclada por el texto de su label.</summary>
    private static ILocator CnField(IPage page, string label)
        => page.Locator(".cn-field").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("label", new PageLocatorOptions { HasText = label })
        }).First;
}
