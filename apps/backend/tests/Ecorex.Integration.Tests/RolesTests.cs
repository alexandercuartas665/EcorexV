using Ecorex.Application.Common;
using Ecorex.Application.MenuConfig;
using Ecorex.Application.Roles;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de los roles de permisos dinamicos (Ola B1, ADR-0032) en matriz dual
/// PostgreSQL / SQL Server: crear rol + guardar la matriz + releer; asignar rol a usuario y que
/// ResolveEffectivePermissions lo refleje; unicidad de nombre por tenant; aislamiento cross-tenant;
/// DeleteAsync bloquea IsSystem y roles con usuarios; catalogo derivado del menu. Reusa las fixtures
/// de aislamiento dual (Testcontainers).
/// </summary>
public abstract class RolesTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected RolesTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateRole_SavePermisos_RoundTrips()
    {
        var tenantId = await NewTenantAsync("Roles RoundTrip");

        var created = await RunAsync(tenantId, s => s.SaveAsync(null, "QA", "rol de prueba", true, Guid.NewGuid()));
        Assert.True(created.IsOk, created.Error);
        var rolId = created.Value!.Id;

        var permisos = new List<ModulePermissionDto>
        {
            new("inventario-items", true, true, false, false),
            new("actividades", true, false, false, false),
            new("vacio", false, false, false, false) // no debe persistir
        };
        var saved = await RunAsync(tenantId, s => s.SavePermisosAsync(rolId, permisos, Guid.NewGuid()));
        Assert.True(saved.IsOk, saved.Error);

        var detail = await RunAsync(tenantId, s => s.GetAsync(rolId));
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Permisos.Count); // la fila vacia no se guardo
        var inv = detail.Permisos.Single(p => p.ModuleKey == "inventario-items");
        Assert.True(inv.CanView);
        Assert.True(inv.CanCreate);
        Assert.False(inv.CanDelete);
    }

    [Fact]
    public async Task SavePermisos_IsReplacedOnResave()
    {
        var tenantId = await NewTenantAsync("Roles Resave");
        var created = await RunAsync(tenantId, s => s.SaveAsync(null, "Reasigna", null, true, Guid.NewGuid()));
        var rolId = created.Value!.Id;

        await RunAsync(tenantId, s => s.SavePermisosAsync(rolId,
            new List<ModulePermissionDto> { new("a", true, false, false, false), new("b", true, false, false, false) },
            Guid.NewGuid()));
        // Reguardar con un set distinto: borra e reinserta (no acumula).
        await RunAsync(tenantId, s => s.SavePermisosAsync(rolId,
            new List<ModulePermissionDto> { new("c", true, true, false, false) },
            Guid.NewGuid()));

        var detail = await RunAsync(tenantId, s => s.GetAsync(rolId));
        Assert.Single(detail!.Permisos);
        Assert.Equal("c", detail.Permisos[0].ModuleKey);
    }

    [Fact]
    public async Task AssignRoleToUser_ReflectedInEffectivePermissions()
    {
        var tenantId = await NewTenantAsync("Roles Asignacion");

        // Usuario Advisor (no Owner/Admin) para que el rol mande.
        Guid platformUserId;
        Guid tenantUserId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var pu = new PlatformUser { Email = "adv@roles.local", DisplayName = "Adv" };
            ctx.PlatformUsers.Add(pu);
            var tu = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = pu.Id,
                Email = "adv@roles.local",
                TenantRole = TenantRole.Advisor
            };
            ctx.TenantUsers.Add(tu);
            await ctx.SaveChangesAsync();
            platformUserId = pu.Id;
            tenantUserId = tu.Id;
        }

        var created = await RunAsync(tenantId, s => s.SaveAsync(null, "Operativo", null, true, Guid.NewGuid()));
        var rolId = created.Value!.Id;
        await RunAsync(tenantId, s => s.SavePermisosAsync(rolId,
            new List<ModulePermissionDto> { new("inventario-items", true, true, false, false) },
            Guid.NewGuid()));

        // Antes de asignar: sin rol -> Unrestricted (regla opt-in B2: conserva acceso del paso 1,
        // no se restringe). No es AllowAll (no ostenta poder organico Owner/Admin) pero Can=true.
        var before = await RunAsync(tenantId, s => s.ResolveEffectivePermissionsAsync(platformUserId));
        Assert.False(before.AllowAll);
        Assert.True(before.Unrestricted);
        Assert.True(before.Can("inventario-items", PermissionAction.View));

        var assigned = await RunAsync(tenantId, s => s.AssignRoleToUserAsync(tenantUserId, rolId, Guid.NewGuid()));
        Assert.True(assigned.IsOk, assigned.Error);

        // Con rol asignado: ya NO es Unrestricted; queda sujeto a su matriz.
        var after = await RunAsync(tenantId, s => s.ResolveEffectivePermissionsAsync(platformUserId));
        Assert.False(after.AllowAll);
        Assert.False(after.Unrestricted);
        Assert.Equal(rolId, after.RolId);
        Assert.True(after.Can("inventario-items", PermissionAction.View));
        Assert.True(after.Can("inventario-items", PermissionAction.Create));
        Assert.False(after.Can("inventario-items", PermissionAction.Delete));
    }

    [Fact]
    public async Task OwnerOrAdmin_ResolveAllowAll_RegardlessOfRole()
    {
        var tenantId = await NewTenantAsync("Roles OwnerAllowAll");
        Guid platformUserId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var pu = new PlatformUser { Email = "owner@roles.local", DisplayName = "Owner" };
            ctx.PlatformUsers.Add(pu);
            ctx.TenantUsers.Add(new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = pu.Id,
                Email = "owner@roles.local",
                TenantRole = TenantRole.Owner
            });
            await ctx.SaveChangesAsync();
            platformUserId = pu.Id;
        }

        var eff = await RunAsync(tenantId, s => s.ResolveEffectivePermissionsAsync(platformUserId));
        Assert.True(eff.AllowAll);
        Assert.True(eff.Can("cualquier-cosa", PermissionAction.Delete));
    }

    [Fact]
    public async Task RoleName_IsUniquePerTenant()
    {
        var tenantId = await NewTenantAsync("Roles Unicidad");
        var first = await RunAsync(tenantId, s => s.SaveAsync(null, "Duplicado", null, true, Guid.NewGuid()));
        Assert.True(first.IsOk);

        var second = await RunAsync(tenantId, s => s.SaveAsync(null, "Duplicado", null, true, Guid.NewGuid()));
        Assert.False(second.IsOk);
        Assert.Equal(RolServiceStatus.Conflict, second.Status);
    }

    [Fact]
    public async Task CrossTenant_Roles_AreIsolated()
    {
        var a = await NewTenantAsync("Roles Tenant A");
        var b = await NewTenantAsync("Roles Tenant B");

        var inA = await RunAsync(a, s => s.SaveAsync(null, "Solo A", null, true, Guid.NewGuid()));
        Assert.True(inA.IsOk);

        var bList = await RunAsync(b, s => s.ListAsync());
        Assert.DoesNotContain(bList, r => r.Id == inA.Value!.Id);

        // Leer el rol de A desde B no lo devuelve (filtro global).
        var fromB = await RunAsync(b, s => s.GetAsync(inA.Value!.Id));
        Assert.Null(fromB);
    }

    [Fact]
    public async Task Delete_BlocksSystemRole()
    {
        var tenantId = await NewTenantAsync("Roles DeleteSystem");
        Guid systemRolId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var rol = new Rol { TenantId = tenantId, Name = "Administrador", IsSystem = true, IsActive = true };
            ctx.Roles.Add(rol);
            await ctx.SaveChangesAsync();
            systemRolId = rol.Id;
        }

        var res = await RunAsync(tenantId, s => s.DeleteAsync(systemRolId, Guid.NewGuid()));
        Assert.False(res.IsOk);
        Assert.Equal(RolServiceStatus.Invalid, res.Status);
    }

    [Fact]
    public async Task Delete_BlocksRoleWithUsers()
    {
        var tenantId = await NewTenantAsync("Roles DeleteWithUsers");
        var created = await RunAsync(tenantId, s => s.SaveAsync(null, "ConUsuarios", null, true, Guid.NewGuid()));
        var rolId = created.Value!.Id;

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var pu = new PlatformUser { Email = "u2@roles.local", DisplayName = "U2" };
            ctx.PlatformUsers.Add(pu);
            ctx.TenantUsers.Add(new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = pu.Id,
                Email = "u2@roles.local",
                TenantRole = TenantRole.Advisor,
                RolId = rolId
            });
            await ctx.SaveChangesAsync();
        }

        var res = await RunAsync(tenantId, s => s.DeleteAsync(rolId, Guid.NewGuid()));
        Assert.False(res.IsOk);
        Assert.Equal(RolServiceStatus.Invalid, res.Status);
    }

    [Fact]
    public async Task Delete_RemovesRoleAndPermisos()
    {
        var tenantId = await NewTenantAsync("Roles DeleteOk");
        var created = await RunAsync(tenantId, s => s.SaveAsync(null, "Borrable", null, true, Guid.NewGuid()));
        var rolId = created.Value!.Id;
        await RunAsync(tenantId, s => s.SavePermisosAsync(rolId,
            new List<ModulePermissionDto> { new("actividades", true, false, false, false) }, Guid.NewGuid()));

        var res = await RunAsync(tenantId, s => s.DeleteAsync(rolId, Guid.NewGuid()));
        Assert.True(res.IsOk, res.Error);

        await using var ctx = _fixture.CreateContext(tenantId);
        Assert.Equal(0, await ctx.Roles.CountAsync(r => r.Id == rolId));
        Assert.Equal(0, await ctx.RolPermisos.CountAsync(p => p.RolId == rolId));
    }

    [Fact]
    public async Task ModuleCatalog_DerivesFromReadyMenuItems()
    {
        var tenantId = await NewTenantAsync("Roles Catalogo");

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = true, SortOrder = 0 };
            ctx.MenuViews.Add(view);
            var section = new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = view.Id,
                Kind = MenuNodeKind.Section,
                Name = "Sistema General",
                Route = "gen",
                SortOrder = 0
            };
            ctx.MenuNodes.Add(section);
            // Item Ready -> entra al catalogo.
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = view.Id,
                ParentId = section.Id,
                Kind = MenuNodeKind.Item,
                Name = "Administracion de usuarios",
                Route = "admin-usuarios",
                State = MenuNodeState.Ready,
                SortOrder = 0
            });
            // Item InDevelopment -> NO entra.
            ctx.MenuNodes.Add(new MenuNode
            {
                TenantId = tenantId,
                MenuViewId = view.Id,
                ParentId = section.Id,
                Kind = MenuNodeKind.Item,
                Name = "Stub",
                Route = "modulo/stub",
                State = MenuNodeState.InDevelopment,
                SortOrder = 1
            });
            await ctx.SaveChangesAsync();
        }

        var catalog = await RunAsync(tenantId, s => s.GetModuleCatalogAsync());
        Assert.Contains(catalog, m => m.Key == "admin-usuarios" && m.Grupo == "Sistema General");
        Assert.DoesNotContain(catalog, m => m.Key == "modulo/stub");
    }

    // ---- Ola B2 (ADR-0033): menu filtrado por "Ver" ----

    [Fact]
    public async Task MenuFilter_LimitedRole_ExcludesModulesWithoutView()
    {
        var tenantId = await NewTenantAsync("Menu Filtrado");
        Guid platformUserId, tenantUserId;
        Guid viewId;

        // Menu: seccion "Inventarios" (2 items) + seccion "Desarrollo" (1 item) + un usuario Advisor.
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = true, SortOrder = 0 };
            ctx.MenuViews.Add(view);
            viewId = view.Id;

            var inv = new MenuNode { TenantId = tenantId, MenuViewId = view.Id, Kind = MenuNodeKind.Section, Name = "Inventarios", Route = "inv", SortOrder = 0 };
            var dev = new MenuNode { TenantId = tenantId, MenuViewId = view.Id, Kind = MenuNodeKind.Section, Name = "Desarrollo", Route = "dev", SortOrder = 1 };
            ctx.MenuNodes.AddRange(inv, dev);
            ctx.MenuNodes.Add(new MenuNode { TenantId = tenantId, MenuViewId = view.Id, ParentId = inv.Id, Kind = MenuNodeKind.Item, Name = "Items", Route = "inventario-items", State = MenuNodeState.Ready, SortOrder = 0 });
            ctx.MenuNodes.Add(new MenuNode { TenantId = tenantId, MenuViewId = view.Id, ParentId = inv.Id, Kind = MenuNodeKind.Item, Name = "Bodegas", Route = "inventario-bodegas", State = MenuNodeState.Ready, SortOrder = 1 });
            ctx.MenuNodes.Add(new MenuNode { TenantId = tenantId, MenuViewId = view.Id, ParentId = dev.Id, Kind = MenuNodeKind.Item, Name = "Reglas", Route = "reglas", State = MenuNodeState.Ready, SortOrder = 0 });

            var pu = new PlatformUser { Email = "lim@roles.local", DisplayName = "Limitado" };
            ctx.PlatformUsers.Add(pu);
            var tu = new TenantUser { TenantId = tenantId, PlatformUserId = pu.Id, Email = "lim@roles.local", TenantRole = TenantRole.Advisor };
            ctx.TenantUsers.Add(tu);
            await ctx.SaveChangesAsync();
            platformUserId = pu.Id;
            tenantUserId = tu.Id;
        }

        // Rol: Ver en inventario-items, NADA de la seccion Desarrollo, sin inventario-bodegas.
        var rol = await RunAsync(tenantId, s => s.SaveAsync(null, "Limitado", null, true, Guid.NewGuid()));
        await RunAsync(tenantId, s => s.SavePermisosAsync(rol.Value!.Id, new List<ModulePermissionDto>
        {
            new("inventario-items", true, false, false, false)
        }, Guid.NewGuid()));
        await RunAsync(tenantId, s => s.AssignRoleToUserAsync(tenantUserId, rol.Value!.Id, Guid.NewGuid()));

        var eff = await RunAsync(tenantId, s => s.ResolveEffectivePermissionsAsync(platformUserId));
        Assert.False(eff.Unrestricted);
        Assert.True(eff.Can("inventario-items", PermissionAction.View));
        Assert.False(eff.Can("reglas", PermissionAction.View));

        var menu = await ResolveMenuAsync(tenantId, viewId);
        var filtered = MenuPermissionFilter.Filter(menu, eff);

        Assert.NotNull(filtered);
        // Solo queda la seccion Inventarios con SU unico item visible (inventario-items).
        var section = Assert.Single(filtered!.Roots, n => n.Kind == MenuNodeKind.Section);
        Assert.Equal("inv", section.Route);
        var leaf = Assert.Single(section.Children);
        Assert.Equal("inventario-items", leaf.Route);
        // La seccion Desarrollo (sin ningun item con Ver) desaparece del arbol.
        Assert.DoesNotContain(filtered.Roots, n => n.Route == "dev");
    }

    [Fact]
    public async Task MenuFilter_OwnerAndNoRole_SeeFullMenu()
    {
        var tenantId = await NewTenantAsync("Menu Completo");
        Guid ownerUserId, noRoleUserId, viewId;

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var view = new MenuView { TenantId = tenantId, Name = "Completo", IsDefault = true, SortOrder = 0 };
            ctx.MenuViews.Add(view);
            viewId = view.Id;
            var sec = new MenuNode { TenantId = tenantId, MenuViewId = view.Id, Kind = MenuNodeKind.Section, Name = "Sistema", Route = "sys", SortOrder = 0 };
            ctx.MenuNodes.Add(sec);
            ctx.MenuNodes.Add(new MenuNode { TenantId = tenantId, MenuViewId = view.Id, ParentId = sec.Id, Kind = MenuNodeKind.Item, Name = "Reglas", Route = "reglas", State = MenuNodeState.Ready, SortOrder = 0 });
            ctx.MenuNodes.Add(new MenuNode { TenantId = tenantId, MenuViewId = view.Id, ParentId = sec.Id, Kind = MenuNodeKind.Item, Name = "Items", Route = "inventario-items", State = MenuNodeState.Ready, SortOrder = 1 });

            var owner = new PlatformUser { Email = "own@roles.local", DisplayName = "Owner" };
            var advisor = new PlatformUser { Email = "sr@roles.local", DisplayName = "SinRol" };
            ctx.PlatformUsers.AddRange(owner, advisor);
            ctx.TenantUsers.Add(new TenantUser { TenantId = tenantId, PlatformUserId = owner.Id, Email = "own@roles.local", TenantRole = TenantRole.Owner });
            ctx.TenantUsers.Add(new TenantUser { TenantId = tenantId, PlatformUserId = advisor.Id, Email = "sr@roles.local", TenantRole = TenantRole.Advisor });
            await ctx.SaveChangesAsync();
            ownerUserId = owner.Id;
            noRoleUserId = advisor.Id;
        }

        var menu = await ResolveMenuAsync(tenantId, viewId);

        foreach (var uid in new[] { ownerUserId, noRoleUserId })
        {
            var eff = await RunAsync(tenantId, s => s.ResolveEffectivePermissionsAsync(uid));
            Assert.True(eff.Unrestricted);
            var filtered = MenuPermissionFilter.Filter(menu, eff);
            var section = Assert.Single(filtered!.Roots, n => n.Kind == MenuNodeKind.Section);
            Assert.Equal(2, section.Children.Count); // ambos items visibles
        }
    }

    // ---- Helpers ----

    private async Task<ResolvedMenuDto?> ResolveMenuAsync(Guid tenantId, Guid viewId)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = new Ecorex.Application.MenuConfig.MenuConfigService(ctx, new TestTenantContext(tenantId));
        return await svc.GetMenuForTenantUserAsync(tenantId, viewId);
    }

    private async Task<Guid> NewTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<T> RunAsync<T>(Guid tenantId, Func<IRolService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var service = new RolService(ctx, new TestTenantContext(tenantId), new NoOpAuditWriter());
        return await action(service);
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public void Write(Guid actorUserId, string actionName, string entityName, Guid? entityId,
            object? previousValue, object? newValue, Guid? tenantId = null, string? reason = null,
            AuditActorType actorType = AuditActorType.Human)
        {
            // Los tests no persisten auditoria; el interceptor ya estampa tenant/fechas.
        }
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class RolesTests_Postgres
    : RolesTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public RolesTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class RolesTests_SqlServer
    : RolesTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public RolesTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
