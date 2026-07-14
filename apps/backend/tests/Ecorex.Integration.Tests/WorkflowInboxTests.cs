using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la bandeja operativa de flujos (ola F2, ADR-0036) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Verifica el flujo
/// end-to-end: crear una tarea de un ActivityType con flujo publicado arranca la instancia con
/// un paso Pending; GetMyPendingSteps lo trae para el candidato del cargo y NO para un extrano;
/// Claim lo asigna; CompletePendingStep avanza al siguiente nodo cuyo candidato es otro cargo;
/// al llegar a la compuerta, CompleteStep con 'Aprobada' enruta a Facturacion y con 'Rechazada'
/// vuelve por el loop de reinicio. Ademas: aislamiento cross-tenant de la bandeja.
/// </summary>
public abstract class WorkflowInboxTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected WorkflowInboxTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // start -> Requerimiento -> Cotizacion -> Gateway; Aprobada -> Facturacion -> end;
    // Rechazada -> End_Reinicio (RestartNodeId -> Cotizacion).
    private const string InboxXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          id="inbox" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Inbox">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_Req" name="Requerimiento" />
            <bpmn:task id="Task_Cot" name="Cotizacion" />
            <bpmn:exclusiveGateway id="Gw_Ap" name="Aprobacion" />
            <bpmn:task id="Task_Fac" name="Facturacion" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:endEvent id="End_Re" name="Rechazada" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_Req" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_Req" targetRef="Task_Cot" />
            <bpmn:sequenceFlow id="F3" sourceRef="Task_Cot" targetRef="Gw_Ap" />
            <bpmn:sequenceFlow id="F4" name="Aprobada" sourceRef="Gw_Ap" targetRef="Task_Fac">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Aprobada'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="F5" name="Rechazada" sourceRef="Gw_Ap" targetRef="End_Re">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">approval == 'Rechazada'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="F6" sourceRef="Task_Fac" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task EndToEnd_CandidateSeesStep_Claims_Completes_AdvancesToNextCargo()
    {
        var seed = await SeedTenantAsync("Inbox E2E");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var engine = BuildEngine(ctx, seed);
        var (asesorId, aprobadorId, extranoId) = await SeedCargosAsync(ctx, tenantCtx, seed);

        var definition = await PublishFlowAsync(engine, ctx, seed);
        await AttachPoliciesAsync(ctx, tenantCtx, definition, asesorCargoId: asesorId.CargoId, aprobadorCargoId: aprobadorId.CargoId);

        // Crear la tarea via TaskItemService: arranca el flujo, paso Requerimiento Pending.
        var taskService = BuildTaskService(ctx, seed, engine);
        var created = await taskService.CreateAsync(
            new CreateTaskItemRequest("Tarea inbox", seed.ActivityTypeId), seed.PlatformUserId, "Tester");
        Assert.True(created.IsOk, created.Error);

        var inbox = new WorkflowInboxService(ctx, tenantCtx, new NodeAssigneeResolver(ctx), engine);

        // El Asesor (candidato de Requerimiento) ve el paso; un extrano NO.
        var asesorSteps = await inbox.GetMyPendingStepsAsync(asesorId.UserId);
        var step = Assert.Single(asesorSteps);
        Assert.Equal("Requerimiento", step.NodeName);
        Assert.True(step.IsClaimable);
        Assert.False(step.IsMine);
        Assert.False(step.HasForm);
        Assert.False(step.IsGatewayAhead);
        Assert.Empty(await inbox.GetMyPendingStepsAsync(extranoId));

        // Reclamar: pasa a "mio".
        Assert.True((await inbox.ClaimStepAsync(step.StepId, asesorId.UserId)).IsOk);
        var mine = Assert.Single(await inbox.GetMyPendingStepsAsync(asesorId.UserId));
        Assert.True(mine.IsMine);
        Assert.Equal(asesorId.UserId, mine.AssignedToTenantUserId);

        // Completar Requerimiento -> avanza a Cotizacion (candidato: Aprobador).
        var completed = await inbox.CompletePendingStepAsync(mine.StepId, asesorId.UserId, null, null);
        Assert.True(completed.IsOk, completed.Error);

        Assert.Empty(await inbox.GetMyPendingStepsAsync(asesorId.UserId)); // ya no tiene nada
        var aprobadorSteps = await inbox.GetMyPendingStepsAsync(aprobadorId.UserId);
        var cotStep = Assert.Single(aprobadorSteps);
        Assert.Equal("Cotizacion", cotStep.NodeName);
        // Cotizacion apunta a la compuerta: gateway adelante con opciones Aprobada/Rechazada.
        Assert.True(cotStep.IsGatewayAhead);
        Assert.Equal(new[] { "Aprobada", "Rechazada" }, cotStep.ApprovalOptions.OrderBy(o => o, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Gateway_ApprovedRoutesToFacturacion_RejectedRestartsCotizacion()
    {
        var seed = await SeedTenantAsync("Inbox Gateway");

        // --- Camino Aprobada: Cotizacion (Aprobada) -> Facturacion ---
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
            var engine = BuildEngine(ctx, seed);
            var (asesor, aprobador, _) = await SeedCargosAsync(ctx, tenantCtx, seed);
            var definition = await PublishFlowAsync(engine, ctx, seed);
            await AttachPoliciesAsync(ctx, tenantCtx, definition, asesor.CargoId, aprobador.CargoId);
            var inbox = new WorkflowInboxService(ctx, tenantCtx, new NodeAssigneeResolver(ctx), engine);

            var run = (await engine.StartInstanceAsync(definition.Id)).Value!;
            var req = Assert.Single(await inbox.GetMyPendingStepsAsync(asesor.UserId));
            Assert.True((await inbox.CompletePendingStepAsync(req.StepId, asesor.UserId, null, null)).IsOk);
            var cot = Assert.Single(await inbox.GetMyPendingStepsAsync(aprobador.UserId));
            Assert.Equal("Cotizacion", cot.NodeName);
            // Completar Cotizacion con "Aprobada": el servicio resuelve tambien la compuerta que
            // queda detras (mismo approvalResult) y el flujo enruta a Facturacion.
            var cotResult = await inbox.CompletePendingStepAsync(cot.StepId, aprobador.UserId, "Aprobada", "ok");
            Assert.True(cotResult.IsOk, cotResult.Error);

            var current = await engine.GetCurrentStepsAsync(run.Id);
            Assert.Equal("Task_Fac", Assert.Single(current).BpmnElementId);
        }

        // --- Camino Rechazada: Cotizacion (Rechazada) -> reinicio en Cotizacion (ciclo 1) ---
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var tenantCtx = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
            var engine = BuildEngine(ctx, seed);
            var (asesor, aprobador, _) = await LoadCargosAsync(ctx, seed);
            var definition = await ctx.WorkflowDefinitions.AsNoTracking()
                .FirstAsync(d => d.ProcessCode == "INB-01" && d.IsPublished);
            var inbox = new WorkflowInboxService(ctx, tenantCtx, new NodeAssigneeResolver(ctx), engine);

            var run = (await engine.StartInstanceAsync(definition.Id)).Value!;
            var req = Assert.Single(await inbox.GetMyPendingStepsAsync(asesor.UserId));
            Assert.True((await inbox.CompletePendingStepAsync(req.StepId, asesor.UserId, null, null)).IsOk);
            var cot = Assert.Single(await inbox.GetMyPendingStepsAsync(aprobador.UserId));
            var rejected = await inbox.CompletePendingStepAsync(cot.StepId, aprobador.UserId, "Rechazada", "faltan datos");
            Assert.True(rejected.IsOk, rejected.Error);

            var current = Assert.Single(await engine.GetCurrentStepsAsync(run.Id));
            Assert.Equal("Task_Cot", current.BpmnElementId);
            Assert.Equal(1, current.CycleIndex);
            Assert.True(current.IsCycleStart);
        }
    }

    [Fact]
    public async Task Inbox_IsTenantIsolated()
    {
        var seedA = await SeedTenantAsync("Inbox A");
        var seedB = await SeedTenantAsync("Inbox B");

        Guid asesorAUserId;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var tenantCtxA = new TestTenantContext(seedA.TenantId, seedA.PlatformUserId);
            var engineA = BuildEngine(ctxA, seedA);
            var (asesor, aprobador, _) = await SeedCargosAsync(ctxA, tenantCtxA, seedA);
            asesorAUserId = asesor.UserId;
            var definition = await PublishFlowAsync(engineA, ctxA, seedA);
            await AttachPoliciesAsync(ctxA, tenantCtxA, definition, asesor.CargoId, aprobador.CargoId);
            var taskService = BuildTaskService(ctxA, seedA, engineA);
            Assert.True((await taskService.CreateAsync(
                new CreateTaskItemRequest("Privado de A", seedA.ActivityTypeId), seedA.PlatformUserId, "A")).IsOk);
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            // El filtro global oculta pasos/instancias de A: aunque preguntemos por el usuario de A,
            // el contexto B no ve nada (y el usuario de A no existe en B).
            var tenantCtxB = new TestTenantContext(seedB.TenantId, seedB.PlatformUserId);
            var inboxB = new WorkflowInboxService(ctxB, tenantCtxB, new NodeAssigneeResolver(ctxB), BuildEngine(ctxB, seedB));
            Assert.Empty(await inboxB.GetMyPendingStepsAsync(asesorAUserId));
            Assert.Empty(await ctxB.WorkflowStepHistories.ToListAsync());
        }
    }

    // ---- Helpers ----

    private async Task<WorkflowDefinitionDto> PublishFlowAsync(
        IWorkflowEngine engine, EcorexDbContext ctx, SeedData seed)
    {
        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("INB-01", "Flujo inbox", InboxXml))).Value!;
        var restartEnd = imported.Nodes.Single(n => n.BpmnElementId == "End_Re");
        var restartTarget = imported.Nodes.Single(n => n.BpmnElementId == "Task_Cot");
        Assert.True((await engine.SetRestartTargetAsync(restartEnd.Id, restartTarget.Id)).IsOk);
        Assert.True((await engine.PublishAsync(imported.Id)).IsOk);

        var activityType = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        activityType.WorkflowDefinitionId = imported.Id;
        await ctx.SaveChangesAsync();
        return imported;
    }

    private static async Task AttachPoliciesAsync(
        EcorexDbContext ctx, ITenantContext tenantCtx, WorkflowDefinitionDto definition,
        Guid asesorCargoId, Guid aprobadorCargoId)
    {
        var policySvc = new WorkflowNodePolicyService(ctx, tenantCtx);
        var reqNode = definition.Nodes.Single(n => n.BpmnElementId == "Task_Req");
        var cotNode = definition.Nodes.Single(n => n.BpmnElementId == "Task_Cot");
        Assert.True((await policySvc.AddNodePolicyAsync(reqNode.Id, asesorCargoId)).IsOk);
        Assert.True((await policySvc.AddNodePolicyAsync(cotNode.Id, aprobadorCargoId)).IsOk);
    }

    /// <summary>Crea 2 cargos (Asesor, Aprobador) con funcionarios ligados a 2 usuarios + un extrano.</summary>
    private async Task<(CargoSeed Asesor, CargoSeed Aprobador, Guid ExtranoUserId)> SeedCargosAsync(
        EcorexDbContext ctx, ITenantContext tenantCtx, SeedData seed)
    {
        var asesorUser = await AddTenantUserAsync(ctx, seed.TenantId, "asesor");
        var aprobadorUser = await AddTenantUserAsync(ctx, seed.TenantId, "aprobador");
        var extranoUser = await AddTenantUserAsync(ctx, seed.TenantId, "extrano");

        var org = new OrgUnitService(ctx, tenantCtx);
        var asesorDep = (await org.CreateAsync(new SaveOrgUnitRequest("Comercial", Classifier: OrgUnitClassifier.Dependencia))).Value!;
        var asesorCargo = (await org.CreateAsync(new SaveOrgUnitRequest("Asesor Comercial", ParentId: asesorDep.Id, Classifier: OrgUnitClassifier.Cargo))).Value!;
        Assert.True((await org.CreateAsync(new SaveOrgUnitRequest("Asesor Persona", ParentId: asesorCargo.Id, Classifier: OrgUnitClassifier.Funcionario, TenantUserId: asesorUser))).IsOk);

        var aprobadorDep = (await org.CreateAsync(new SaveOrgUnitRequest("Finanzas", Classifier: OrgUnitClassifier.Dependencia))).Value!;
        var aprobadorCargo = (await org.CreateAsync(new SaveOrgUnitRequest("Aprobador", ParentId: aprobadorDep.Id, Classifier: OrgUnitClassifier.Cargo))).Value!;
        Assert.True((await org.CreateAsync(new SaveOrgUnitRequest("Aprobador Persona", ParentId: aprobadorCargo.Id, Classifier: OrgUnitClassifier.Funcionario, TenantUserId: aprobadorUser))).IsOk);

        return (new CargoSeed(asesorCargo.Id, asesorUser), new CargoSeed(aprobadorCargo.Id, aprobadorUser), extranoUser);
    }

    /// <summary>Re-lee los cargos ya sembrados (por nombre) en un contexto nuevo.</summary>
    private static async Task<(CargoSeed Asesor, CargoSeed Aprobador, Guid ExtranoUserId)> LoadCargosAsync(
        EcorexDbContext ctx, SeedData seed)
    {
        var asesorCargo = await ctx.OrgUnits.AsNoTracking().SingleAsync(u => u.Name == "Asesor Comercial");
        var aprobadorCargo = await ctx.OrgUnits.AsNoTracking().SingleAsync(u => u.Name == "Aprobador");
        var asesorUser = await ctx.OrgUnits.AsNoTracking().Where(u => u.Name == "Asesor Persona").Select(u => u.TenantUserId!.Value).SingleAsync();
        var aprobadorUser = await ctx.OrgUnits.AsNoTracking().Where(u => u.Name == "Aprobador Persona").Select(u => u.TenantUserId!.Value).SingleAsync();
        return (new CargoSeed(asesorCargo.Id, asesorUser), new CargoSeed(aprobadorCargo.Id, aprobadorUser), Guid.Empty);
    }

    private static async Task<Guid> AddTenantUserAsync(EcorexDbContext ctx, Guid tenantId, string tag)
    {
        var platformUser = new PlatformUser
        {
            Email = $"{tag}-{Guid.NewGuid():N}@inbox.test",
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
        return tenantUser.Id;
    }

    private sealed record CargoSeed(Guid CargoId, Guid UserId);

    private static WorkflowEngine BuildEngine(EcorexDbContext ctx, SeedData seed)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId),
            new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());

    private static TaskItemService BuildTaskService(EcorexDbContext ctx, SeedData seed, IWorkflowEngine engine)
    {
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new TaskItemService(ctx, tenantContext, new SequenceService(ctx, tenantContext), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
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
        Guid activityTypeId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser
            {
                Email = $"owner-{tenantId:N}@inbox.test",
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
            var activityType = new ActivityType
            {
                TenantId = tenantId,
                Category = "General",
                Name = "Con flujo inbox"
            };
            ctx.ActivityTypes.Add(activityType);
            await ctx.SaveChangesAsync();
            tenantUserId = tenantUser.Id;
            platformUserId = platformUser.Id;
            activityTypeId = activityType.Id;
        }
        return new SeedData(tenantId, tenantUserId, platformUserId, activityTypeId);
    }

    private sealed record SeedData(Guid TenantId, Guid TenantUserId, Guid PlatformUserId, Guid ActivityTypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class WorkflowInboxTests_Postgres
    : WorkflowInboxTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public WorkflowInboxTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class WorkflowInboxTests_SqlServer
    : WorkflowInboxTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public WorkflowInboxTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
