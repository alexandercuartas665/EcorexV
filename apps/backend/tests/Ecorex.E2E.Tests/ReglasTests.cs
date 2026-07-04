using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario del modulo de GESTION DE REGLAS (/reglas, ADR-0023, proto gen_reglas):
/// crear una regla NOTIFICAR con + Nueva regla, seleccionarla en el sidebar (lista plana),
/// editar la prioridad, Validar el PARAM_XML (representacion editable del ParamsJson),
/// Ejecutar la regla (prueba real) y ver la entrada en "Historial reciente".
/// Selectores por clases estables del modulo (rg-*, rule-item), sin data-testid.
/// </summary>
public sealed class ReglasTests : E2eTestBase
{
    public ReglasTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task SeleccionarRegla_EditarPrioridad_ValidarXml_Ejecutar_YVerHistorial()
    {
        RequireApp();
        var page = await LoginAsync();
        var ruleName = $"Regla E2E {Sfx}";

        // 1. El modulo carga con el layout de 3 paneles y KPIs reales.
        await page.GotoAsync("reglas");
        await page.Locator(".rg-layout").WaitForAsync();
        await Assertions.Expect(page.Locator(".rg-kpi")).ToHaveCountAsync(4);
        // "Importar XML" queda deshabilitado (sin formato de import definido).
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Importar XML" }))
            .ToBeDisabledAsync();

        // 2. Crear la regla (documento demo RUL-005 ya seleccionado por la primera regla
        //    del sidebar; si no hubiera seleccion apareceria el mini modal de documento).
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "+ Nueva regla" }).ClickAsync();
        if (await page.Locator(".rg-pick-modal").IsVisibleAsync())
        {
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Continuar" }).ClickAsync();
        }
        await Assertions.Expect(page.Locator(".rg-rule-title")).ToContainTextAsync("Nueva regla");

        var nombre = MetaField(page, "Nombre").Locator("input");
        await nombre.FillAsync(ruleName);
        await nombre.BlurAsync();
        await MetaField(page, "Verbo").Locator("select")
            .SelectOptionAsync(new SelectOptionValue { Value = "NOTIFICAR" });
        // Parametro del verbo en la "Vista renderizada" (form dinamico del descriptor).
        var mensaje = page.Locator(".rg-param-field")
            .Filter(new LocatorFilterOptions { HasText = "(message)" }).Locator("input");
        await mensaje.FillAsync($"Hola desde E2E {Sfx}");
        await mensaje.BlurAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Crear regla" }).ClickAsync();
        await Assertions.Expect(page.Locator(".rg-status-strip")).ToContainTextAsync("guardados");

        // 3. Seleccionar la regla en el SIDEBAR (lista plana) y verla activa.
        var item = page.Locator(".rule-item").Filter(new LocatorFilterOptions { HasText = ruleName });
        await item.ClickAsync();
        await Assertions.Expect(page.Locator(".rule-item.active")).ToContainTextAsync(ruleName);
        await Assertions.Expect(page.Locator(".rg-rule-title")).ToContainTextAsync(ruleName);

        // 4. Editar la prioridad (SortOrder).
        var prioridad = MetaField(page, "Prioridad").Locator("input");
        await prioridad.FillAsync("7");
        await prioridad.BlurAsync();

        // 5. Validar el PARAM_XML generado desde ParamsJson (contrato del proto).
        await Assertions.Expect(page.Locator(".rg-code-input"))
            .ToHaveValueAsync(new System.Text.RegularExpressions.Regex("PROCESO>NOTIFICAR</PROCESO"));
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Validar" }).ClickAsync();
        await Assertions.Expect(page.Locator(".rg-ok")).ToContainTextAsync("XML valido");

        // 6. Ejecutar la regla (guarda y corre: la ejecucion SIEMPRE queda en historial).
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Ejecutar regla" }).ClickAsync();
        await Assertions.Expect(page.Locator(".rg-test-result")).ToContainTextAsync("Exito");

        // 7. La entrada aparece en "Historial reciente" con la regla y el disparador Manual.
        var recent = page.Locator(".rg-history-card").First.Locator(".rg-history-item")
            .Filter(new LocatorFilterOptions { HasText = ruleName });
        await Assertions.Expect(recent.First).ToContainTextAsync("Manual");
        await Assertions.Expect(recent.First).ToContainTextAsync("Exito");

        // 8. La prioridad editada quedo persistida (el guardado ocurrio antes de ejecutar).
        await Assertions.Expect(MetaField(page, "Prioridad").Locator("input")).ToHaveValueAsync("7");

        // 9. El tab Historial (contador > 0) muestra la misma ejecucion en formato lista.
        await page.Locator(".rg-tab").Filter(new LocatorFilterOptions { HasText = "Historial" }).ClickAsync();
        await Assertions.Expect(page.Locator(".rg-history-item").Filter(
            new LocatorFilterOptions { HasText = ruleName }).First).ToBeVisibleAsync();
    }

    /// <summary>Fila .rg-meta del editor anclada por el texto de su label.</summary>
    private static ILocator MetaField(IPage page, string label)
        => page.Locator(".rg-meta").Filter(new LocatorFilterOptions
        {
            Has = page.Locator("label", new PageLocatorOptions { HasText = label })
        }).First;
}
