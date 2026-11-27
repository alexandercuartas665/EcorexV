using Ecorex.Application.Common;
using Ecorex.Application.MenuConfig;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Auth;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la Ola 2 del menu configurable (editor "Administrador de Menu") en
/// matriz dual PostgreSQL / SQL Server: CRUD de nodos (crear/actualizar/mover/borrar en cascada),
/// SetDefault (solo una IsDefault), no borrar la vista por defecto, no crear ciclos en Move,
/// export->import round-trip, asignacion de usuario reflejada en GetMenuForTenantUser y
/// aislamiento cross-tenant. Reusa las fixtures de aislamiento dual (Testcontainers).
/// </summary>
public abstract class MenuConfigEditorTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected MenuConfigEditorTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateNode_UnderSection_PersistsAndAppearsInTree()
    {
        var tenantId = await NewTenantAsync("Editor Create");
        var viewId = await NewViewAsync(tenantId, "V", isDefault: true);

        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Sec", iconKey: "list"));
        Assert.True(section.IsOk, section.Error);
        var item = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, section.Value!.Id, MenuNodeKind.Item, "It", route: "r", legacyCode: "000001"));
        Assert.True(item.IsOk, item.Error);

        var tree = await RunAsync(tenantId, s => s.GetViewTreeAsync(viewId));
        Assert.True(tree.IsOk);
        var sec = Assert.Single(tree.Value!.Roots);
        Assert.Equal("Sec", sec.Name);
        var leaf = Assert.Single(sec.Children);
        Assert.Equal("It", leaf.Name);
        Assert.Equal("000001", leaf.LegacyCode);
    }

    [Fact]
    public async Task CreateNode_SectionUnderItem_IsInvalid()
    {
        var tenantId = await NewTenantAsync("Editor Kind");
        var viewId = await NewViewAsync(tenantId, "V", isDefault: true);
        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Sec"));
        var item = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, section.Value!.Id, MenuNodeKind.Item, "It"));

        // Una seccion NO puede colgar de un item.
        var bad = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, item.Value!.Id, MenuNodeKind.Section, "Nope"));
        Assert.Equal(MenuConfigStatus.Invalid, bad.Status);
    }

    [Fact]
    public async Task UpdateNode_ChangesFields()
    {
        var tenantId = await NewTenantAsync("Editor Update");
        var viewId = await NewViewAsync(tenantId, "V", isDefault: true);
        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Sec"));

        var upd = await RunAsync(tenantId, s => s.UpdateNodeAsync(section.Value!.Id,
            new MenuNodeEditDto(Name: "Renombrada", IconKey: "gear", State: MenuNodeState.InDevelopment)));
        Assert.True(upd.IsOk, upd.Error);
        Assert.Equal("Renombrada", upd.Value!.Name);
        Assert.Equal("gear", upd.Value.IconKey);
        Assert.Equal(MenuNodeState.InDevelopment, upd.Value.State);
    }

    [Fact]
    public async Task ToggleVisibility_HidesNodeFromResolvedMenu()
    {
        var tenantId = await NewTenantAsync("Editor Toggle");
        var viewId = await NewViewAsync(tenantId, "V", isDefault: true);
        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Sec"));
        await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, section.Value!.Id, MenuNodeKind.Item, "It", route: "r"));

        await RunAsync(tenantId, s => s.ToggleNodeVisibilityAsync(section.Value!.Id));

        // La seccion oculta poda su rama del menu resuelto (solo visibles).
        var resolved = await RunAsync(tenantId, s => s.GetMenuForTenantUserAsync(tenantId, viewId));
        Assert.True(resolved is null || resolved.Roots.All(r => r.Name != "Sec"));
    }

    [Fact]
    public async Task MoveNode_ReordersSiblings()
    {
        var tenantId = await NewTenantAsync("Editor Move");
        var viewId = await NewViewAsync(tenantId, "V", isDefault: true);
        var a = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "A"));
        var b = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "B"));
        var c = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "C"));

        // Mueve C al inicio (sortOrder 0).
        var moved = await RunAsync(tenantId, s => s.MoveNodeAsync(c.Value!.Id, null, 0));
        Assert.True(moved.IsOk, moved.Error);

        var tree = await RunAsync(tenantId, s => s.GetViewTreeAsync(viewId));
        var names = tree.Value!.Roots.Select(r => r.Name).ToList();
        Assert.Equal(new[] { "C", "A", "B" }, names);
    }

    [Fact]
    public async Task MoveNode_IntoOwnDescendant_IsInvalid()
    {
        var tenantId = await NewTenantAsync("Editor Cycle");
        var viewId = await NewViewAsync(tenantId, "V", isDefault: true);
        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Sec"));
        var sub = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, section.Value!.Id, MenuNodeKind.Subgroup, "Sub"));

        // Reparentar la seccion dentro de su propio subgrupo crearia un ciclo.
        var bad = await RunAsync(tenantId, s => s.MoveNodeAsync(section.Value!.Id, sub.Value!.Id, 0));
        Assert.Equal(MenuConfigStatus.Invalid, bad.Status);
    }

    [Fact]
    public async Task DeleteNode_CascadesToDescendants()
    {
        var tenantId = await NewTenantAsync("Editor DeleteNode");
        var viewId = await NewViewAsync(tenantId, "V", isDefault: true);
        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Sec"));
        var sub = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, section.Value!.Id, MenuNodeKind.Subgroup, "Sub"));
        await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, sub.Value!.Id, MenuNodeKind.Item, "Leaf"));

        var del = await RunAsync(tenantId, s => s.DeleteNodeAsync(section.Value!.Id));
        Assert.True(del.IsOk, del.Error);

        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.Equal(0, await ctx.MenuNodes.CountAsync(n => n.MenuViewId == viewId));
    }

    [Fact]
    public async Task DeleteNode_DejaLapida_YElBackfillDeArranqueNoLoRepone_NiEnLaCopia()
    {
        // Caso SOLDARCO: el tenant quita "Compras" de su menu personalizado y en cada despliegue el backfill
        // de arranque (EnsureRequerimientosCompraMenuAsync) se lo volvia a crear porque el borrado era fisico.
        var tenantId = await NewTenantAsync("Editor Lapida");
        var viewId = await NewViewAsync(tenantId, "Soldarco General", isDefault: true);

        async Task Arranque()
        {
            await using var ctx = _fixture.CreateContext(tenantId: null);
            var seeder = new DatabaseSeeder(ctx, new Pbkdf2PasswordHasher(), NullLogger<DatabaseSeeder>.Instance);
            await seeder.EnsureRequerimientosCompraMenuAsync(tenantId);
        }

        await Arranque();
        var tree = await RunAsync(tenantId, s => s.GetViewTreeAsync(viewId));
        var compras = Assert.Single(tree.Value!.Roots, r => r.Route == "compras");

        var del = await RunAsync(tenantId, s => s.DeleteNodeAsync(compras.Id));
        Assert.True(del.IsOk, del.Error);

        // La copia hecha DESPUES de quitarlo hereda la lapida.
        var copia = await RunAsync(tenantId, s => s.CloneViewAsync(viewId, "Soldarco General (copia)"));
        Assert.True(copia.IsOk, copia.Error);

        // Dos arranques (dos despliegues): no reaparece ni en la vista ni en la copia.
        await Arranque();
        await Arranque();
        foreach (var v in new[] { viewId, copia.Value!.Id })
        {
            var t = await RunAsync(tenantId, s => s.GetViewTreeAsync(v));
            Assert.DoesNotContain(t.Value!.Roots, r => r.Route == "compras");
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            // Oculto para las consultas normales; como lapida (IsDeleted) para IgnoreQueryFilters.
            Assert.Equal(0, await ctx.MenuNodes.CountAsync(n => n.MenuViewId == viewId));
            var lapidas = await ctx.MenuNodes.IgnoreQueryFilters()
                .Where(n => n.MenuViewId == viewId).ToListAsync();
            Assert.NotEmpty(lapidas);
            Assert.All(lapidas, n => Assert.True(n.IsDeleted && n.DeletedAt is not null));
        }

        // Borrar la copia elimina tambien sus lapidas.
        var delCopia = await RunAsync(tenantId, s => s.DeleteViewAsync(copia.Value.Id));
        Assert.True(delCopia.IsOk, delCopia.Error);
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            Assert.Equal(0, await ctx.MenuNodes.IgnoreQueryFilters().CountAsync(n => n.MenuViewId == copia.Value.Id));
        }
    }

    [Fact]
    public async Task SetDefault_MarksOnlyOne()
    {
        var tenantId = await NewTenantAsync("Editor Default");
        var v1 = await NewViewAsync(tenantId, "V1", isDefault: true);
        var v2 = await NewViewAsync(tenantId, "V2", isDefault: false);

        var res = await RunAsync(tenantId, s => s.SetDefaultViewAsync(v2));
        Assert.True(res.IsOk, res.Error);

        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.False(await ctx.MenuViews.Where(v => v.Id == v1).Select(v => v.IsDefault).FirstAsync());
        Assert.True(await ctx.MenuViews.Where(v => v.Id == v2).Select(v => v.IsDefault).FirstAsync());
    }

    [Fact]
    public async Task DeleteDefaultView_IsInvalid()
    {
        var tenantId = await NewTenantAsync("Editor DeleteDefault");
        var viewId = await NewViewAsync(tenantId, "Solo", isDefault: true);

        var res = await RunAsync(tenantId, s => s.DeleteViewAsync(viewId));
        Assert.Equal(MenuConfigStatus.Invalid, res.Status);

        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.True(await ctx.MenuViews.AnyAsync(v => v.Id == viewId));
    }

    [Fact]
    public async Task DeleteView_CascadesNodes_AndUnassignsUsers()
    {
        var tenantId = await NewTenantAsync("Editor DeleteView");
        var keep = await NewViewAsync(tenantId, "Keep", isDefault: true);
        var doomed = await NewViewAsync(tenantId, "Doomed", isDefault: false);
        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(doomed, null, MenuNodeKind.Section, "Sec"));
        Assert.True(section.IsOk);

        Guid tenantUserId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var pu = new PlatformUser { Email = "d@menu.local", DisplayName = "D" };
            ctx.PlatformUsers.Add(pu);
            var tu = new TenantUser { TenantId = tenantId, PlatformUserId = pu.Id, Email = "d@menu.local", MenuViewId = doomed };
            ctx.TenantUsers.Add(tu);
            await ctx.SaveChangesAsync();
            tenantUserId = tu.Id;
        }

        var del = await RunAsync(tenantId, s => s.DeleteViewAsync(doomed));
        Assert.True(del.IsOk, del.Error);

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            Assert.False(await ctx.MenuViews.AnyAsync(v => v.Id == doomed));
            Assert.Equal(0, await ctx.MenuNodes.CountAsync(n => n.MenuViewId == doomed));
            var tu = await ctx.TenantUsers.FirstAsync(u => u.Id == tenantUserId);
            Assert.Null(tu.MenuViewId); // cae a la vista por defecto
        }
        Assert.NotEqual(Guid.Empty, keep);
    }

    [Fact]
    public async Task ExportImport_RoundTrip_PreservesStructure()
    {
        var tenantId = await NewTenantAsync("Editor Export");
        var viewId = await NewViewAsync(tenantId, "Origen", isDefault: true);
        var section = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Sec", iconKey: "list"));
        var sub = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, section.Value!.Id, MenuNodeKind.Subgroup, "Sub"));
        await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, sub.Value!.Id, MenuNodeKind.Item, "Leaf", legacyCode: "000009", route: "r"));

        var export = await RunAsync(tenantId, s => s.ExportViewAsync(viewId));
        Assert.True(export.IsOk, export.Error);
        var json = System.Text.Json.JsonSerializer.Serialize(export.Value);

        var imported = await RunAsync(tenantId, s => s.ImportViewAsync(json, "Importada"));
        Assert.True(imported.IsOk, imported.Error);
        Assert.False(imported.Value!.IsDefault);

        var tree = await RunAsync(tenantId, s => s.GetViewTreeAsync(imported.Value.Id));
        var sec = Assert.Single(tree.Value!.Roots);
        Assert.Equal("Sec", sec.Name);
        var importedSub = Assert.Single(sec.Children);
        Assert.Equal(MenuNodeKind.Subgroup, importedSub.Kind);
        var leaf = Assert.Single(importedSub.Children);
        Assert.Equal("000009", leaf.LegacyCode);
    }

    [Fact]
    public async Task AssignUser_ChangesResolvedMenu()
    {
        var tenantId = await NewTenantAsync("Editor Assign");
        var big = await NewViewAsync(tenantId, "Big", isDefault: true);
        var small = await NewViewAsync(tenantId, "Small", isDefault: false);
        // "Big" tiene una seccion "SoloBig"; "Small" tiene "SoloSmall".
        await RunAsync(tenantId, s => s.CreateNodeAsync(big, null, MenuNodeKind.QuickLink, "SoloBig", route: "b"));
        await RunAsync(tenantId, s => s.CreateNodeAsync(small, null, MenuNodeKind.QuickLink, "SoloSmall", route: "s"));

        Guid tenantUserId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var pu = new PlatformUser { Email = "a@menu.local", DisplayName = "A" };
            ctx.PlatformUsers.Add(pu);
            var tu = new TenantUser { TenantId = tenantId, PlatformUserId = pu.Id, Email = "a@menu.local" };
            ctx.TenantUsers.Add(tu);
            await ctx.SaveChangesAsync();
            tenantUserId = tu.Id;
        }

        var assign = await RunAsync(tenantId, s => s.AssignUserToViewAsync(tenantUserId, small));
        Assert.True(assign.IsOk, assign.Error);

        // El menu del usuario refleja la vista "Small".
        var resolved = await RunAsync(tenantId, s => s.GetMenuForTenantUserAsync(tenantId, small));
        Assert.NotNull(resolved);
        Assert.Contains(resolved!.Roots, r => r.Name == "SoloSmall");
        Assert.DoesNotContain(resolved.Roots, r => r.Name == "SoloBig");
    }

    [Fact]
    public async Task Editor_Operations_AreTenantIsolated()
    {
        var a = await NewTenantAsync("Editor Iso A");
        var b = await NewTenantAsync("Editor Iso B");
        var viewA = await NewViewAsync(a, "SoloA", isDefault: true);
        var section = await RunAsync(a, s => s.CreateNodeAsync(viewA, null, MenuNodeKind.Section, "SecA"));
        Assert.True(section.IsOk);

        // El tenant B no puede leer ni tocar el arbol de la vista de A.
        var treeFromB = await RunAsync(b, s => s.GetViewTreeAsync(viewA));
        Assert.Equal(MenuConfigStatus.NotFound, treeFromB.Status);

        var delFromB = await RunAsync(b, s => s.DeleteNodeAsync(section.Value!.Id));
        Assert.Equal(MenuConfigStatus.NotFound, delFromB.Status);
    }

    // ---- Menu estandar (MenuCanonicalTree): cargar en vista nueva / completar una existente ----

    [Fact]
    public async Task LoadStandard_EmptyView_GetsCanonicalTree_AndIsIdempotent()
    {
        var tenantId = await NewTenantAsync("Std Vacia");
        var viewId = await NewViewAsync(tenantId, "Nueva", isDefault: false);

        var first = await RunAsync(tenantId, s => s.LoadStandardMenuAsync(viewId));
        Assert.True(first.IsOk, first.Error);
        var expected = MenuCanonicalTree.BuildNodes(tenantId, viewId).Count;
        Assert.Equal(expected, first.Value);

        var tree = await RunAsync(tenantId, s => s.GetViewTreeAsync(viewId));
        Assert.True(tree.IsOk);
        Assert.Contains(tree.Value!.Roots, r => r.Kind == MenuNodeKind.Section && r.Name == "Sistema \u00b7 Actividades"
            && r.Children.Any(c => c.Route == "cerebros"));

        // Segunda vez no agrega nada (no duplica).
        var second = await RunAsync(tenantId, s => s.LoadStandardMenuAsync(viewId));
        Assert.True(second.IsOk, second.Error);
        Assert.Equal(0, second.Value);
    }

    [Fact]
    public async Task LoadStandard_PartialView_AddsOnlyMissing_KeepsRenamedAndHidden()
    {
        var tenantId = await NewTenantAsync("Std Parcial");
        var viewId = await NewViewAsync(tenantId, "Recortada", isDefault: true);
        // Seccion "act" renombrada por el cliente, con "Tableros" renombrado y OCULTO.
        var act = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "Mis tableros", route: "act"));
        var tab = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, act.Value!.Id, MenuNodeKind.Item, "Kanban", route: "tableros"));
        await RunAsync(tenantId, s => s.ToggleNodeVisibilityAsync(tab.Value!.Id));

        var res = await RunAsync(tenantId, s => s.LoadStandardMenuAsync(viewId));
        Assert.True(res.IsOk, res.Error);
        Assert.True(res.Value > 0);

        var tree = (await RunAsync(tenantId, s => s.GetViewTreeAsync(viewId))).Value!;
        // No se crea otra seccion "Sistema - Actividades": los faltantes caen en la existente (por slug).
        var sec = Assert.Single(tree.Roots, r => r.Kind == MenuNodeKind.Section && r.Route == "act");
        Assert.Equal("Mis tableros", sec.Name);
        var kanban = Assert.Single(sec.Children, c => c.Route == "tableros");
        Assert.Equal("Kanban", kanban.Name);
        Assert.False(kanban.IsVisible);
        Assert.Contains(sec.Children, c => c.Route == "cerebros");
        // Las demas secciones del estandar se agregan.
        Assert.Contains(tree.Roots, r => r.Kind == MenuNodeKind.Section && r.Route == "gen");
    }

    [Fact]
    public async Task ResolvedMenu_EmptyDefaultView_FallsBackToRichestView()
    {
        var tenantId = await NewTenantAsync("Std Default Vacia");
        var full = await NewViewAsync(tenantId, "Completo", isDefault: false);
        await NewViewAsync(tenantId, "Nueva vacia", isDefault: true);
        await RunAsync(tenantId, s => s.LoadStandardMenuAsync(full));

        var resolved = await RunAsync(tenantId, s => s.GetMenuForTenantUserAsync(tenantId, null));
        Assert.NotNull(resolved);
        Assert.Equal(full, resolved!.MenuViewId);
    }

    [Fact]
    public async Task ModuleCatalog_IncludesStandardModulesMissingFromTenantMenus()
    {
        var tenantId = await NewTenantAsync("Std Catalogo");
        var viewId = await NewViewAsync(tenantId, "Minima", isDefault: true);
        var sec = await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, null, MenuNodeKind.Section, "S"));
        await RunAsync(tenantId, s => s.CreateNodeAsync(viewId, sec.Value!.Id, MenuNodeKind.Item, "Propio", route: "mi-ruta-propia"));

        var catalog = await RunAsync(tenantId, s => s.ListModuleCatalogAsync());
        Assert.Contains(catalog, c => c.Route == "mi-ruta-propia");
        Assert.Contains(catalog, c => c.Route == "cerebros");
        Assert.DoesNotContain(catalog, c => c.Route.StartsWith("modulo/", StringComparison.Ordinal));
    }

    // ---- Helpers ----

    private async Task<Guid> NewTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> NewViewAsync(Guid tenantId, string name, bool isDefault)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var view = new MenuView { TenantId = tenantId, Name = name, IsDefault = isDefault, SortOrder = 0 };
        ctx.MenuViews.Add(view);
        await ctx.SaveChangesAsync();
        return view.Id;
    }

    private async Task<T> RunAsync<T>(Guid tenantId, Func<IMenuConfigService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var service = new MenuConfigService(ctx, new TestTenantContext(tenantId));
        return await action(service);
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class MenuConfigEditorTests_Postgres
    : MenuConfigEditorTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public MenuConfigEditorTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class MenuConfigEditorTests_SqlServer
    : MenuConfigEditorTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public MenuConfigEditorTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
