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
/// Tests de integracion del WorkflowEngine (FASE 4, ADR-0014) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// import del fixture BPMN real del vault (00001) con conteos y versionado, instancia
/// lineal completa con TaskItem que termina Done (auto-arranque desde CreateAsync),
/// compuerta exclusiva (Approved por una rama, Rejected reinicia ciclo via RestartNodeId),
/// rechazo con reactivacion append-only, tope de 50 iteraciones (Stuck) con un loop
/// autonomo sintetico y aislamiento cross-tenant de definiciones e instancias.
/// </summary>
public abstract class WorkflowEngineTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected WorkflowEngineTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- (1) Import del fixture BPMN real ----

    [Fact]
    public async Task Publish_FlowWithoutTaskNode_IsRejected()
    {
        // Ola C1: un flujo sin ningun paso humano (solo start -> end) no se puede publicar: si un
        // concepto lo usara, la actividad naceria enrolada y sin nada que atender.
        const string emptyXml = """
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="empty" targetNamespace="http://ecorex.local/bpmn">
              <bpmn:process id="P_Empty">
                <bpmn:startEvent id="Start_1" name="Inicio" />
                <bpmn:endEvent id="End_1" name="Fin" />
                <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="End_1" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var seed = await SeedTenantAsync("Workflow Publish Guard");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);

        var imported = (await engine.ImportBpmnAsync(new ImportBpmnRequest("EMPTY-01", "Flujo sin pasos", emptyXml))).Value!;
        var result = await engine.PublishAsync(imported.Id);

        Assert.Equal(WorkflowEngineStatus.Invalid, result.Status);
        Assert.Contains("paso", result.Error!, StringComparison.OrdinalIgnoreCase);
        // No se publico: sigue en borrador.
        Assert.False(await ctx.WorkflowDefinitions.AsNoTracking().AnyAsync(d => d.Id == imported.Id && d.IsPublished));

        // Un flujo lineal CON pasos (Task_A, Task_B) si publica.
        var linear = (await engine.ImportBpmnAsync(new ImportBpmnRequest("LIN-OK", "Flujo con pasos", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(linear.Id)).IsOk);
    }

    [Fact]
    public async Task ImportRealFixture00001_MaterializesExpectedGraph_AndKeepsXmlVerbatim()
    {
        var seed = await SeedTenantAsync("Workflow Fixture");
        var xml = await File.ReadAllTextAsync(FixturePath);

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);

        var imported = await engine.ImportBpmnAsync(new ImportBpmnRequest("PRO-00001", "Flujo compras 00001", xml));
        Assert.True(imported.IsOk, imported.Error);
        var definition = imported.Value!;

        // Conteos REALES del fixture del vault: 1 start + 8 tasks + 3 gateways + 2 endEvents
        // = 14 nodos ejecutables y 13 sequenceFlows (las anotaciones de texto se ignoran).
        Assert.Equal(14, definition.Nodes.Count);
        Assert.Equal(13, definition.Edges.Count);
        Assert.Equal(1, definition.Nodes.Count(n => n.NodeType == WorkflowNodeType.StartEvent));
        Assert.Equal(8, definition.Nodes.Count(n => n.NodeType == WorkflowNodeType.Task));
        Assert.Equal(3, definition.Nodes.Count(n => n.NodeType == WorkflowNodeType.ExclusiveGateway));
        Assert.Equal(2, definition.Nodes.Count(n => n.NodeType == WorkflowNodeType.EndEvent));
        Assert.Equal(1, definition.Version);
        Assert.False(definition.IsPublished);

        // El XML se guarda TAL CUAL (portabilidad bpmn.io: el motor no lo toca).
        var stored = await ctx.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == definition.Id);
        Assert.Equal(xml, stored.BpmnXml);
    }

    [Fact]
    public async Task ReimportSameProcessCode_CreatesNewUnpublishedVersion_AndPublishIsExclusive()
    {
        var seed = await SeedTenantAsync("Workflow Versiones");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);

        var v1 = (await engine.ImportBpmnAsync(new ImportBpmnRequest("VER-01", "Proceso v1", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(v1.Id)).IsOk);

        // Reimportar el mismo ProcessCode: version max+1, NO publicada; la v1 sigue publicada.
        var v2 = (await engine.ImportBpmnAsync(new ImportBpmnRequest("VER-01", "Proceso v2", LinearXml))).Value!;
        Assert.Equal(2, v2.Version);
        Assert.False(v2.IsPublished);
        Assert.True(await ctx.WorkflowDefinitions.AnyAsync(d => d.Id == v1.Id && d.IsPublished));

        // Publicar v2 despublica v1: solo una version publicada por ProcessCode.
        Assert.True((await engine.PublishAsync(v2.Id)).IsOk);
        Assert.False(await ctx.WorkflowDefinitions.AnyAsync(d => d.Id == v1.Id && d.IsPublished));
        Assert.True(await ctx.WorkflowDefinitions.AnyAsync(d => d.Id == v2.Id && d.IsPublished));
    }

    // ---- (2) Instancia lineal completa con TaskItem ----

    [Fact]
    public async Task LinearFlow_TaskCreatedWithPublishedDefinition_AutoStartsAndEndsDone()
    {
        var seed = await SeedTenantAsync("Workflow Lineal");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);

        // Definicion lineal publicada y anclada al ActivityType (como hara el catalogo).
        var definition = (await engine.ImportBpmnAsync(new ImportBpmnRequest("LIN-01", "Flujo lineal", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(definition.Id)).IsOk);
        var activityType = await ctx.ActivityTypes.SingleAsync(t => t.Id == seed.ActivityTypeId);
        activityType.WorkflowDefinitionId = definition.Id;
        await ctx.SaveChangesAsync();

        // Crear la tarea via TaskItemService: el flujo arranca en la MISMA transaccion.
        var service = BuildTaskService(ctx, seed, engine);
        var created = await service.CreateAsync(
            new CreateTaskItemRequest("Tarea con flujo", seed.ActivityTypeId), seed.PlatformUserId, "Tester");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        var instance = await ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.TaskItemId == taskId);
        Assert.Equal(WorkflowInstanceStatus.Running, instance.Status);

        // La tarea quedo enlazada y Active, con la actividad de inicio del flujo.
        var task = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId);
        Assert.Equal(instance.Id, task.WorkflowInstanceId);
        Assert.Equal(TaskItemStatus.Active, task.Status);
        Assert.True(await ctx.TaskItemActivities.AnyAsync(a => a.TaskItemId == taskId && a.Text.Contains("inicio el flujo")));

        // El startEvent se completo solo: current = Task A.
        var current = await engine.GetCurrentStepsAsync(instance.Id);
        var stepA = Assert.Single(current);
        Assert.Equal("Task_A", stepA.BpmnElementId);
        Assert.Equal(WorkflowStepStatus.Pending, stepA.Status);

        // Completar A -> current B; completar B -> endEvent: instancia Completed y tarea Done.
        Assert.True((await engine.CompleteStepAsync(instance.Id, stepA.Id, seed.TenantUserId)).IsOk);
        var stepB = Assert.Single(await engine.GetCurrentStepsAsync(instance.Id));
        Assert.Equal("Task_B", stepB.BpmnElementId);
        var finished = await engine.CompleteStepAsync(instance.Id, stepB.Id, seed.TenantUserId);
        Assert.True(finished.IsOk, finished.Error);
        Assert.Equal(WorkflowInstanceStatus.Completed, finished.Value!.Status);
        Assert.NotNull(finished.Value.CompletedAt);
        Assert.Empty(await engine.GetCurrentStepsAsync(instance.Id));

        task = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId);
        Assert.Equal(TaskItemStatus.Done, task.Status);
        Assert.True(await ctx.TaskItemActivities.AnyAsync(a => a.TaskItemId == taskId && a.Text == "flujo completado"));

        // Historial completo: start + A + B + end, todos Completed y ninguno current.
        var history = await ctx.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == instance.Id).ToListAsync();
        Assert.Equal(4, history.Count);
        Assert.All(history, s => Assert.Equal(WorkflowStepStatus.Completed, s.Status));
        Assert.All(history, s => Assert.False(s.IsCurrent));
    }

    // ---- (2b) Ola 2: el alta CONSUME el concepto (flujo + titulo/detalle auto) ----

    [Fact]
    public async Task ConceptFlow_TaskFromSubcategoria_AutoStartsFlow_AppliesAutoTitleAndDetail()
    {
        // Ola 2: una actividad-proceso (subcategoria con WorkflowDefinitionId publicada) arranca su
        // WorkflowInstance en la misma transaccion, aplica TituloAuto/DetalleAuto, y una subcategoria
        // SIN flujo no crea instancia.
        var seed = await SeedTenantAsync("Workflow Concepto");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);

        var definition = (await engine.ImportBpmnAsync(new ImportBpmnRequest("CON-01", "Flujo del concepto", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(definition.Id)).IsOk);

        var categoria = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-1", Nombre = "Comercial" };
        ctx.ActividadCategorias.Add(categoria);
        var proceso = new ActividadSubcategoria
        {
            TenantId = seed.TenantId,
            CategoriaId = categoria.Id,
            Codigo = "CAT-1-1",
            Nombre = "Cotizacion",
            WorkflowDefinitionId = definition.Id,
            TituloAuto = "Cotizacion para @cliente",
            DetalleAuto = "Detalle automatico del concepto"
        };
        var simple = new ActividadSubcategoria
        {
            TenantId = seed.TenantId,
            CategoriaId = categoria.Id,
            Codigo = "CAT-1-2",
            Nombre = "Nota simple"
        };
        ctx.ActividadSubcategorias.AddRange(proceso, simple);
        await ctx.SaveChangesAsync();

        var service = BuildTaskService(ctx, seed, engine);

        // (a) Actividad-proceso, sin titulo -> se usa TituloAuto con token @cliente; arranca el flujo.
        var procResult = await service.CreateAsync(
            new CreateTaskItemRequest("", ActivityTypeId: null, RequesterName: "ACME", SubcategoriaId: proceso.Id),
            seed.PlatformUserId, "Tester");
        Assert.True(procResult.IsOk, procResult.Error);
        var procTaskId = procResult.Value!.Item.Id;
        Assert.Equal("Cotizacion para ACME", procResult.Value.Item.Title);

        var instance = await ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.TaskItemId == procTaskId);
        Assert.Equal(WorkflowInstanceStatus.Running, instance.Status);
        var procTask = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == procTaskId);
        Assert.Equal(instance.Id, procTask.WorkflowInstanceId);
        Assert.Equal("Detalle automatico del concepto", procTask.Description);
        // Primer paso pendiente (visible en el tablero "mis pendientes" + detalle de la tarea, ADR-0038).
        var step = Assert.Single(await engine.GetCurrentStepsAsync(instance.Id));
        Assert.Equal(WorkflowStepStatus.Pending, step.Status);

        // (b) Actividad simple (subcategoria sin flujo): no crea instancia.
        var simpleResult = await service.CreateAsync(
            new CreateTaskItemRequest("Tarea simple", ActivityTypeId: null, SubcategoriaId: simple.Id),
            seed.PlatformUserId, "Tester");
        Assert.True(simpleResult.IsOk, simpleResult.Error);
        var simpleTask = await ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == simpleResult.Value!.Item.Id);
        Assert.Null(simpleTask.WorkflowInstanceId);
    }

    // ---- (3) Compuerta: Approved por una rama; Rejected reinicia via RestartNodeId ----

    [Fact]
    public async Task Gateway_ApprovedFollowsBranch_RejectedOpensNewCycleAtRestartNode()
    {
        var seed = await SeedTenantAsync("Workflow Gateway");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);
        var definition = await ImportGatewayDefinitionAsync(engine);

        // Camino Approved: la decision se captura EN Task_A (paso que entra a la compuerta). El
        // gateway se auto-resuelve en la MISMA cascada (ADR-0037): A(Approved) -> G -> B.
        var approvedRun = (await engine.StartInstanceAsync(definition.Id)).Value!;
        await CompleteSingleCurrentAsync(engine, approvedRun.Id, approval: "Approved"); // Task_A
        var afterApproved = Assert.Single(await engine.GetCurrentStepsAsync(approvedRun.Id));
        Assert.Equal("Task_B", afterApproved.BpmnElementId);
        Assert.Equal(0, afterApproved.CycleIndex);
        // El gateway NO quedo pendiente: es una fila Completed que heredo la decision.
        var gwStep = await ctx.WorkflowStepHistories.AsNoTracking()
            .Join(ctx.WorkflowNodes.AsNoTracking(), s => s.NodeId, n => n.Id, (s, n) => new { s, n })
            .Where(x => x.s.InstanceId == approvedRun.Id && x.n.NodeType == WorkflowNodeType.ExclusiveGateway)
            .Select(x => x.s).SingleAsync();
        Assert.Equal(WorkflowStepStatus.Completed, gwStep.Status);
        Assert.False(gwStep.IsCurrent);
        Assert.Equal("Approved", gwStep.ApprovalResult);

        // Camino Rejected: A(Rejected) -> G -> endEvent de reinicio -> ciclo nuevo en A.
        var rejectedRun = (await engine.StartInstanceAsync(definition.Id)).Value!;
        var result = await CompleteSingleCurrentAsync(engine, rejectedRun.Id, approval: "Rejected"); // Task_A
        Assert.Equal(WorkflowInstanceStatus.Running, result.Value!.Status);

        var restarted = Assert.Single(await engine.GetCurrentStepsAsync(rejectedRun.Id));
        Assert.Equal("Task_A", restarted.BpmnElementId);
        Assert.Equal(1, restarted.CycleIndex);
        Assert.True(restarted.IsCycleStart);
        var instance = await ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == rejectedRun.Id);
        Assert.Equal(1, instance.CurrentCycle);
    }

    [Fact]
    public async Task RejectStep_ReactivatesPreviousNode_AppendOnly()
    {
        var seed = await SeedTenantAsync("Workflow Rechazo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);
        var definition = await ImportGatewayDefinitionAsync(engine);

        var run = (await engine.StartInstanceAsync(definition.Id)).Value!;
        // A(Approved) enruta a Task_B (el gateway se auto-resuelve, ADR-0037): Task_B queda vigente.
        await CompleteSingleCurrentAsync(engine, run.Id, approval: "Approved"); // Task_A
        var taskB = Assert.Single(await engine.GetCurrentStepsAsync(run.Id));
        Assert.Equal("Task_B", taskB.BpmnElementId);

        // Rechazar Task_B: reactiva el paso anterior reactivable. Su origen es el gateway (no
        // reactivable como Task), asi que la reactivacion sube al ultimo Task Completed = Task_A.
        var rejected = await engine.RejectStepAsync(run.Id, taskB.Id, seed.TenantUserId, "faltan datos");
        Assert.True(rejected.IsOk, rejected.Error);
        var reactivated = Assert.Single(await engine.GetCurrentStepsAsync(run.Id));
        Assert.Equal("Task_A", reactivated.BpmnElementId);
        Assert.Equal(WorkflowStepStatus.Pending, reactivated.Status);

        // Append-only: el paso rechazado sigue en el historial con su comentario, y el
        // Task_A original completado tambien (la reactivacion NO lo reescribio).
        var history = await ctx.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == run.Id).ToListAsync();
        var rejectedRow = Assert.Single(history, s => s.Status == WorkflowStepStatus.Rejected);
        Assert.Equal(taskB.Id, rejectedRow.Id);
        Assert.Equal("faltan datos", rejectedRow.ApprovalComment);
        Assert.Equal(2, history.Count(s => s.NodeId == reactivated.NodeId)); // A completado + A reactivado
    }

    // ---- (4) Reinicios encadenados sin salida: Stuck al tope de 50 ----

    [Fact]
    public async Task AutonomousLoopWithoutExit_HitsIterationCap_AndMarksInstanceStuck()
    {
        var seed = await SeedTenantAsync("Workflow Stuck");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        // Hook de reglas que auto-completa todo Task: el loop A -> reinicio(A) nunca espera
        // a un humano y el avance en cascada consume las 50 iteraciones.
        var engine = BuildEngine(ctx, seed, new AutoCompleteRuleHook());

        var definition = (await engine.ImportBpmnAsync(new ImportBpmnRequest("LOOP-01", "Loop sin salida", LoopXml))).Value!;
        var taskA = definition.Nodes.Single(n => n.BpmnElementId == "Task_A");
        var restartEnd = definition.Nodes.Single(n => n.BpmnElementId == "End_Restart");
        Assert.True((await engine.SetRestartTargetAsync(restartEnd.Id, taskA.Id)).IsOk);

        var started = await engine.StartInstanceAsync(definition.Id);
        Assert.Equal(WorkflowEngineStatus.StuckDetected, started.Status);
        Assert.Equal(WorkflowInstanceStatus.Stuck, started.Value!.Status);

        var instance = await ctx.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == started.Value.Id);
        Assert.Equal(WorkflowInstanceStatus.Stuck, instance.Status);

        // Cada vuelta del loop es un ciclo nuevo (append-only, IsCycleStart) y el historial
        // conserva TODAS las filas de todos los ciclos.
        var history = await ctx.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == instance.Id).ToListAsync();
        Assert.True(instance.CurrentCycle >= 40, $"CurrentCycle={instance.CurrentCycle}");
        Assert.True(history.Count(s => s.IsCycleStart) >= 40);
        Assert.Equal(history.Max(s => s.CycleIndex), instance.CurrentCycle);
    }

    // ---- (5) Aislamiento multi-tenant ----

    [Fact]
    public async Task WorkflowDefinitionsAndInstances_AreIsolatedBetweenTenants()
    {
        var seedA = await SeedTenantAsync("Workflow Tenant A");
        var seedB = await SeedTenantAsync("Workflow Tenant B");

        Guid definitionId;
        Guid instanceId;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var engineA = BuildEngine(ctxA, seedA);
            var definition = (await engineA.ImportBpmnAsync(new ImportBpmnRequest("ISO-01", "Privado de A", LinearXml))).Value!;
            definitionId = definition.Id;
            instanceId = (await engineA.StartInstanceAsync(definition.Id)).Value!.Id;
        }

        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            // El filtro global oculta definiciones, nodos, instancias y pasos de A.
            Assert.Empty(await ctxB.WorkflowDefinitions.ToListAsync());
            Assert.Empty(await ctxB.WorkflowInstances.ToListAsync());
            Assert.Empty(await ctxB.WorkflowStepHistories.ToListAsync());

            var engineB = BuildEngine(ctxB, seedB);
            Assert.Empty(await engineB.GetCurrentStepsAsync(instanceId));
            var start = await engineB.StartInstanceAsync(definitionId);
            Assert.Equal(WorkflowEngineStatus.NotFound, start.Status);
            var publish = await engineB.PublishAsync(definitionId);
            Assert.Equal(WorkflowEngineStatus.NotFound, publish.Status);
        }
    }

    // ---- (6) Historial append-only tras un reinicio ----

    [Fact]
    public async Task StepHistory_IsAppendOnly_CycleZeroRowsSurviveRestart()
    {
        var seed = await SeedTenantAsync("Workflow AppendOnly");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var engine = BuildEngine(ctx, seed);
        var definition = await ImportGatewayDefinitionAsync(engine);

        var run = (await engine.StartInstanceAsync(definition.Id)).Value!;
        var beforeRestart = await ctx.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == run.Id).ToListAsync();

        // A(Rejected) -> gateway auto-resuelto (Rejected) -> End_Restart -> reinicio en A (ciclo 1).
        await CompleteSingleCurrentAsync(engine, run.Id, approval: "Rejected"); // Task_A

        var after = await ctx.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == run.Id).ToListAsync();

        // Todas las filas previas siguen existiendo, con su MISMO estado final (append-only).
        foreach (var old in beforeRestart)
        {
            var survivor = after.SingleOrDefault(s => s.Id == old.Id);
            Assert.NotNull(survivor);
            Assert.Equal(old.CycleIndex, survivor!.CycleIndex);
            Assert.Equal(old.NodeId, survivor.NodeId);
        }
        // El gateway del ciclo 0 quedo Completed con la decision heredada (no Pending-current).
        var gatewayNodeId = definition.Nodes.Single(n => n.BpmnElementId == "Gateway_G").Id;
        var gwRow = after.Single(s => s.CycleIndex == 0 && s.NodeId == gatewayNodeId);
        Assert.Equal(WorkflowStepStatus.Completed, gwRow.Status);
        Assert.Equal("Rejected", gwRow.ApprovalResult);
        Assert.False(gwRow.IsCurrent);
        // Y el reinicio agrego la fila nueva del ciclo 1.
        var cycleOne = Assert.Single(after, s => s.CycleIndex == 1);
        Assert.True(cycleOne.IsCycleStart);
        Assert.Equal(WorkflowStepStatus.Pending, cycleOne.Status);
    }

    // ---- Helpers y definiciones sinteticas ----

    private static string FixturePath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ejemplo-bpmn-flujo-00001.bpmn");

    /// <summary>start -> Task_A -> Task_B -> end.</summary>
    private const string LinearXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="lin" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Linear">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_A" name="Paso A" />
            <bpmn:task id="Task_B" name="Paso B" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_A" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_A" targetRef="Task_B" />
            <bpmn:sequenceFlow id="F3" sourceRef="Task_B" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// start -> Task_A -> Gateway_G; Approved -> Task_B -> end; Rejected -> End_Restart
    /// (endEvent cuyo RestartNodeId se configura hacia Task_A).
    /// </summary>
    private const string GatewayXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="gw" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Gateway">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_A" name="Preparar" />
            <bpmn:exclusiveGateway id="Gateway_G" name="Aprobacion" />
            <bpmn:task id="Task_B" name="Ejecutar" />
            <bpmn:endEvent id="End_1" name="Fin" />
            <bpmn:endEvent id="End_Restart" name="Rechazada: reinicia" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_A" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_A" targetRef="Gateway_G" />
            <bpmn:sequenceFlow id="F3" sourceRef="Gateway_G" targetRef="Task_B">
              <bpmn:conditionExpression>approval == 'Approved'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="F4" sourceRef="Gateway_G" targetRef="End_Restart">
              <bpmn:conditionExpression>approval == 'Rejected'</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:sequenceFlow id="F5" sourceRef="Task_B" targetRef="End_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>start -> Task_A -> End_Restart (reinicio hacia Task_A: loop sin salida).</summary>
    private const string LoopXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="loop" targetNamespace="http://ecorex.local/bpmn">
          <bpmn:process id="P_Loop">
            <bpmn:startEvent id="Start_1" name="Inicio" />
            <bpmn:task id="Task_A" name="Paso ciclico" />
            <bpmn:endEvent id="End_Restart" name="Reiniciar" />
            <bpmn:sequenceFlow id="F1" sourceRef="Start_1" targetRef="Task_A" />
            <bpmn:sequenceFlow id="F2" sourceRef="Task_A" targetRef="End_Restart" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private async Task<WorkflowDefinitionDto> ImportGatewayDefinitionAsync(IWorkflowEngine engine)
    {
        var imported = await engine.ImportBpmnAsync(new ImportBpmnRequest("GW-01", "Flujo con compuerta", GatewayXml));
        Assert.True(imported.IsOk, imported.Error);
        var definition = imported.Value!;
        var taskA = definition.Nodes.Single(n => n.BpmnElementId == "Task_A");
        var restartEnd = definition.Nodes.Single(n => n.BpmnElementId == "End_Restart");
        var configured = await engine.SetRestartTargetAsync(restartEnd.Id, taskA.Id);
        Assert.True(configured.IsOk, configured.Error);
        return definition;
    }

    /// <summary>Completa el UNICO paso current de la instancia (falla si hay 0 o varios).</summary>
    private static async Task<WorkflowResult<WorkflowInstanceDto>> CompleteSingleCurrentAsync(
        IWorkflowEngine engine, Guid instanceId, string? approval = null)
    {
        var step = Assert.Single(await engine.GetCurrentStepsAsync(instanceId));
        var result = await engine.CompleteStepAsync(instanceId, step.Id, null, approval);
        Assert.True(result.IsOk || result.Status == WorkflowEngineStatus.StuckDetected, result.Error);
        return result;
    }

    private static WorkflowEngine BuildEngine(EcorexDbContext ctx, SeedData seed, IWorkflowRuleHook? hook = null)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId),
            hook ?? new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());

    private static TaskItemService BuildTaskService(EcorexDbContext ctx, SeedData seed, IWorkflowEngine engine)
    {
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new TaskItemService(ctx, tenantContext, new SequenceService(ctx, tenantContext), engine, new NoOpEmailSender(), new NodeAssigneeResolver(ctx));
    }

    /// <summary>Hook de reglas de prueba: toda Task se resuelve sola (regla autonoma).</summary>
    private sealed class AutoCompleteRuleHook : IWorkflowRuleHook
    {
        public Task<RuleHookResult> OnNodeActivatedAsync(WorkflowRuleContext ctx, CancellationToken ct)
            => Task.FromResult(new RuleHookResult(RuleHookOutcome.AutoComplete));
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
                Email = $"user-{tenantId:N}@workflow.test",
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
                Name = "Con flujo"
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
public sealed class WorkflowEngineTests_Postgres
    : WorkflowEngineTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public WorkflowEngineTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class WorkflowEngineTests_SqlServer
    : WorkflowEngineTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public WorkflowEngineTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
