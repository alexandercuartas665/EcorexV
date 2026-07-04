using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (d): registrar una entrada MANUAL de 0:30 con nota en el worklog del
/// detalle y verificar el total ("30m" segun TaskUi.FormatDuration) y el historial.
/// </summary>
public sealed class WorklogTests : E2eTestBase
{
    public WorklogTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Detalle_EntradaManual30Min_ActualizaTotalEHistorial()
    {
        RequireApp();
        var page = await LoginAsync();
        var title = $"E2E worklog {Sfx}";
        var note = $"Avance E2E {Sfx}";
        await CreateActivityAsync(page, "Gestion Humana", "Solicitud", title);

        await OpenTaskDetailAsync(page, title);

        // Tarea recien creada: total en cero y sin historial.
        await Assertions.Expect(page.Locator(".tk-worklog-total")).ToContainTextAsync("0m");
        await Assertions.Expect(page.Locator(".tk-worklog-row")).ToHaveCountAsync(0);

        // Entrada manual 0:30 con nota.
        var manual = page.Locator(".tk-manual");
        await manual.Locator("input.tk-manual-num").Nth(0).FillAsync("0");
        await manual.Locator("input.tk-manual-num").Nth(1).FillAsync("30");
        await manual.Locator("input.tk-timer-note").FillAsync(note);
        await manual.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Guardar" }).ClickAsync();

        // Total actualizado en la cabecera del worklog y en el pill del hero.
        await Assertions.Expect(page.Locator(".tk-worklog-total")).ToContainTextAsync("30m");

        // Entrada en el historial: duracion, tipo Manual y la nota.
        var row = page.Locator(".tk-worklog-row").First;
        await Assertions.Expect(row.Locator(".tk-worklog-dur")).ToHaveTextAsync("30m");
        await Assertions.Expect(row.Locator(".tk-worklog-kind")).ToHaveTextAsync("Manual");
        await Assertions.Expect(row.Locator(".tk-worklog-note")).ToHaveTextAsync(note);
    }
}
