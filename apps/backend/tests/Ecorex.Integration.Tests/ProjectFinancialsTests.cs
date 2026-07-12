using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de Proyectos P2 (presupuesto/costos + DOFA) en matriz dual PostgreSQL /
/// SQL Server: agregar rubros y verificar montos/totales, y agregar entradas DOFA por cuadrante.
/// </summary>
public abstract class ProjectFinancialsTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ProjectFinancialsTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task BudgetItems_AddAndList_ComputeAmounts()
    {
        var (tenantId, projectId) = await NewProjectAsync("Proyecto Presupuesto");

        var a = await RunAsync(tenantId, s => s.AddBudgetItemAsync(projectId,
            new CreateBudgetItemRequest("Licencias", PlannedAmount: 1000m, ActualAmount: 800m, Category: "Software")));
        Assert.True(a.IsOk, a.Error);
        var b = await RunAsync(tenantId, s => s.AddBudgetItemAsync(projectId,
            new CreateBudgetItemRequest("Consultoria", PlannedAmount: 500m, ActualAmount: 700m)));
        Assert.True(b.IsOk, b.Error);

        var items = await RunAsync(tenantId, s => s.ListBudgetItemsAsync(projectId));
        Assert.Equal(2, items.Count);
        Assert.Equal(1500m, items.Sum(x => x.PlannedAmount));
        Assert.Equal(1500m, items.Sum(x => x.ActualAmount));
        Assert.Contains(items, x => x.Name == "Licencias" && x.Category == "Software" && x.PlannedAmount == 1000m);
    }

    [Fact]
    public async Task Dofa_AddAndList_GroupsByQuadrant()
    {
        var (tenantId, projectId) = await NewProjectAsync("Proyecto DOFA");

        Assert.True((await RunAsync(tenantId, s => s.AddDofaAsync(projectId, new CreateDofaRequest(DofaQuadrant.Fortaleza, "Equipo experto")))).IsOk);
        Assert.True((await RunAsync(tenantId, s => s.AddDofaAsync(projectId, new CreateDofaRequest(DofaQuadrant.Amenaza, "Competencia agresiva")))).IsOk);

        var entries = await RunAsync(tenantId, s => s.ListDofaAsync(projectId));
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Quadrant == DofaQuadrant.Fortaleza && e.Text == "Equipo experto");
        Assert.Contains(entries, e => e.Quadrant == DofaQuadrant.Amenaza);
    }

    // ---- Helpers ----

    private async Task<(Guid TenantId, Guid ProjectId)> NewProjectAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        var pu = new PlatformUser { Email = $"owner-{projectId:N}@fin.local", DisplayName = "Owner" };
        ctx.PlatformUsers.Add(pu);
        var owner = new TenantUser { TenantId = tenantId, PlatformUserId = pu.Id, Email = pu.Email, TenantRole = TenantRole.Owner };
        ctx.TenantUsers.Add(owner);
        ctx.Projects.Add(new Project { Id = projectId, TenantId = tenantId, Code = "PRJ-FIN", Name = name, OwnerTenantUserId = owner.Id });
        await ctx.SaveChangesAsync();
        return (tenantId, projectId);
    }

    private async Task<T> RunAsync<T>(Guid tenantId, Func<IProjectService, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var service = new ProjectService(ctx, new TestTenantContext(tenantId));
        return await action(service);
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ProjectFinancialsTests_Postgres
    : ProjectFinancialsTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ProjectFinancialsTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ProjectFinancialsTests_SqlServer
    : ProjectFinancialsTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ProjectFinancialsTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
