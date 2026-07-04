using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (c): mover la tarjeta Pendiente -> Activa desde el DETALLE (dropdown de
/// transiciones validas de la maquina de estados), NO por drag and drop: el DnD nativo
/// HTML5 del kanban es notoriamente fragil de automatizar (Playwright no dispara la
/// secuencia dragstart/dragover/drop de forma fiable sobre Blazor Server) y el dropdown
/// ejercita la MISMA TaskItemStateMachine por la misma ruta de servicio (ver ADR-0019).
/// </summary>
public sealed class MoveCardTests : E2eTestBase
{
    public MoveCardTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Detalle_DropdownDeEstado_MuevePendienteAActiva()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E mover {Sfx}";
        await CreateActivityAsync(page, "Gestion Humana", "Solicitud", title);

        await OpenTaskDetailAsync(page, title);

        // El pill de estado muestra Pendiente y su menu solo ofrece transiciones validas
        // desde Pending (Activa / En proceso / Suspendida; nunca Terminada ni Cerrada).
        var pill = StatusPill(page);
        await Assertions.Expect(pill.Locator(".tk-status")).ToHaveTextAsync("Pendiente");
        await pill.ClickAsync();
        var options = page.Locator(".tk-pill-pop .tk-pill-item");
        await Assertions.Expect(options).ToHaveTextAsync(new[] { "Activa", "En proceso", "Suspendida" });

        await options.Filter(new LocatorFilterOptions { HasText = "Activa" }).ClickAsync();
        await Assertions.Expect(pill.Locator(".tk-status")).ToHaveTextAsync("Activa");

        // Al cerrar, la tarjeta quedo en la columna Activa y ya no esta en Pendiente.
        await CloseTaskDetailAsync(page);
        await Assertions.Expect(CardIn(KanbanColumn(page, "Activa"), title)).ToBeVisibleAsync();
        await Assertions.Expect(CardIn(KanbanColumn(page, "Pendiente"), title)).ToHaveCountAsync(0);
    }
}
