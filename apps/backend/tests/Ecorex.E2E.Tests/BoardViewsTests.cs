using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenarios de la ola 3 (vistas CALENDARIO y GANTT del detalle de tablero,
/// pantalla 'work' del prototipo ECOREX.dc.html):
/// (a) Calendario: una tarea con fecha limite aparece como chip en la celda de su
///     DueDate (mes navegable) y el click en el chip abre el detalle de la tarea;
///     ademas la celda de HOY existe resaltada en el mes actual.
/// (b) Gantt: la tarea aparece como fila con su barra (progreso N/M del checklist)
///     y la linea vertical de HOY es visible en la ventana de 14 dias por defecto.
/// La tarea del calendario se crea con vencimiento el dia 25 del MES SIGUIENTE para
/// no competir con el tope de 3 chips por celda del seed/corridas previas.
/// </summary>
public sealed class BoardViewsTests : E2eTestBase
{
    public BoardViewsTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Calendario_ChipEnElDiaDelDueDate_YClickAbreElDetalle()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E cal {Sfx}";
        // Dia pseudo-unico (1..28) del MES SIGUIENTE derivado del sufijo de la corrida:
        // evita chocar con el tope de 3 chips por celda al acumular corridas en la BD dev.
        var day = 1 + (int)(Convert.ToUInt32(Sfx[..4], 16) % 28);
        var due = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).AddDays(day - 1);

        await OpenBoardAsync(page, "Comercial - Requerimiento Infraestructura");
        await QuickCreateTaskAsync(page, title, column: "Por hacer", dueDate: due);

        // Cambiar a la vista Calendario: header con el mes actual y celda de HOY resaltada.
        await page.Locator(".ab-tab").Filter(new LocatorFilterOptions { HasText = "Calendario" }).ClickAsync();
        await page.Locator(".ab-cal").WaitForAsync();
        await Assertions.Expect(page.Locator(".ab-cal-cell.today")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".ab-cal-num.today")).ToHaveTextAsync(DateTime.Today.Day.ToString());

        // Navegar al mes siguiente: el chip de la tarea esta en la celda de su dia.
        await page.Locator(".ab-cal-btn[title='Mes siguiente']").ClickAsync();
        var chip = page.Locator(".ab-cal-chip").Filter(new LocatorFilterOptions { HasText = title });
        await Assertions.Expect(chip).ToBeVisibleAsync();
        var cell = page.Locator(".ab-cal-cell").Filter(new LocatorFilterOptions
        {
            Has = page.Locator(".ab-cal-chip", new PageLocatorOptions { HasText = title })
        });
        await Assertions.Expect(cell.Locator(".ab-cal-num")).ToHaveTextAsync(day.ToString());

        // Click en el chip -> se abre el TaskDetailModal de esa tarea.
        await chip.ClickAsync();
        var detail = page.Locator(".tk-detail");
        await detail.WaitForAsync();
        await Assertions.Expect(detail).ToContainTextAsync(title);
        await CloseTaskDetailAsync(page);

        // Limpieza que ademas cubre el menu "..." de la tarjeta (ola 3): volver al
        // tablero y ARCHIVAR la tarea con confirmacion; la tarjeta desaparece.
        await page.Locator(".ab-tab").Filter(new LocatorFilterOptions { HasText = "Tablero" }).ClickAsync();
        var card = CardIn(BoardColumn(page, "Por hacer"), title);
        await card.Locator(".ab-card-menu").ClickAsync();
        await page.Locator(".ab-dd-opt").Filter(new LocatorFilterOptions { HasText = "Archivar" }).ClickAsync();
        await page.Locator(".ab-dd-opt").Filter(new LocatorFilterOptions { HasText = "Confirmar archivado" }).ClickAsync();
        await Assertions.Expect(page.Locator(".tk-toast.ok").Filter(new LocatorFilterOptions { HasText = "archivada" })).ToBeVisibleAsync();
        await Assertions.Expect(card).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Gantt_BarraConProgresoYLineaDeHoy()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E gantt {Sfx}";

        await OpenBoardAsync(page, "Comercial - Requerimiento Infraestructura");
        // Vence HOY: la barra (CreatedAt -> DueDate) cae dentro de la ventana de 14
        // dias por defecto, que siempre contiene a hoy.
        await QuickCreateTaskAsync(page, title, column: "Por hacer", dueDate: DateTime.Today);

        await page.Locator(".ab-tab").Filter(new LocatorFilterOptions { HasText = "Gantt" }).ClickAsync();
        await page.Locator(".ab-gantt").WaitForAsync();

        // Banda superior: etiqueta "TAREA - {MES} {ANO}" y 14 celdas de dia.
        await Assertions.Expect(page.Locator(".ab-gantt-lbl")).ToContainTextAsync("TAREA -");
        await Assertions.Expect(page.Locator(".ab-gantt-day")).ToHaveCountAsync(14);

        // Fila de la tarea con su barra y el progreso N/M (sin checklist: 0/0).
        var row = page.Locator(".ab-gantt-row").Filter(new LocatorFilterOptions { HasText = title });
        await Assertions.Expect(row).ToBeVisibleAsync();
        var bar = row.Locator(".ab-gantt-bar");
        await Assertions.Expect(bar).ToBeVisibleAsync();
        await Assertions.Expect(bar).ToHaveTextAsync("0/0");

        // Linea vertical de HOY visible en la fila.
        await Assertions.Expect(row.Locator(".ab-gantt-today")).ToBeVisibleAsync();

        // Click en la barra -> se abre el TaskDetailModal de esa tarea.
        await bar.ClickAsync();
        var detail = page.Locator(".tk-detail");
        await detail.WaitForAsync();
        await Assertions.Expect(detail).ToContainTextAsync(title);
    }
}
