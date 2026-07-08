using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la asignacion por nodo (ADR-0035, ola F1) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// (1) persistir una policy (Cargo) sobre un nodo Task y RE-LEER los candidatos resueltos
/// (funcionarios del cargo) desde la BD; (2) unicidad (WorkflowNodeId, OrgUnitId) y rechazo
/// de Funcionario como asignable; (3) aislamiento cross-tenant; (4) borrar el nodo/definicion
/// cascada la policy (y NO ACTION hacia la unidad).
/// </summary>
public abstract class NodeAssignmentTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected NodeAssignmentTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Policy_Persists_And_Resolver_ReturnsFunctionaries()
    {
        var seed = await SeedTenantAsync("NodeAssign Resolve");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var org = new OrgUnitService(ctx, tenantCtx);
        var policySvc = new WorkflowNodePolicyService(ctx, tenantCtx);

        // Comercial (Dependencia) -> Asesor (Cargo) -> Funcionario ligado al usuario del tenant.
        var comercial = (await org.CreateAsync(new SaveOrgUnitRequest(
            "Comercial", Classifier: OrgUnitClassifier.Dependencia))).Value!;
        var cargo = (await org.CreateAsync(new SaveOrgUnitRequest(
            "Asesor", ParentId: comercial.Id, Classifier: OrgUnitClassifier.Cargo))).Value!;
        var funcionario = await org.CreateAsync(new SaveOrgUnitRequest(
            "Ana Ocupante", ParentId: cargo.Id,
            Classifier: OrgUnitClassifier.Funcionario, TenantUserId: seed.TenantUserId));
        Assert.True(funcionario.IsOk, funcionario.Error);

        var (_, taskNodeId) = await SeedFlowWithTaskAsync(ctx, seed.TenantId);

        // Asignar el Cargo al nodo Task.
        var added = await policySvc.AddNodePolicyAsync(taskNodeId, cargo.Id);
        Assert.True(added.IsOk, added.Error);
        Assert.Equal(OrgUnitClassifier.Cargo, added.Value!.Classifier);
        Assert.Equal(1, added.Value.CandidateCount);

        // Re-leer desde otra conexion: la policy y el candidato persistieron.
        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var resolver2 = new NodeAssigneeResolver(ctx2);
        var candidates = await resolver2.ResolveCandidatesAsync(taskNodeId);
        Assert.Equal(seed.TenantUserId, Assert.Single(candidates));

        var policies = await new WorkflowNodePolicyService(ctx2, tenantCtx).ListNodePoliciesAsync(taskNodeId);
        var row = Assert.Single(policies);
        Assert.Equal(cargo.Id, row.OrgUnitId);
        Assert.Equal(1, row.CandidateCount);
    }

    [Fact]
    public async Task Policy_UniqueAndRejectsFunctionario()
    {
        var seed = await SeedTenantAsync("NodeAssign Unique");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var org = new OrgUnitService(ctx, tenantCtx);
        var policySvc = new WorkflowNodePolicyService(ctx, tenantCtx);

        var cargo = (await org.CreateAsync(new SaveOrgUnitRequest(
            "Aprobador", Classifier: OrgUnitClassifier.Cargo))).Value!;
        var funcionario = (await org.CreateAsync(new SaveOrgUnitRequest(
            "Caro", ParentId: cargo.Id,
            Classifier: OrgUnitClassifier.Funcionario, TenantUserId: seed.TenantUserId))).Value!;

        var (_, taskNodeId) = await SeedFlowWithTaskAsync(ctx, seed.TenantId);

        Assert.True((await policySvc.AddNodePolicyAsync(taskNodeId, cargo.Id)).IsOk);
        // Duplicado -> Conflict (indice unico WorkflowNodeId, OrgUnitId).
        Assert.Equal(OrgServiceStatus.Conflict, (await policySvc.AddNodePolicyAsync(taskNodeId, cargo.Id)).Status);
        // Funcionario NO asignable -> Invalid.
        var rejected = await policySvc.AddNodePolicyAsync(taskNodeId, funcionario.Id);
        Assert.Equal(OrgServiceStatus.Invalid, rejected.Status);
        Assert.Contains("Funcionario", rejected.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Policies_AreTenantIsolated()
    {
        var seedA = await SeedTenantAsync("NodeAssign A");
        var seedB = await SeedTenantAsync("NodeAssign B");

        Guid taskNodeA;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var org = new OrgUnitService(ctxA, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId));
            var cargo = (await org.CreateAsync(new SaveOrgUnitRequest(
                "Cargo A", Classifier: OrgUnitClassifier.Cargo))).Value!;
            (_, taskNodeA) = await SeedFlowWithTaskAsync(ctxA, seedA.TenantId);
            var policyA = new WorkflowNodePolicyService(ctxA, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId));
            Assert.True((await policyA.AddNodePolicyAsync(taskNodeA, cargo.Id)).IsOk);
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            // El filtro global oculta el nodo y la policy de A.
            Assert.Empty(await ctxB.WorkflowNodePolicies.ToListAsync());
            var policyB = new WorkflowNodePolicyService(ctxB, new TestTenantContext(seedB.TenantId, seedB.PlatformUserId));
            Assert.Empty(await policyB.ListNodePoliciesAsync(taskNodeA));
            Assert.Empty(await new NodeAssigneeResolver(ctxB).ResolveCandidatesAsync(taskNodeA));
        }
    }

    [Fact]
    public async Task DeletingNode_CascadesPolicy()
    {
        var seed = await SeedTenantAsync("NodeAssign Cascade");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var org = new OrgUnitService(ctx, tenantCtx);
        var policySvc = new WorkflowNodePolicyService(ctx, tenantCtx);

        var cargo = (await org.CreateAsync(new SaveOrgUnitRequest(
            "Cargo Cascade", Classifier: OrgUnitClassifier.Cargo))).Value!;
        var (definitionId, taskNodeId) = await SeedFlowWithTaskAsync(ctx, seed.TenantId);
        Assert.True((await policySvc.AddNodePolicyAsync(taskNodeId, cargo.Id)).IsOk);
        Assert.Equal(1, await ctx.WorkflowNodePolicies.CountAsync());

        // Borrar la definicion cascada nodos -> policies (FK cascade node->policy).
        var definition = await ctx.WorkflowDefinitions.FirstAsync(d => d.Id == definitionId);
        ctx.WorkflowDefinitions.Remove(definition);
        await ctx.SaveChangesAsync();

        Assert.Equal(0, await ctx.WorkflowNodePolicies.CountAsync());
        Assert.Equal(0, await ctx.WorkflowNodes.CountAsync());
        // La unidad NO se borra (NO ACTION hacia OrgUnit): sigue en base.
        Assert.Equal(1, await ctx.OrgUnits.CountAsync(u => u.Id == cargo.Id));
    }

    // ---- Helpers ----

    /// <summary>Crea una definicion minima con un nodo Task y devuelve (definitionId, taskNodeId).</summary>
    private static async Task<(Guid DefinitionId, Guid TaskNodeId)> SeedFlowWithTaskAsync(
        IApplicationDbContext ctx, Guid tenantId)
    {
        var definition = new WorkflowDefinition
        {
            TenantId = tenantId,
            ProcessCode = $"NA-{Guid.NewGuid():N}"[..12],
            Name = "Flujo asignacion",
            BpmnXml = "<xml/>",
            Version = 1
        };
        ctx.WorkflowDefinitions.Add(definition);
        var task = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "Task_1",
            Name = "Paso",
            NodeType = WorkflowNodeType.Task,
            AllowsAssignment = true
        };
        ctx.WorkflowNodes.Add(task);
        await ctx.SaveChangesAsync();
        return (definition.Id, task.Id);
    }

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
                Email = $"user-{tenantId:N}@na.test",
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

    private sealed record SeedData(Guid TenantId, Guid TenantUserId, Guid PlatformUserId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class NodeAssignmentTests_Postgres
    : NodeAssignmentTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public NodeAssignmentTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class NodeAssignmentTests_SqlServer
    : NodeAssignmentTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public NodeAssignmentTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
