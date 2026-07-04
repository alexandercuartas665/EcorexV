using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario del modulo de flujos (ADR-0022): abrir /flujos y ver el indice del
/// prototipo (KPIs + tarjetas con metricas), crear un flujo nuevo (borrador minimo
/// Inicio -> Fin), y en el editor canvas: agregar una tarea con la toolbar, conectarla
/// con la herramienta de conexion, renombrarla desde el panel de detalle, guardar,
/// cerrar y REABRIR para verificar que el grafo persistio en la base real.
/// </summary>
public sealed class FlowsEditorTests : E2eTestBase
{
    public FlowsEditorTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Flujos_CrearBorrador_AgregarConectarRenombrar_PersisteTrasReabrir()
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
        // Borrador minimo: Inicio -> Fin.
        await Assertions.Expect(page.Locator(".fe-stats")).ToContainTextAsync("2 nodos - 1 conexiones");

        // 3. Agregar tarea con la toolbar flotante (queda seleccionada).
        await page.ClickAsync(".fe-tool[title='Agregar tarea']");
        await Assertions.Expect(page.Locator(".fe-stats")).ToContainTextAsync("3 nodos - 1 conexiones");
        var task = page.Locator(".fe-node.rect");
        await Assertions.Expect(task).ToHaveTextAsync("Nueva tarea");

        // 4. Renombrar desde el panel de detalle (persiste en el change/blur).
        await page.FillAsync(".fe-panel-input", taskName);
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(task).ToHaveTextAsync(taskName);

        // 5. Conectar: herramienta de conexion, clic origen (Inicio) y destino (la tarea).
        await page.ClickAsync(".fe-tool[title='Conectar nodos']");
        await Assertions.Expect(page.Locator(".fe-hint")).ToHaveTextAsync("Clic en el nodo origen");
        await page.ClickAsync(".fe-node.start");
        await Assertions.Expect(page.Locator(".fe-hint")).ToHaveTextAsync("Clic en el nodo destino");
        await task.ClickAsync();
        await Assertions.Expect(page.Locator(".fe-stats")).ToContainTextAsync("3 nodos - 2 conexiones");

        // 6. Guardar cambios y volver al indice: la tarjeta refleja los 3 nodos.
        await page.ClickAsync(".fe-btn-brand");
        await Assertions.Expect(page.Locator(".fe-notice")).ToContainTextAsync("Cambios guardados");
        await page.ClickAsync(".fe-head-actions button:has-text('Cerrar')");
        var card = page.Locator(".fl-card", new PageLocatorOptions { HasText = flowName });
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card).ToContainTextAsync("Borrador");
        await Assertions.Expect(card).ToContainTextAsync("3 nodos");

        // 7. REABRIR el editor: el grafo persistio (tarea renombrada + 2 conexiones).
        await card.ClickAsync();
        await Assertions.Expect(page.Locator(".fe-shell")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".fe-stats")).ToContainTextAsync("3 nodos - 2 conexiones");
        await Assertions.Expect(page.Locator(".fe-node.rect")).ToHaveTextAsync(taskName);
    }
}
