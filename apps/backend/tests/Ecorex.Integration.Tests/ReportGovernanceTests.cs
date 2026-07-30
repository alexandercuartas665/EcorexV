using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Gobernanza de reportes por rol (ADR-0051) en matriz DUAL PostgreSQL / SQL Server:
/// - AISLAMIENTO cross-tenant de <see cref="ReportDefinitionRole"/>: una asignacion de otro tenant no
///   existe por construccion (filtro global), y el reporte de otro tenant no aparece.
/// - <c>ListAsync</c> filtra por rol: el admin (seeAll) ve todos; un rol ve los asignados a el mas los
///   que no tienen asignacion; otro rol NO ve los asignados a un rol distinto; un reporte sin
///   asignaciones lo ven todos.
/// </summary>
public abstract class ReportGovernanceTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ReportGovernanceTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ListAsync_FiltersByRole_AdminSeesAll_UnassignedVisibleToAll()
    {
        var t = await SeedTenantAsync();
        var supervisor = await AddRolAsync(t, "Supervisor");
        var otro = await AddRolAsync(t, "Otro");

        var asignado = await AddReportAsync(t, "Solo Supervisor");
        var libre = await AddReportAsync(t, "Para todos");

        await AssignAsync(t, asignado, new[] { supervisor });

        // Admin (seeAll) ve ambos.
        var admin = await ListAsync(t, seeAll: true, rolId: null);
        Assert.Contains(admin, r => r.Id == asignado);
        Assert.Contains(admin, r => r.Id == libre);

        // Supervisor ve el asignado + el libre.
        var sup = await ListAsync(t, seeAll: false, rolId: supervisor);
        Assert.Contains(sup, r => r.Id == asignado);
        Assert.Contains(sup, r => r.Id == libre);

        // Otro rol ve solo el libre (NO el asignado a Supervisor).
        var other = await ListAsync(t, seeAll: false, rolId: otro);
        Assert.DoesNotContain(other, r => r.Id == asignado);
        Assert.Contains(other, r => r.Id == libre);

        // Sin rol y sin ser admin: solo el libre.
        var sinRol = await ListAsync(t, seeAll: false, rolId: null);
        Assert.DoesNotContain(sinRol, r => r.Id == asignado);
        Assert.Contains(sinRol, r => r.Id == libre);
    }

    [Fact]
    public async Task ReportDefinitionRole_IsTenantIsolated()
    {
        var a = await SeedTenantAsync();
        var b = await SeedTenantAsync();
        var rolA = await AddRolAsync(a, "RolA");
        var repA = await AddReportAsync(a, "Reporte de A");
        await AssignAsync(a, repA, new[] { rolA });

        // En contexto del tenant B: ni la asignacion ni el reporte de A existen.
        await using var ctxB = _fixture.CreateContext(b);
        var svcB = new ReportDefinitionService(ctxB, new TestTenantContext(b), null!);

        var asignadosVistosPorB = await svcB.GetAssignedRolesAsync(repA);
        Assert.Empty(asignadosVistosPorB);

        var listaB = await svcB.ListAsync(seeAll: true, rolId: null);
        Assert.DoesNotContain(listaB, r => r.Id == repA);
    }

    // ---- Helpers ----

    private async Task<IReadOnlyList<ReportDefinitionSummary>> ListAsync(Guid tenantId, bool seeAll, Guid? rolId)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = new ReportDefinitionService(ctx, new TestTenantContext(tenantId), null!);
        return await svc.ListAsync(seeAll, rolId);
    }

    private async Task AssignAsync(Guid tenantId, Guid reportId, Guid[] rolIds)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var svc = new ReportDefinitionService(ctx, new TestTenantContext(tenantId), null!);
        await svc.AssignRolesAsync(reportId, rolIds);
    }

    private async Task<Guid> SeedTenantAsync()
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "T-" + tenantId.ToString("N")[..8] });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Guid> AddRolAsync(Guid tenantId, string name)
    {
        var id = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId);
        ctx.Roles.Add(new Rol { Id = id, TenantId = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddReportAsync(Guid tenantId, string name)
    {
        var id = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId);
        ctx.ReportDefinitions.Add(new ReportDefinition
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Kind = ReportDefinitionKind.Dashboard,
            Status = ReportDefinitionStatus.Active,
            SpecJson = "{}"
        });
        await ctx.SaveChangesAsync();
        return id;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ReportGovernanceTests_Postgres
    : ReportGovernanceTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ReportGovernanceTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ReportGovernanceTests_SqlServer
    : ReportGovernanceTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ReportGovernanceTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
