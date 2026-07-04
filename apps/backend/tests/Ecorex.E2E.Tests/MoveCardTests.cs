using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (c), ola 2: mover la tarjeta entre COLUMNAS del tablero desde el detalle
/// (dropdown "Mover a", misma ruta de servicio MoveTaskAsync que el drag and drop),
/// NO por drag and drop: el DnD nativo HTML5 es notoriamente fragil de automatizar
/// (Playwright no dispara dragstart/dragover/drop de forma fiable sobre Blazor Server).
/// Verifica ademas que el pill de Estado (maquina de estados) sigue operativo.
/// </summary>
public sealed class MoveCardTests : E2eTestBase
{
    public MoveCardTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Detalle_DropdownMoverA_CambiaLaColumnaDeLaTarjeta()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E mover {Sfx}";

        await OpenBoardAsync(page, "Comercial - Requerimiento Infraestructura");
        await QuickCreateTaskAsync(page, title, column: "Por hacer",
            typeLabel: "Gestion Humana / Solicitud");

        await OpenTaskDetailAsync(page, title);

        // Sin encargado y sin flujo: la tarea nace Pendiente (maquina de estados intacta).
        var pill = StatusPill(page);
        await Assertions.Expect(pill.Locator(".tk-status")).ToHaveTextAsync("Pendiente");

        // Dropdown "Mover a": ofrece las 4 columnas del tablero y mueve a "En progreso".
        var moveTo = page.Locator("button.tk-moveto");
        await Assertions.Expect(moveTo).ToContainTextAsync("Por hacer");
        await moveTo.ClickAsync();
        var options = page.Locator(".tk-pill-pop .tk-pill-item");
        await Assertions.Expect(options).ToHaveTextAsync(new[] { "Por hacer", "En progreso", "En revision", "Completado" });

        await options.Filter(new LocatorFilterOptions { HasText = "En progreso" }).ClickAsync();
        await Assertions.Expect(moveTo).ToContainTextAsync("En progreso");

        // Al cerrar, la tarjeta quedo en la columna "En progreso" y salio de "Por hacer".
        await CloseTaskDetailAsync(page);
        await Assertions.Expect(CardIn(BoardColumn(page, "En progreso"), title)).ToBeVisibleAsync();
        await Assertions.Expect(CardIn(BoardColumn(page, "Por hacer"), title)).ToHaveCountAsync(0);
    }
}
