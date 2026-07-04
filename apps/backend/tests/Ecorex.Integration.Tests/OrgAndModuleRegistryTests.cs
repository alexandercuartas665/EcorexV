using Ecorex.Application.Common;
using Ecorex.Application.Modules;
using Ecorex.Application.Organization;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de los modulos de sistema FASE 5 (ADR-0017) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// (1) CRUD del arbol de dependencias + ciclo rechazado (una unidad no puede ser su
/// propio ancestro), (2) aislamiento cross-tenant de OrgUnit/TenantModule, (3) el
/// catalogo global ModuleDefinition es visible desde AMBOS tenants pero el estado
/// TenantModule esta aislado, y (4) habilitar/deshabilitar modulo por tenant
/// (incluida la proteccion de modulos nucleo).
/// </summary>
public abstract class OrgAndModuleRegistryTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected OrgAndModuleRegistryTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- (1) Arbol CRUD + ciclo rechazado ----

    [Fact]
    public async Task OrgUnitTree_CrudAndCycleRejected()
    {
        var seed = await SeedTenantAsync("Org Arbol");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = new OrgUnitService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId));

        // Crear raiz > hija > nieta, con responsable y miembro.
        var root = (await service.CreateAsync(new SaveOrgUnitRequest(
            "Direccion General", OrgUnitKind.Area, ResponsibleTenantUserId: seed.TenantUserId))).Value!;
        var child = (await service.CreateAsync(new SaveOrgUnitRequest(
            "Tecnologia", OrgUnitKind.Area, ParentId: root.Id))).Value!;
        var grandChild = (await service.CreateAsync(new SaveOrgUnitRequest(
            "Desarrollo", OrgUnitKind.Team, ParentId: child.Id))).Value!;

        var addMember = await service.AddMemberAsync(grandChild.Id, seed.TenantUserId, "Desarrollador");
        Assert.True(addMember.IsOk, addMember.Error);
        // Miembro duplicado -> Conflict.
        var duplicated = await service.AddMemberAsync(grandChild.Id, seed.TenantUserId);
        Assert.Equal(OrgServiceStatus.Conflict, duplicated.Status);
        // Listado de miembros (query traducida por EF en ambos motores).
        var members = await service.ListMembersAsync(grandChild.Id);
        var onlyMember = Assert.Single(members);
        Assert.Equal(seed.TenantUserId, onlyMember.TenantUserId);
        Assert.Equal("Desarrollador", onlyMember.Role);

        // Arbol completo ordenado: 1 raiz con 1 hija con 1 nieta (con conteo de miembros).
        var tree = await service.GetTreeAsync();
        var rootNode = Assert.Single(tree);
        Assert.Equal("Direccion General", rootNode.Name);
        var childNode = Assert.Single(rootNode.Children);
        var grandChildNode = Assert.Single(childNode.Children);
        Assert.Equal(1, grandChildNode.MemberCount);
        Assert.Equal(seed.TenantUserId, rootNode.ResponsibleTenantUserId);

        // KPIs: 3 dependencias, 2 areas, 1 usuario asignado (responsable + miembro = mismo usuario).
        var kpis = await service.GetKpisAsync();
        Assert.Equal(3, kpis.TotalUnits);
        Assert.Equal(2, kpis.Areas);
        Assert.Equal(1, kpis.AssignedUsers);

        // Update valido: renombrar y mover la nieta bajo la raiz.
        var moved = await service.UpdateAsync(grandChild.Id, new SaveOrgUnitRequest(
            "Desarrollo Core", OrgUnitKind.Team, ParentId: root.Id));
        Assert.True(moved.IsOk, moved.Error);
        Assert.Equal(root.Id, moved.Value!.ParentId);

        // CICLO rechazado: la raiz no puede colgar de su descendiente (error tipado Invalid).
        var cycle = await service.UpdateAsync(root.Id, new SaveOrgUnitRequest(
            "Direccion General", OrgUnitKind.Area, ParentId: child.Id));
        Assert.Equal(OrgServiceStatus.Invalid, cycle.Status);
        Assert.Contains("ciclo", cycle.Error, StringComparison.OrdinalIgnoreCase);
        // Auto-referencia tambien rechazada.
        var selfParent = await service.UpdateAsync(root.Id, new SaveOrgUnitRequest(
            "Direccion General", OrgUnitKind.Area, ParentId: root.Id));
        Assert.Equal(OrgServiceStatus.Invalid, selfParent.Status);

        // Archivar: bloqueado si tiene hijas activas; permitido en hoja. Nunca DELETE fisico.
        var archiveBlocked = await service.SetArchivedAsync(root.Id, archived: true);
        Assert.Equal(OrgServiceStatus.Invalid, archiveBlocked.Status);
        var archiveLeaf = await service.SetArchivedAsync(moved.Value.Id, archived: true);
        Assert.True(archiveLeaf.IsOk, archiveLeaf.Error);
        Assert.Equal(2, (await service.GetKpisAsync()).TotalUnits);
        Assert.Equal(3, await ctx.OrgUnits.CountAsync()); // sigue en base (soft-delete)
    }

    // ---- (2) Aislamiento cross-tenant de OrgUnit / TenantModule ----

    [Fact]
    public async Task OrgUnitsAndTenantModules_AreTenantIsolated()
    {
        var seedA = await SeedTenantAsync("Org Tenant A");
        var seedB = await SeedTenantAsync("Org Tenant B");
        var moduleId = await SeedModuleDefinitionAsync("000850", "Dependencias");

        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var orgA = new OrgUnitService(ctxA, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId));
            var created = await orgA.CreateAsync(new SaveOrgUnitRequest("Solo de A", OrgUnitKind.Area));
            Assert.True(created.IsOk, created.Error);

            var registryA = new ModuleRegistryService(ctxA, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId));
            var enabled = await registryA.SetModuleEnabledAsync(moduleId, enabled: true);
            Assert.True(enabled.IsOk, enabled.Error);
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            // El filtro global oculta los datos de A: B no ve unidades ni estados de A.
            Assert.Empty(await ctxB.OrgUnits.ToListAsync());
            Assert.Empty(await ctxB.OrgUnitMembers.ToListAsync());
            Assert.Empty(await ctxB.TenantModules.ToListAsync());

            var orgB = new OrgUnitService(ctxB, new TestTenantContext(seedB.TenantId, seedB.PlatformUserId));
            Assert.Empty(await orgB.GetTreeAsync());
            Assert.Equal(0, (await orgB.GetKpisAsync()).TotalUnits);
        }

        // Sin tenant activo (fail-closed): nada visible.
        await using (var ctxNone = _fixture.CreateContext(tenantId: null))
        {
            Assert.Empty(await ctxNone.OrgUnits.ToListAsync());
            Assert.Empty(await ctxNone.TenantModules.ToListAsync());
        }
    }

    // ---- (3) Catalogo global visible desde ambos tenants; estado aislado ----

    [Fact]
    public async Task ModuleDefinitions_AreGlobal_ButTenantStateIsIsolated()
    {
        var seedA = await SeedTenantAsync("Registry Tenant A");
        var seedB = await SeedTenantAsync("Registry Tenant B");
        var moduleId = await SeedModuleDefinitionAsync("000109", "Modulos web");

        // A habilita el modulo y guarda settings propios.
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var registryA = new ModuleRegistryService(ctxA, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId));
            var rowsA = await registryA.ListCatalogAsync();
            var rowA = Assert.Single(rowsA, r => r.ModuleDefinitionId == moduleId);
            Assert.False(rowA.IsEnabled); // sin fila TenantModule = deshabilitado

            Assert.True((await registryA.SetModuleEnabledAsync(moduleId, true)).IsOk);
            var settings = await registryA.UpdateSettingsAsync(moduleId, """{"color":"violeta"}""");
            Assert.True(settings.IsOk, settings.Error);

            // JSON invalido y no-objeto: error tipado.
            Assert.Equal(OrgServiceStatus.Invalid, (await registryA.UpdateSettingsAsync(moduleId, "no-json")).Status);
            Assert.Equal(OrgServiceStatus.Invalid, (await registryA.UpdateSettingsAsync(moduleId, "[1,2]")).Status);
        }

        // B VE la definicion global (catalogo compartido) pero SIN el estado de A.
        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            var registryB = new ModuleRegistryService(ctxB, new TestTenantContext(seedB.TenantId, seedB.PlatformUserId));
            var rowsB = await registryB.ListCatalogAsync();
            var rowB = Assert.Single(rowsB, r => r.ModuleDefinitionId == moduleId);
            Assert.Equal("000109", rowB.LegacyCode);       // catalogo global visible
            Assert.False(rowB.IsEnabled);                   // estado de A NO se filtra a B
            Assert.Null(rowB.SettingsJson);

            // GetEnabledModulesAsync es fail-closed: B no puede consultar el tenant A.
            var registryBForA = await registryB.GetEnabledModulesAsync(seedA.TenantId);
            Assert.Empty(registryBForA);
        }

        // A si ve su modulo habilitado con sus settings (fuente para el menu del registry).
        await using (var ctxA2 = _fixture.CreateContext(seedA.TenantId))
        {
            var registryA2 = new ModuleRegistryService(ctxA2, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId));
            var enabledA = await registryA2.GetEnabledModulesAsync(seedA.TenantId);
            var moduleA = Assert.Single(enabledA);
            Assert.Equal("000109", moduleA.LegacyCode);
            // Comparacion semantica: jsonb (PG) normaliza el formato del documento.
            using var settingsDoc = System.Text.Json.JsonDocument.Parse(moduleA.SettingsJson!);
            Assert.Equal("violeta", settingsDoc.RootElement.GetProperty("color").GetString());
        }

        // Sin tenant ambiente (PlatformAdmin) el tenant explicito SI se puede consultar.
        await using (var ctxPlatform = _fixture.CreateContext(tenantId: null))
        {
            var registryPlatform = new ModuleRegistryService(ctxPlatform, new TestTenantContext(null));
            Assert.Single(await registryPlatform.GetEnabledModulesAsync(seedA.TenantId));
            Assert.Empty(await registryPlatform.GetEnabledModulesAsync(seedB.TenantId));
        }
    }

    // ---- (4) Habilitar / deshabilitar modulo por tenant ----

    [Fact]
    public async Task SetModuleEnabled_TogglesPerTenant_AndProtectsCoreModules()
    {
        var seed = await SeedTenantAsync("Registry Toggle");
        var normalId = await SeedModuleDefinitionAsync("000291", "Flujos");
        var coreId = await SeedModuleDefinitionAsync("000038", "Actividades", isCore: true);

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var registry = new ModuleRegistryService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId));

        // Habilitar crea la fila TenantModule; deshabilitar la conserva (idempotente re-toggle).
        Assert.True((await registry.SetModuleEnabledAsync(normalId, true)).IsOk);
        Assert.True((await registry.ListCatalogAsync()).Single(r => r.ModuleDefinitionId == normalId).IsEnabled);

        Assert.True((await registry.SetModuleEnabledAsync(normalId, false)).IsOk);
        Assert.False((await registry.ListCatalogAsync()).Single(r => r.ModuleDefinitionId == normalId).IsEnabled);
        Assert.Equal(1, await ctx.TenantModules.CountAsync(tm => tm.ModuleDefinitionId == normalId));

        Assert.True((await registry.SetModuleEnabledAsync(normalId, true)).IsOk);
        var enabled = await registry.GetEnabledModulesAsync(seed.TenantId);
        Assert.Contains(enabled, m => m.ModuleDefinitionId == normalId);

        // Modulo NUCLEO: se puede habilitar pero NO deshabilitar (error tipado).
        Assert.True((await registry.SetModuleEnabledAsync(coreId, true)).IsOk);
        var coreOff = await registry.SetModuleEnabledAsync(coreId, false);
        Assert.Equal(OrgServiceStatus.Invalid, coreOff.Status);
        Assert.Contains("nucleo", coreOff.Error, StringComparison.OrdinalIgnoreCase);

        // Modulo inexistente -> NotFound.
        Assert.Equal(OrgServiceStatus.NotFound, (await registry.SetModuleEnabledAsync(Guid.CreateVersion7(), true)).Status);
    }

    // ---- Helpers ----

    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid tenantUserId;
        Guid platformUserId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser
            {
                Email = $"user-{tenantId:N}@org.test",
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            ctx.PlatformUsers.Add(platformUser);
            var tenantUser = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = platformUser.Id,
                Email = platformUser.Email
            };
            ctx.TenantUsers.Add(tenantUser);
            await ctx.SaveChangesAsync();
            tenantUserId = tenantUser.Id;
            platformUserId = platformUser.Id;
        }

        return new SeedData(tenantId, tenantUserId, platformUserId);
    }

    /// <summary>Upsert de una definicion GLOBAL del catalogo (sin tenant, como el PlatformAdmin).</summary>
    private async Task<Guid> SeedModuleDefinitionAsync(string legacyCode, string name, bool isCore = false)
    {
        await using var ctx = _fixture.CreateContext(tenantId: null);
        var existing = await ctx.ModuleDefinitions.FirstOrDefaultAsync(d => d.LegacyCode == legacyCode);
        if (existing is not null)
        {
            return existing.Id;
        }
        var definition = new ModuleDefinition
        {
            LegacyCode = legacyCode,
            Name = name,
            Area = ModuleArea.Sistema,
            IsCore = isCore
        };
        ctx.ModuleDefinitions.Add(definition);
        await ctx.SaveChangesAsync();
        return definition.Id;
    }

    private sealed record SeedData(Guid TenantId, Guid TenantUserId, Guid PlatformUserId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class OrgAndModuleRegistryTests_Postgres
    : OrgAndModuleRegistryTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public OrgAndModuleRegistryTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class OrgAndModuleRegistryTests_SqlServer
    : OrgAndModuleRegistryTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public OrgAndModuleRegistryTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
