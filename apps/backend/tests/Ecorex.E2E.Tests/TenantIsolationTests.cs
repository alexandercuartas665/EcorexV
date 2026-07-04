using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// Escenario (g): aislamiento visual con un segundo usuario del tenant demo
/// (owner@sky-system.local segun el seed de DatabaseSeeder). NOTA: la BD dev actual se
/// sembro con una version anterior del seeder y solo tiene demo-admin@ecorex.tareas
/// como usuario del tenant SKY SYSTEM (el seed inicial solo corre con platform_users
/// vacia), asi que en ese entorno este caso se SALTA con motivo explicito en vez de
/// fingir cobertura. En una BD recien sembrada el caso corre completo.
/// </summary>
public sealed class TenantIsolationTests : E2eTestBase
{
    public TenantIsolationTests(E2eAppFixture fx) : base(fx)
    {
    }

    [SkippableFact]
    public async Task Login_ComoOwnerSkySystem_VeSoloElWorkspaceDeSuTenant()
    {
        RequireApp();
        var page = await NewPageAsync();

        var ok = await TryLoginAsync(page, "owner@sky-system.local", "Demo123*");
        Skip.If(!ok,
            "El seed de la BD dev no tiene owner@sky-system.local (la BD se sembro antes de que " +
            "existieran los 4 usuarios por rol; el seed inicial solo corre con platform_users vacia). " +
            "Unico usuario de tenant disponible: demo-admin@ecorex.tareas, ya cubierto por los demas " +
            "escenarios. Para ejercitar este caso: sembrar una BD limpia (docker compose down -v && up).");

        // Aterriza en el workspace del tenant demo, no en la consola de plataforma.
        Assert.Contains("/inicio", page.Url);
        await Assertions.Expect(page.Locator(".dash-sub")).ToContainTextAsync("SKY SYSTEM");

        // El kanban carga solo datos del tenant (sin fuga: nada del tenant interno
        // "Plataforma ECOREX" es visible para un usuario de agencia).
        await page.GotoAsync("actividades");
        await Assertions.Expect(page.Locator(".tk-board")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("body")).Not.ToContainTextAsync("Plataforma ECOREX");
    }
}
