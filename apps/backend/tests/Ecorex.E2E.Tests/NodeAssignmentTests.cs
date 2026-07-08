using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario de asignacion por nodo (ADR-0035, ola F1): en el editor bpmn-js se crea un
/// borrador, se agrega una tarea, se marca "Permite asignacion manual", se selecciona el nodo
/// (via el puente window.ecorexBpmnE2E.select, porque el click de mouse de bpmn-js no dispara
/// igual bajo Playwright, como en FlowsEditorTests) y en el acordeon "Asignar usuarios" se
/// elige una Dependencia/Cargo y se asigna. Luego se reabre el acordeon y se verifica que la
/// asignacion persistio. La resolucion efectiva del paso y la bandeja son la ola F2.
/// </summary>
public sealed class NodeAssignmentTests : E2eTestBase
{
    public NodeAssignmentTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Flujos_AsignarDependenciaPorNodo_PersisteTrasReabrir()
    {
        RequireApp();
        var page = await LoginAsync();
        var flowName = $"E2E asignacion {Sfx}";
        var taskName = $"Paso asignable {Sfx}";

        // 1. Nuevo flujo (borrador minimo Inicio -> Fin) y abrir el editor.
        await page.GotoAsync("flujos");
        await page.ClickAsync("button.fl-btn-new");
        await page.FillAsync(".fl-modal input.fl-input", flowName);
        await page.ClickAsync(".fl-modal .fl-btn-brand");
        await Assertions.Expect(page.Locator(".fe-shell")).ToBeVisibleAsync();
        await WaitBpmnReadyAsync(page);

        // 2. Agregar una tarea y guardar (el panel opera sobre el ULTIMO grafo guardado).
        var taskId = await page.EvaluateAsync<string>(
            "([name]) => window.ecorexBpmnE2E.addTaskAndConnect('bpmn-canvas', name)",
            new object[] { taskName });
        Assert.False(string.IsNullOrWhiteSpace(taskId));
        await page.ClickAsync(".fe-btn-brand");
        await Assertions.Expect(page.Locator(".fe-notice")).ToContainTextAsync("Cambios guardados");

        // 3. Seleccionar el nodo Task (puente E2E) -> Blazor carga el panel del nodo.
        await SelectNodeAsync(page, taskId);
        // El panel muestra el nombre del nodo guardado (confirma la seleccion).
        await Assertions.Expect(page.Locator(".fe-panel-name")).ToContainTextAsync(taskName);

        // 4. Acordeon "Configuracion basica" (indice 0, abierto por defecto): marcar
        //    "Permite asignacion manual del paso".
        var allowsCheck = page.Locator(".fe-check input[type=checkbox]");
        await Assertions.Expect(allowsCheck).ToBeVisibleAsync();
        await allowsCheck.CheckAsync();
        // Reseleccionar para refrescar el panel con AllowsAssignment=true.
        await SelectNodeAsync(page, taskId);

        // 5. Acordeon "Asignar usuarios": abrirlo, elegir una dependencia/cargo y asignar.
        await page.ClickAsync(".fe-acc:has-text('Asignar usuarios')");
        var select = page.Locator(".fe-acc-body select.fe-input").First;
        await Assertions.Expect(select).ToBeVisibleAsync();
        // Elige la primera opcion real (indice 1: la opcion 0 es el placeholder).
        await select.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.ClickAsync(".fe-acc-body button.fe-add-dashed");

        // 6. La fila de la asignacion aparece (dependencia/cargo con conteo de candidatos).
        await Assertions.Expect(page.Locator(".fe-acc-body .fe-row")).ToHaveCountAsync(1);

        // 7. Reseleccionar el nodo (recarga las policies desde la BD): la asignacion persistio.
        //    El acordeon "Asignar usuarios" sigue abierto, asi que su cuerpo se re-renderiza.
        await SelectNodeAsync(page, taskId);
        await Assertions.Expect(page.Locator(".fe-acc-body .fe-row")).ToHaveCountAsync(1);
    }

    private static async Task SelectNodeAsync(IPage page, string nodeId)
    {
        await page.EvaluateAsync(
            "([id]) => window.ecorexBpmnE2E.select('bpmn-canvas', id)",
            new object[] { nodeId });
        // Deja que el circuito Blazor procese OnElementSelected antes de tocar el panel.
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task WaitBpmnReadyAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => window.ecorexBpmnE2E && window.ecorexBpmnE2E.ready('bpmn-canvas')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
    }
}
