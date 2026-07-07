using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E del menu configurable por perfil (Ola 1): dos usuarios demo del tenant SKY SYSTEM ven
/// menus distintos segun su vista asignada. completo@sky-system.local (vista Completo) ve la
/// seccion "Sistema Inventarios" con sus 6 items; simple@sky-system.local (vista Simple) ve
/// menos secciones (NO ve "Sistema CRM") pero SI "Mis Procesos". Selectores por texto/clase
/// del prototipo (.ecorex-acc-name, .ecorex-acc-item).
/// </summary>
public sealed class MenuProfileTests : E2eTestBase
{
    private const string Password = "Demo123*";

    public MenuProfileTests(E2eAppFixture fx) : base(fx) { }

    [SkippableFact]
    public async Task CompletoProfile_ShowsInventarios_WithAllItems()
    {
        RequireApp();
        var page = await LoginAsync("completo@sky-system.local", Password);

        // La seccion "Sistema Inventarios" existe en la vista Completo.
        var inventarios = page.Locator(".ecorex-acc-name")
            .Filter(new LocatorFilterOptions { HasText = "Inventarios" });
        await Assertions.Expect(inventarios).ToBeVisibleAsync();

        // Su acordeon tiene 6 items (Bodegas, Grupos, Marcas, Subgrupos, Tipos, Items).
        var section = page.Locator(".ecorex-acc").Filter(new LocatorFilterOptions
        {
            Has = page.Locator(".ecorex-acc-name", new PageLocatorOptions { HasText = "Inventarios" })
        });
        await Assertions.Expect(section.Locator(".ecorex-acc-item")).ToHaveCountAsync(6);

        // La vista Completo tambien tiene la seccion "Sistema CRM".
        await Assertions.Expect(page.Locator(".ecorex-acc-name")
            .Filter(new LocatorFilterOptions { HasText = "CRM" }).First).ToBeVisibleAsync();
    }

    [SkippableFact]
    public async Task SimpleProfile_HidesCrm_ButShowsMisProcesos()
    {
        RequireApp();
        var page = await LoginAsync("simple@sky-system.local", Password);

        // La vista Simple SI muestra "Mis Procesos".
        await Assertions.Expect(page.Locator(".ecorex-acc-name")
            .Filter(new LocatorFilterOptions { HasText = "Mis Procesos" })).ToBeVisibleAsync();

        // La vista Simple tiene claramente MENOS secciones que Completo (4: Mis Procesos,
        // Inventarios, Automatizacion; sin CRM/General/Desarrollo/etc.).
        var sectionCount = await page.Locator(".ecorex-acc:not(.ecorex-acc-sub) > summary .ecorex-acc-name").CountAsync();
        Assert.True(sectionCount <= 4, $"La vista Simple deberia tener pocas secciones, tiene {sectionCount}.");

        // No debe existir la seccion "Sistema CRM" en la vista Simple.
        var crm = page.Locator(".ecorex-acc-name").Filter(new LocatorFilterOptions { HasText = "Sistema" })
            .Filter(new LocatorFilterOptions { HasText = "CRM" });
        await Assertions.Expect(crm).ToHaveCountAsync(0);
    }
}
