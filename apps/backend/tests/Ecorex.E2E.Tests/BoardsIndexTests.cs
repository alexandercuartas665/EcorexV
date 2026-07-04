using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenarios de la ola 2 (experiencia de tableros del prototipo, pantalla 'work'):
/// (a) el indice de /actividades muestra los KPIs y los 3 tableros demo y abre
///     PRY-0042 con sus 4 columnas propias y las pestanas de alcance;
/// (b) los chips de filtro por columna funcionan y se limpian;
/// (c) el checklist del detalle alimenta la barra "Avance" del sidebar y el
///     "Progreso" de la tarjeta del tablero.
/// Las aserciones de conteo son >= (la suite comparte BD y otros tests crean tareas).
/// </summary>
public sealed class BoardsIndexTests : E2eTestBase
{
    public BoardsIndexTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Indice_MuestraKpisYTableros_YAbreElDetalleConFiltros()
    {
        RequireApp();
        var page = await LoginAsync();
        await page.GotoAsync("actividades");

        // Cabecera e indicadores del indice.
        await Assertions.Expect(page.Locator("h1.ab-title")).ToHaveTextAsync("Tableros");
        await Assertions.Expect(page.Locator(".ab-kpi")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator(".ab-kpi-lbl").Nth(0)).ToHaveTextAsync("Tableros");

        // Los 3 tableros demo del seeder estan en el grid.
        var cards = page.Locator(".ab-board-card");
        await Assertions.Expect(cards.Filter(new LocatorFilterOptions { HasText = "Comercial - Requerimiento Infraestructura" })).ToBeVisibleAsync();
        await Assertions.Expect(cards.Filter(new LocatorFilterOptions { HasText = "Marketing - Lanzamiento Q3" })).ToBeVisibleAsync();
        await Assertions.Expect(cards.Filter(new LocatorFilterOptions { HasText = "Soporte - Mesa de ayuda" })).ToBeVisibleAsync();
        await Assertions.Expect(cards.Filter(new LocatorFilterOptions { HasText = "PRY-0042" })).ToBeVisibleAsync();

        // Abrir PRY-0042: titulo, subtitulo literal, 4 columnas propias y 3 alcances.
        await cards.Filter(new LocatorFilterOptions { HasText = "PRY-0042" }).ClickAsync();
        await page.Locator(".ab-kanban").WaitForAsync();
        await Assertions.Expect(page.Locator("h1.ab-dtitle")).ToHaveTextAsync("Comercial - Requerimiento Infraestructura");
        await Assertions.Expect(page.Locator(".ab-dsub")).ToHaveTextAsync("Toca cualquier valor para filtrar las tareas del tablero.");
        await Assertions.Expect(page.Locator(".ab-col-name")).ToHaveTextAsync(new[] { "Por hacer", "En progreso", "En revision", "Completado" });
        await Assertions.Expect(page.Locator(".ab-scope")).ToHaveCountAsync(3);

        // Chip de Estado "Completado": filtra el kanban (la columna "Por hacer" queda vacia).
        var chipCompletado = page.Locator(".ab-chip").Filter(new LocatorFilterOptions { HasText = "Completado" });
        await chipCompletado.ClickAsync();
        await Assertions.Expect(chipCompletado).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bon\\b"));
        await Assertions.Expect(BoardColumn(page, "Por hacer").Locator(".ab-card")).ToHaveCountAsync(0);

        // Alcance "No asignados" combinado con el chip: contadores visibles y kanban coherente.
        await page.Locator(".ab-scope").Filter(new LocatorFilterOptions { HasText = "No asignados" }).ClickAsync();
        await Assertions.Expect(page.Locator(".ab-scope").Filter(new LocatorFilterOptions { HasText = "No asignados" }))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bon\\b"));

        // Limpiar: los chips se apagan y "Por hacer" vuelve a tener tarjetas del seed.
        await page.Locator(".ab-scope").Filter(new LocatorFilterOptions { HasText = "Todas del equipo" }).ClickAsync();
        await page.Locator(".ab-clear-inline").ClickAsync();
        await Assertions.Expect(page.Locator(".ab-chip.on")).ToHaveCountAsync(0);
        // First + ToBeVisible reintenta hasta que el reload pinte las tarjetas del seed.
        await Assertions.Expect(BoardColumn(page, "Por hacer").Locator(".ab-card").First).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task Detalle_ChecklistToggle_ActualizaAvanceYProgresoDeLaTarjeta()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E checklist {Sfx}";

        await OpenBoardAsync(page, "Comercial - Requerimiento Infraestructura");
        await QuickCreateTaskAsync(page, title, column: "Por hacer",
            typeLabel: "Gestion Humana / Solicitud");
        await OpenTaskDetailAsync(page, title);

        // Agregar un item de chequeo y completarlo: la barra "Avance" del sidebar lo refleja.
        var checklist = page.Locator(".tk-checklist");
        await checklist.Locator("input").FillAsync($"Item E2E {Sfx}");
        await checklist.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Agregar item" }).ClickAsync();
        await Assertions.Expect(checklist.Locator(".tk-check-row")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".tk-avance-row strong")).ToHaveTextAsync("0/1");

        await checklist.Locator(".tk-check-box").ClickAsync();
        await Assertions.Expect(checklist.Locator(".tk-check-box.done")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".tk-avance-row strong")).ToHaveTextAsync("1/1");

        // Al cerrar, el "Progreso" de la tarjeta del tablero quedo en 1/1.
        await CloseTaskDetailAsync(page);
        var card = CardIn(BoardColumn(page, "Por hacer"), title);
        await Assertions.Expect(card.Locator(".ab-card-progrow strong")).ToHaveTextAsync("1/1");
    }
}
