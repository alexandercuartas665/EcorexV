using Microsoft.Playwright;

namespace Ecorex.E2E.Tests;

/// <summary>
/// E2E del enforcement de permisos por rol (Ola B2, ADR-0033). El usuario demo simple@ tiene el rol
/// "Asesor limitado" (Ver en Mis Procesos/Inventarios/Automatizacion; SIN Ver en Desarrollo/CRM; SIN
/// Crear en inventario). Se verifica que:
/// - el sidebar de simple@ NO muestra "Sistema . Desarrollo" ni "Sistema . CRM" pero SI "Mis
///   Procesos" y "Sistema . Inventarios";
/// - en /inventario-items simple@ (Ver pero no Crear) NO ve el boton "+ Nuevo item";
/// - owner@ (Unrestricted por poder organico) SI ve la seccion completa y el boton de crear.
/// Selectores por texto/clase del prototipo (el producto no tiene data-testid).
/// </summary>
public sealed class PermissionEnforcementTests : E2eTestBase
{
    private const string Password = "Demo123*";

    public PermissionEnforcementTests(E2eAppFixture fx) : base(fx) { }

    [SkippableFact]
    public async Task Simple_Sidebar_HidesDevAndCrm_ShowsProcesosAndInventarios()
    {
        RequireApp();
        var page = await LoginAsync("simple@sky-system.local", Password);

        // Secciones permitidas: Mis Procesos e Inventarios visibles.
        await Assertions.Expect(page.Locator(".ecorex-acc-name")
            .Filter(new LocatorFilterOptions { HasText = "Mis Procesos" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".ecorex-acc-name")
            .Filter(new LocatorFilterOptions { HasText = "Inventarios" })).ToBeVisibleAsync();

        // Secciones sin Ver: Desarrollo y CRM NO aparecen.
        await Assertions.Expect(page.Locator(".ecorex-acc-name")
            .Filter(new LocatorFilterOptions { HasText = "Desarrollo" })).ToHaveCountAsync(0);
        var crm = page.Locator(".ecorex-acc-name")
            .Filter(new LocatorFilterOptions { HasText = "Sistema" })
            .Filter(new LocatorFilterOptions { HasText = "CRM" });
        await Assertions.Expect(crm).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Simple_InventarioItems_HasNoCreateButton()
    {
        RequireApp();
        var page = await LoginAsync("simple@sky-system.local", Password);

        await page.GotoAsync("inventario-items");
        // La pagina carga (paso el gate de Ver): sus filtros existen.
        await page.Locator(".inv-filters").WaitForAsync();

        // Sin permiso de Crear: el boton "+ Nuevo item" no se renderiza.
        await Assertions.Expect(page.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = "+ Nuevo item" })).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Owner_InventarioItems_ShowsCreateButton()
    {
        RequireApp();
        var page = await LoginAsync("owner@sky-system.local", Password);

        await page.GotoAsync("inventario-items");
        await page.Locator(".inv-filters").WaitForAsync();

        // Owner es Unrestricted: ve el boton de crear.
        await Assertions.Expect(page.GetByRole(AriaRole.Button,
            new PageGetByRoleOptions { Name = "+ Nuevo item" }).First).ToBeVisibleAsync();
    }
}
