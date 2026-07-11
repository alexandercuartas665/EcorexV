using Ecorex.Application.MenuConfig;
using Ecorex.Application.Roles;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests PUROS del filtrado del menu por permiso de "Ver" (Ola B2, ADR-0033): Unrestricted deja el
/// arbol intacto; con rol poda los Item sin View y oculta secciones que quedan vacias; QuickLinks y
/// Item sin Route se conservan.
/// </summary>
public class MenuPermissionFilterTests
{
    private static MenuNodeDto Item(string name, string? route) =>
        new(Guid.NewGuid(), MenuNodeKind.Item, name, null, null, route, MenuNodeState.Ready, 0, false, Array.Empty<MenuNodeDto>());

    private static MenuNodeDto Section(string name, string route, params MenuNodeDto[] children) =>
        new(Guid.NewGuid(), MenuNodeKind.Section, name, null, null, route, MenuNodeState.Ready, 0, false, children);

    private static MenuNodeDto Quick(string name, string route) =>
        new(Guid.NewGuid(), MenuNodeKind.QuickLink, name, null, null, route, MenuNodeState.Ready, 0, false, Array.Empty<MenuNodeDto>());

    private static EffectivePermissions RoleWith(params (string route, bool view)[] mods) =>
        EffectivePermissions.FromPermissions(
            Guid.NewGuid(),
            mods.Select(m => new ModulePermissionDto(m.route, m.view, false, false, false)));

    [Fact]
    public void Unrestricted_ReturnsTreeUntouched()
    {
        var roots = new[]
        {
            Section("Sistema", "sys", Item("A", "a"), Item("B", "b"))
        };

        var kept = MenuPermissionFilter.Filter(roots, EffectivePermissions.UnrestrictedAccess());

        Assert.Same(roots, kept);
    }

    [Fact]
    public void NullPermissions_ReturnsTreeUntouched()
    {
        var roots = new[] { Section("Sistema", "sys", Item("A", "a")) };
        var kept = MenuPermissionFilter.Filter(roots, permissions: null);
        Assert.Same(roots, kept);
    }

    [Fact]
    public void WithRole_PrunesItemsWithoutView()
    {
        var roots = new[]
        {
            Section("Inventarios", "inv", Item("Items", "inventario-items"), Item("Bodegas", "inventario-bodegas"))
        };
        var perms = RoleWith(("inventario-items", true), ("inventario-bodegas", false));

        var kept = MenuPermissionFilter.Filter(roots, perms);

        var section = Assert.Single(kept);
        var leaf = Assert.Single(section.Children);
        Assert.Equal("inventario-items", leaf.Route);
    }

    [Fact]
    public void WithRole_HidesSection_WhenAllChildrenPruned()
    {
        var roots = new[]
        {
            Section("Desarrollo", "dev", Item("Reglas", "reglas"), Item("Modulos web", "modulos-web")),
            Section("Inventarios", "inv", Item("Items", "inventario-items"))
        };
        // Sin Ver en nada de Desarrollo; Ver en inventario-items.
        var perms = RoleWith(("reglas", false), ("modulos-web", false), ("inventario-items", true));

        var kept = MenuPermissionFilter.Filter(roots, perms);

        var section = Assert.Single(kept);   // Desarrollo desaparece entero.
        Assert.Equal("inv", section.Route);
    }

    [Fact]
    public void WithRole_KeepsQuickLinksAndRoutelessItems()
    {
        var roots = new[]
        {
            Quick("Inicio", "inicio"),
            Section("Grupo", "grp", Item("Sin ruta", null))
        };
        // El usuario no tiene NINGUN modulo con Ver, pero QuickLink e Item sin Route no se tocan.
        var perms = RoleWith(("otro", false));

        var kept = MenuPermissionFilter.Filter(roots, perms);

        Assert.Equal(2, kept.Count);
        Assert.Contains(kept, n => n.Kind == MenuNodeKind.QuickLink && n.Route == "inicio");
        Assert.Contains(kept, n => n.Kind == MenuNodeKind.Section && n.Children.Count == 1);
    }

    [Fact]
    public void WithRole_FiltersNestedSubgroups()
    {
        var subgroup = new MenuNodeDto(
            Guid.NewGuid(), MenuNodeKind.Subgroup, "Comercial", null, null, "sg", MenuNodeState.Ready, 0, false,
            new[] { Item("Visible", "vis"), Item("Oculto", "oc") });
        var roots = new[] { Section("Procesos", "proc", subgroup) };
        var perms = RoleWith(("vis", true), ("oc", false));

        var kept = MenuPermissionFilter.Filter(roots, perms);

        var section = Assert.Single(kept);
        var sg = Assert.Single(section.Children);
        var leaf = Assert.Single(sg.Children);
        Assert.Equal("vis", leaf.Route);
    }
}
