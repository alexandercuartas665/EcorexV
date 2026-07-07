using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario del modulo de flujos. ADR-0034: el EDITOR ahora es bpmn-js embebido
/// (canvas propio reemplazado). Se abre /flujos y se ve el indice del prototipo
/// (KPIs + tarjetas con metricas), se crea un flujo nuevo (borrador minimo
/// Inicio -> Fin) y en el editor bpmn-js se agrega una tarea y se conecta desde
/// el startEvent, se guarda, se cierra y se REABRE para verificar que el grafo
/// persistio en la base real (re-hidratado en bpmn-js).
///
/// NOTA (ADR-0034 / vault): el click programatico de la PALETA de bpmn-js NO
/// dispara igual que el mouse real de Playwright, por eso el paso "agregar tarea
/// + conectar" se hace de forma DETERMINISTA por la API del modeler
/// (elementFactory + modeling) a traves del puente window.ecorexBpmnE2E que
/// expone el modulo interop. El resto del flujo (indice, crear, guardar, reabrir)
/// sigue siendo interaccion de UI real.
/// </summary>
public sealed class FlowsEditorTests : E2eTestBase
{
    public FlowsEditorTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Flujos_CrearBorrador_AgregarConectar_PersisteTrasReabrir()
    {
        RequireApp();
        var page = await LoginAsync();
        var flowName = $"E2E editor {Sfx}";
        var taskName = $"Tarea E2E {Sfx}";

        // 1. Indice /flujos: header del prototipo, 4 KPIs y tarjetas sembradas.
        await page.GotoAsync("flujos");
        await Assertions.Expect(page.Locator("h1.fl-title")).ToHaveTextAsync("Flujos del proceso");
        await Assertions.Expect(page.Locator(".fl-kpi")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator(".fl-card").First).ToBeVisibleAsync();
        // El seeder deja COT-COM "En marcha" y el borrador demo "Mantenimiento y soporte".
        await Assertions.Expect(page.Locator(".fl-card", new PageLocatorOptions { HasText = "COT-COM" }))
            .ToContainTextAsync("En marcha");

        // 2. Nuevo flujo (modal del prototipo) -> crea el borrador y abre el editor.
        await page.ClickAsync("button.fl-btn-new");
        await page.FillAsync(".fl-modal input.fl-input", flowName);
        await page.Locator(".fl-modal select.fl-input").First.SelectOptionAsync("Operaciones");
        await page.ClickAsync(".fl-modal .fl-btn-brand");
        await Assertions.Expect(page.Locator(".fe-shell")).ToBeVisibleAsync();

        // 3. Esperar a que bpmn-js este listo y traiga el borrador minimo Inicio -> Fin.
        await WaitBpmnReadyAsync(page);
        await AssertCountsAsync(page, startEvents: 1, tasks: 0, endEvents: 1, flows: 1);

        // 4. Agregar tarea y conectarla desde el startEvent (API del modeler, determinista).
        var newTaskId = await page.EvaluateAsync<string>(
            "([name]) => window.ecorexBpmnE2E.addTaskAndConnect('bpmn-canvas', name)",
            new object[] { taskName });
        Assert.False(string.IsNullOrWhiteSpace(newTaskId), "No se creo la tarea en el canvas bpmn-js.");
        await AssertCountsAsync(page, startEvents: 1, tasks: 1, endEvents: 1, flows: 2);
        // La barra de estado del prototipo refleja el ULTIMO GUARDADO (aun 2 nodos - 1 conexiones).
        await Assertions.Expect(page.Locator(".fe-stats")).ToContainTextAsync("2 nodos - 1 conexiones");

        // 5. Guardar cambios: exportXml -> SaveBpmnAsync re-sincroniza nodos/aristas/layout.
        await page.ClickAsync(".fe-btn-brand");
        await Assertions.Expect(page.Locator(".fe-notice")).ToContainTextAsync("Cambios guardados");
        // Tras guardar, la barra ya refleja los 3 nodos - 2 conexiones persistidos.
        await Assertions.Expect(page.Locator(".fe-stats")).ToContainTextAsync("3 nodos - 2 conexiones");

        // 6. Cerrar y volver al indice: la tarjeta refleja los 3 nodos.
        await page.ClickAsync(".fe-head-actions button:has-text('Cerrar')");
        var card = page.Locator(".fl-card", new PageLocatorOptions { HasText = flowName });
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card).ToContainTextAsync("Borrador");
        await Assertions.Expect(card).ToContainTextAsync("3 nodos");

        // 7. REABRIR el editor: el grafo persistio en la BD y se re-hidrata en bpmn-js
        //    (3 flow-nodes + 2 sequenceFlows), con la tarea nombrada.
        await card.ClickAsync();
        await Assertions.Expect(page.Locator(".fe-shell")).ToBeVisibleAsync();
        await WaitBpmnReadyAsync(page);
        await AssertCountsAsync(page, startEvents: 1, tasks: 1, endEvents: 1, flows: 2);
        await Assertions.Expect(page.Locator(".fe-stats")).ToContainTextAsync("3 nodos - 2 conexiones");

    }

    /// <summary>Espera a que el modulo interop registre la instancia del canvas.</summary>
    private static async Task WaitBpmnReadyAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => window.ecorexBpmnE2E && window.ecorexBpmnE2E.ready('bpmn-canvas')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    /// <summary>Verifica los conteos por tipo en el elementRegistry de bpmn-js.</summary>
    private static async Task AssertCountsAsync(IPage page, int startEvents, int tasks, int endEvents, int flows)
    {
        Assert.Equal(startEvents, await CountAsync(page, "bpmn:StartEvent"));
        Assert.Equal(tasks, await CountAsync(page, "bpmn:Task"));
        Assert.Equal(endEvents, await CountAsync(page, "bpmn:EndEvent"));
        Assert.Equal(flows, await CountAsync(page, "bpmn:SequenceFlow"));
    }

    private static async Task<int> CountAsync(IPage page, string type)
        => await page.EvaluateAsync<int>(
            "(t) => window.ecorexBpmnE2E.count('bpmn-canvas', t)", type);
}
