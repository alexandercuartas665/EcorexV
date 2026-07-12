using Ecorex.Application.Common;
using Ecorex.Application.MenuConfig;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del menu configurable por perfil (Ola 1) en matriz dual PostgreSQL /
/// SQL Server: persistir vista + nodos y releer el arbol resuelto, asignar un usuario a una vista,
/// aislamiento cross-tenant (tenant B no ve las vistas de A) y cascada (borrar la vista borra sus
/// nodos). Reusa las fixtures de aislamiento dual (Testcontainers).
/// </summary>
public abstract class MenuConfigTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected MenuConfigTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task View_WithNodes_PersistsAndResolvesTree()
    {
        var tenantId = await NewTenantAsync("Menu Round-Trip");

        Guid viewId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { Id = viewId, TenantId = tenantId, Name = "Completo", IsDefault = true, SortOrder = 0 };
            ctx.MenuViews.Add(view);
            var sectionId = Guid.CreateVersion7();
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = viewId,
                Kind = MenuNodeKind.Section,
                Name = "Mis Procesos",
                Route = "misproc",
                IconKey = "list",
                SortOrder = 0,
                Id = sectionId
            });
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = viewId,
                ParentId = sectionId,
                Kind = MenuNodeKind.Item,
                Name = "Crear una actividad",
                Route = "crear-actividad",
                LegacyCode = "000038",
                SortOrder = 0
            });
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = viewId,
                ParentId = sectionId,
                Kind = MenuNodeKind.Item,
                Name = "Oculto",
                Route = "oculto",
                IsVisible = false,
                SortOrder = 1
            });
            await ctx.SaveChangesAsync();
        }

        var resolved = await RunAsync(tenantId, s => s.GetMenuForTenantUserAsync(tenantId, viewId));
        Assert.NotNull(resolved);
        Assert.Equal("Completo", resolved!.MenuViewName);
        var section = Assert.Single(resolved.Roots);
        Assert.Equal("Mis Procesos", section.Name);
        // El item oculto no aparece ni cuenta.
        Assert.Equal(1, section.VisibleChildCount);
        Assert.Equal("Crear una actividad", section.Children[0].Name);
        Assert.Equal("000038", section.Children[0].LegacyCode);
    }

    [Fact]
    public async Task NullMenuView_FallsBackToDefaultView()
    {
        var tenantId = await NewTenantAsync("Menu Fallback");

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var def = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = true, SortOrder = 0 };
            var other = new MenuView { TenantId = tenantId, Name = "Simple", IsDefault = false, SortOrder = 1 };
            ctx.MenuViews.AddRange(def, other);
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = def.Id,
                Kind = MenuNodeKind.QuickLink,
                Name = "Inicio",
                Route = "inicio",
                SortOrder = 0
            });
            await ctx.SaveChangesAsync();
        }

        // Usuario sin vista asignada (null) -> resuelve la vista IsDefault.
        var resolved = await RunAsync(tenantId, s => s.GetMenuForTenantUserAsync(tenantId, null));
        Assert.NotNull(resolved);
        Assert.Equal("Completo", resolved!.MenuViewName);
    }

    [Fact]
    public async Task NullMenuView_NoDefault_FallsBackToRichestView()
    {
        // #2: tenant real sin NINGUNA vista IsDefault (ej. BITCODE creado sin seed de menu). El usuario
        // sin vista asignada cae a la vista con MAS nodos visibles (la mas completa), no a una minima/E2E
        // aunque esa tenga menor SortOrder.
        var tenantId = await NewTenantAsync("Menu Sin Default");
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var minimal = new MenuView { TenantId = tenantId, Name = "E2E mini", IsDefault = false, SortOrder = 0 };
            var rich = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = false, SortOrder = 1 };
            ctx.MenuViews.AddRange(minimal, rich);
            ctx.MenuNodes.Add(new MenuNode { TenantId = tenantId, MenuViewId = minimal.Id, Kind = MenuNodeKind.QuickLink, Name = "Inicio", Route = "inicio", IsVisible = true, SortOrder = 0 });
            for (var i = 0; i < 3; i++)
            {
                ctx.MenuNodes.Add(new MenuNode { TenantId = tenantId, MenuViewId = rich.Id, Kind = MenuNodeKind.QuickLink, Name = $"N{i}", Route = $"r{i}", IsVisible = true, SortOrder = i });
            }
            await ctx.SaveChangesAsync();
        }

        var resolved = await RunAsync(tenantId, s => s.GetMenuForTenantUserAsync(tenantId, null));
        Assert.NotNull(resolved);
        Assert.Equal("Completo", resolved!.MenuViewName); // la mas rica, no la minima (SortOrder 0)
    }

    [Fact]
    public async Task User_AssignedToView_IsPersisted()
    {
        var tenantId = await NewTenantAsync("Menu Asignacion");
        Guid viewId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { TenantId = tenantId, Name = "Simple", SortOrder = 0 };
            ctx.MenuViews.Add(view);
            var pu = new PlatformUser { Email = "u@menu.local", DisplayName = "U" };
            ctx.PlatformUsers.Add(pu);
            ctx.TenantUsers.Add(new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = pu.Id,
                Email = "u@menu.local",
                TenantRole = TenantRole.Advisor,
                MenuViewId = view.Id
            });
            await ctx.SaveChangesAsync();
            viewId = view.Id;
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var tu = await ctx.TenantUsers.FirstAsync(u => u.Email == "u@menu.local");
            Assert.Equal(viewId, tu.MenuViewId);
        }
    }

    [Fact]
    public async Task CrossTenant_Views_AreIsolated()
    {
        var a = await NewTenantAsync("Menu Tenant A");
        var b = await NewTenantAsync("Menu Tenant B");

        Guid viewA;
        await using (var ctx = _fixture.CreateContext(a))
        {
            var view = new MenuView { TenantId = a, Name = "Solo A", IsDefault = true, SortOrder = 0 };
            ctx.MenuViews.Add(view);
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = a,
                MenuViewId = view.Id,
                Kind = MenuNodeKind.QuickLink,
                Name = "Inicio",
                Route = "inicio",
                SortOrder = 0
            });
            await ctx.SaveChangesAsync();
            viewA = view.Id;
        }

        // El tenant B no ve la vista de A (filtro global).
        var bViews = await RunAsync(b, s => s.ListViewsAsync());
        Assert.DoesNotContain(bViews, v => v.Id == viewA);

        // Resolver la vista de A desde el contexto de B no devuelve el arbol de A.
        var resolvedFromB = await RunAsync(b, s => s.GetMenuForTenantUserAsync(b, viewA));
        Assert.Null(resolvedFromB);
    }

    [Fact]
    public async Task DeletingView_CascadesToNodes()
    {
        var tenantId = await NewTenantAsync("Menu Cascade");
        Guid viewId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { TenantId = tenantId, Name = "Completo", SortOrder = 0 };
            ctx.MenuViews.Add(view);
            var section = Guid.CreateVersion7();
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = view.Id,
                Kind = MenuNodeKind.Section,
                Name = "Sec",
                Route = "sec",
                SortOrder = 0,
                Id = section
            });
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = view.Id,
                ParentId = section,
                Kind = MenuNodeKind.Item,
                Name = "Item",
                Route = "item",
                SortOrder = 0
            });
            await ctx.SaveChangesAsync();
            viewId = view.Id;
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = await ctx.MenuViews.FirstAsync(v => v.Id == viewId);
            ctx.MenuViews.Remove(view);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            Assert.Equal(0, await ctx.MenuNodes.CountAsync(n => n.MenuViewId == viewId));
        }
    }

    [Fact]
    public async Task Clone_CopiesNodes_PreservingHierarchy()
    {
        var tenantId = await NewTenantAsync("Menu Clone");
        Guid sourceId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = true, SortOrder = 0 };
            ctx.MenuViews.Add(view);
            var section = Guid.CreateVersion7();
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = view.Id,
                Kind = MenuNodeKind.Section,
                Name = "Sec",
                Route = "sec",
                SortOrder = 0,
                Id = section
            });
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = view.Id,
                ParentId = section,
                Kind = MenuNodeKind.Item,
                Name = "Item",
                Route = "item",
                SortOrder = 0
            });
            await ctx.SaveChangesAsync();
            sourceId = view.Id;
        }

        var cloned = await RunAsync(tenantId, s => s.CloneViewAsync(sourceId, "Copia"));
        Assert.True(cloned.IsOk, cloned.Error);
        Assert.False(cloned.Value!.IsDefault);
        Assert.Equal(2, cloned.Value.NodeCount);

        // El arbol clonado resuelve igual (seccion con 1 hijo).
        var resolved = await RunAsync(tenantId, s => s.GetMenuForTenantUserAsync(tenantId, cloned.Value.Id));
        Assert.NotNull(resolved);
        var sec = Assert.Single(resolved!.Roots);
        Assert.Equal(1, sec.VisibleChildCount);
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
public sealed class MenuConfigTests_Postgres
    : MenuConfigTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public MenuConfigTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class MenuConfigTests_SqlServer
    : MenuConfigTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public MenuConfigTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
