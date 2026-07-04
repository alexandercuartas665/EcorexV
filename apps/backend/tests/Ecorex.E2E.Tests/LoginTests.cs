using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>Escenario (a): login demo -> /inicio con saludo y KPIs visibles.</summary>
public sealed class LoginTests : E2eTestBase
{
    public LoginTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Login_ConCredencialesDemo_AterrizaEnInicio_ConSaludoYKpis()
    {
        RequireApp();
        var page = await LoginAsync();

        Assert.Contains("/inicio", page.Url);

        // Saludo por franja horaria + nombre ("Buenos dias, Demo" / "Buenas tardes..." / "Buenas noches...").
        await Assertions.Expect(page.Locator("h1.dash-title"))
            .ToContainTextAsync(new Regex("^(Buenos dias|Buenas tardes|Buenas noches)"));

        // Las 4 KPI cards del prototipo, con valor numerico.
        await Assertions.Expect(page.Locator(".dash-kpi")).ToHaveCountAsync(4);
        foreach (var label in new[] { "Tareas activas", "Proyectos en curso", "Flujos ejecutandose", "Alertas" })
        {
            await Assertions.Expect(page.Locator(".dash-kpi-label", new PageLocatorOptions { HasText = label }))
                .ToBeVisibleAsync();
        }
        foreach (var value in await page.Locator(".dash-kpi-value").AllInnerTextsAsync())
        {
            Assert.True(int.TryParse(value.Trim(), out _), $"KPI sin valor numerico: '{value}'");
        }

        // El resumen del saludo menciona el workspace demo.
        await Assertions.Expect(page.Locator(".dash-sub")).ToContainTextAsync("pendientes en");
    }
}
