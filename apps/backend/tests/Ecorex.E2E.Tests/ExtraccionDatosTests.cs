using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario de EXTRACCION DE DATOS (/extraccion-datos, modulo 000730, ADR-0025):
/// crear una fuente JSON apuntando al endpoint demo PROPIO de la consola
/// (/api/demo/scrape-sample, permitido por la excepcion loopback de Development),
/// ejecutarla, ver la preview con los items extraidos y el historial con el dot verde.
/// Selectores por clases estables del modulo (xd-*).
/// </summary>
public sealed class ExtraccionDatosTests : E2eTestBase
{
    public ExtraccionDatosTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task CrearFuenteDemo_Ejecutar_VerPreview_YHistorialVerde()
    {
        RequireApp();
        var page = await LoginAsync();

        var sourceName = $"Fuente E2E {Sfx}";

        // 1. La pagina real carga con la estructura del proto: topbar + sidebar + hero.
        await page.GotoAsync("extraccion-datos");
        await page.Locator(".xd-layout").WaitForAsync();
        await Assertions.Expect(page.Locator(".xd-mod-code")).ToContainTextAsync("MOD 000730");

        // 2. Crear la fuente demo: JSON contra el endpoint propio de la app.
        await page.Locator(".xd-new-btn").ClickAsync();
        await page.FillAsync("#xd-f-name", sourceName);
        await page.FillAsync("#xd-f-url", $"{Fx.BaseUrl}/api/demo/scrape-sample");
        await page.SelectOptionAsync("#xd-f-kind", "Json");
        await page.Locator(".xd-save-btn").ClickAsync();
        await Assertions.Expect(page.Locator(".xd-flash.ok")).ToContainTextAsync("creada");

        // Aparece en el sidebar, seleccionada y activa.
        var item = page.Locator(".xd-item").Filter(new LocatorFilterOptions { HasText = sourceName });
        await Assertions.Expect(item).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".xd-hero-name")).ToHaveTextAsync(sourceName);

        // 3. Ejecutar ahora: la corrida corre contra /api/demo/scrape-sample (loopback dev).
        await page.Locator(".xd-hero-actions .xd-run").ClickAsync();
        await Assertions.Expect(page.Locator(".xd-flash.ok")).ToContainTextAsync("8 items");

        // 4. Preview con los items extraidos (tabla con columnas del JSON demo).
        await Assertions.Expect(page.Locator(".xd-num-badge").First).ToContainTextAsync("8 items");
        await Assertions.Expect(page.Locator(".xd-preview-table th").Filter(
            new LocatorFilterOptions { HasText = "sku" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".xd-preview-table tbody tr")).ToHaveCountAsync(8);
        await Assertions.Expect(page.Locator(".xd-preview-table")).ToContainTextAsync("Pintura interior blanca 1 gl");

        // Los KPIs del hero reflejan la corrida.
        await Assertions.Expect(page.Locator(".xd-kpi").Nth(2).Locator(".val")).ToHaveTextAsync("8");

        // 5. Historial con la corrida exitosa (pill con dot verde).
        var historyRow = page.Locator(".xd-exec-table tbody tr").First;
        await Assertions.Expect(historyRow.Locator(".xd-pill.ok")).ToContainTextAsync("Exitoso");
        await Assertions.Expect(historyRow.Locator("td.num")).ToHaveTextAsync("8");
    }
}
