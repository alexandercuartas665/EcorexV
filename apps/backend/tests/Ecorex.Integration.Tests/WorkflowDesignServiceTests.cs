using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del editor de flujos (IWorkflowDesignService, ADR-0022) en matriz
/// dual PostgreSQL / SQL Server. Cubre las reglas de la spec: PauseAsync bloquea
/// StartInstanceAsync (y Resume lo rehabilita), DeleteNode protege el startEvent, las
/// mutaciones del grafo SOLO aplican a borradores (EnsureDraft reusa el versionado del
/// motor y se reutiliza en llamadas repetidas), el flujo del editor (crear borrador ->
/// agregar tarea -> conectar -> renombrar) persiste y REGENERA un BpmnXml importable con
/// las coordenadas, el export/import JSON crea una version borrador nueva y el indice
/// calcula metricas reales (Running / ejecuciones del mes / % exito).
/// </summary>
public abstract class WorkflowDesignServiceTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected WorkflowDesignServiceTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- (1) Pausa: StartInstance rechaza; Resume rehabilita ----

    [Fact]
    public async Task Pause_BlocksStartInstance_AndResumeAllowsAgain()
    {
        var tenantId = await SeedTenantAsync("Design Pausa");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (engine, design) = BuildServices(ctx, tenantId);

        var definition = (await engine.ImportBpmnAsync(new ImportBpmnRequest("PAU-01", "Flujo pausable", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(definition.Id)).IsOk);

        Assert.True((await design.PauseAsync(definition.Id)).IsOk);
        var canvas = await design.GetCanvasAsync(definition.Id);
        Assert.Equal(WorkflowDesignService.EstadoPausado, canvas!.Estado);

        // Pausada: el motor rechaza instancias nuevas (integracion real engine <-> editor).
        var blocked = await engine.StartInstanceAsync(definition.Id);
        Assert.Equal(WorkflowEngineStatus.Invalid, blocked.Status);
        Assert.Contains("pausado", blocked.Error!, StringComparison.OrdinalIgnoreCase);

        Assert.True((await design.ResumeAsync(definition.Id)).IsOk);
        var started = await engine.StartInstanceAsync(definition.Id);
        Assert.True(started.IsOk, started.Error);

        // Pausar un borrador no tiene sentido (no ejecuta instancias): Invalid.
        var draft = (await engine.ImportBpmnAsync(new ImportBpmnRequest("PAU-02", "Borrador", LinearXml))).Value!;
        Assert.Equal(WorkflowEngineStatus.Invalid, (await design.PauseAsync(draft.Id)).Status);
    }

    // ---- (2) DeleteNode protege el startEvent ----

    [Fact]
    public async Task DeleteNode_StartEventIsProtected_OtherNodesCascadeEdges()
    {
        var tenantId = await SeedTenantAsync("Design Delete");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (_, design) = BuildServices(ctx, tenantId);

        var canvas = (await design.CreateDraftAsync("Flujo con borrado", "Operaciones")).Value!;
        var start = canvas.Nodes.Single(n => n.NodeType == WorkflowNodeType.StartEvent);
        var end = canvas.Nodes.Single(n => n.NodeType == WorkflowNodeType.EndEvent);

        // El startEvent es unico por definicion (regla del motor): nunca se borra.
        var protectedResult = await design.DeleteNodeAsync(start.Id);
        Assert.Equal(WorkflowEngineStatus.Invalid, protectedResult.Status);
        Assert.True(await ctx.WorkflowNodes.AnyAsync(n => n.Id == start.Id));

        // Un nodo normal se borra CON sus aristas.
        var task = (await design.AddNodeAsync(canvas.DefinitionId, WorkflowNodeType.Task, 240, 140)).Value!;
        Assert.True((await design.ConnectAsync(start.Id, task.Id)).IsOk);
        Assert.True((await design.ConnectAsync(task.Id, end.Id)).IsOk);
        Assert.True((await design.DeleteNodeAsync(task.Id)).IsOk);
        Assert.False(await ctx.WorkflowNodes.AnyAsync(n => n.Id == task.Id));
        Assert.False(await ctx.WorkflowEdges.AnyAsync(e => e.SourceNodeId == task.Id || e.TargetNodeId == task.Id));
    }

    // ---- (3) Mutaciones solo en borrador + EnsureDraft reusa el versionado ----

    [Fact]
    public async Task GraphMutations_RequireDraft_AndEnsureDraftReusesEngineVersioning()
    {
        var tenantId = await SeedTenantAsync("Design Versionado");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (engine, design) = BuildServices(ctx, tenantId);

        var v1 = (await engine.ImportBpmnAsync(new ImportBpmnRequest("VER-10", "Proceso publicado", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(v1.Id)).IsOk);

        // Publicada: el grafo NO se muta.
        var denied = await design.AddNodeAsync(v1.Id, WorkflowNodeType.Task, 100, 100);
        Assert.Equal(WorkflowEngineStatus.Invalid, denied.Status);

        // EnsureDraft crea la version borrador siguiente por el camino del motor (max+1).
        var draft = (await design.EnsureDraftAsync(v1.Id)).Value!;
        Assert.Equal(2, draft.Version);
        Assert.False(draft.IsPublished);
        Assert.True(draft.IsEditable);
        Assert.Equal(v1.Nodes.Count, draft.Nodes.Count);

        // Repetir EnsureDraft NO crea version 3: reutiliza el borrador existente.
        var again = (await design.EnsureDraftAsync(v1.Id)).Value!;
        Assert.Equal(draft.DefinitionId, again.DefinitionId);
        Assert.Equal(2, await ctx.WorkflowDefinitions.CountAsync(d => d.ProcessCode == "VER-10"));

        // Sobre el borrador la mutacion aplica y el XML regenerado sigue siendo valido.
        var added = await design.AddNodeAsync(draft.DefinitionId, WorkflowNodeType.Task, 300, 200);
        Assert.True(added.IsOk, added.Error);
        var stored = await ctx.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == draft.DefinitionId);
        var parsed = BpmnProcessParser.Parse(stored.BpmnXml);
        Assert.True(parsed.IsValid, string.Join(" | ", parsed.Errors));
        Assert.Contains(parsed.Nodes, n => n.BpmnElementId == added.Value!.BpmnElementId && n.X == 300 && n.Y == 200);
    }

    // ---- (4) Flujo del editor: agregar + conectar + renombrar + mover persiste ----

    [Fact]
    public async Task EditorFlow_AddConnectRenameMove_PersistsAndRegeneratesPortableXml()
    {
        var tenantId = await SeedTenantAsync("Design Editor");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (engine, design) = BuildServices(ctx, tenantId);

        var canvas = (await design.CreateDraftAsync("Flujo del editor", "Comercial")).Value!;
        var start = canvas.Nodes.Single(n => n.NodeType == WorkflowNodeType.StartEvent);
        var end = canvas.Nodes.Single(n => n.NodeType == WorkflowNodeType.EndEvent);
        Assert.Single(canvas.Edges); // borrador minimo: Inicio -> Fin

        var task = (await design.AddNodeAsync(canvas.DefinitionId, WorkflowNodeType.Task, 240, 140)).Value!;
        Assert.True((await design.ConnectAsync(start.Id, task.Id)).IsOk);
        Assert.True((await design.ConnectAsync(task.Id, end.Id)).IsOk);
        // Duplicado y self-loop: rechazados.
        Assert.Equal(WorkflowEngineStatus.Invalid, (await design.ConnectAsync(start.Id, task.Id)).Status);
        Assert.Equal(WorkflowEngineStatus.Invalid, (await design.ConnectAsync(task.Id, task.Id)).Status);

        Assert.True((await design.RenameNodeAsync(task.Id, "Aprobar cotizacion")).IsOk);
        Assert.True((await design.MoveNodeAsync(task.Id, 410, 260)).IsOk);

        // Releer el canvas: todo persistio (nombre, layout, aristas).
        var reloaded = (await design.GetCanvasAsync(canvas.DefinitionId))!;
        var persisted = reloaded.Nodes.Single(n => n.Id == task.Id);
        Assert.Equal("Aprobar cotizacion", persisted.Name);
        Assert.Equal((410, 260), (persisted.X, persisted.Y));
        Assert.Equal(3, reloaded.Edges.Count);

        // El XML regenerado es importable por el MOTOR y reproduce el grafo (round-trip real).
        var stored = await ctx.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == canvas.DefinitionId);
        var reimported = await engine.ImportBpmnAsync(new ImportBpmnRequest("RT-01", "Round trip", stored.BpmnXml));
        Assert.True(reimported.IsOk, reimported.Error);
        Assert.Equal(3, reimported.Value!.Nodes.Count);
        Assert.Equal(3, reimported.Value.Edges.Count);
        var reimportedTask = reimported.Value.Nodes.Single(n => n.NodeType == WorkflowNodeType.Task);
        Assert.Equal("Aprobar cotizacion", reimportedTask.Name);
    }

    // ---- (5) Export / import JSON (formato del prototipo) ----

    [Fact]
    public async Task ExportJson_ThenImportJson_CreatesNextDraftVersionWithSameGraph()
    {
        var tenantId = await SeedTenantAsync("Design JSON");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (_, design) = BuildServices(ctx, tenantId);

        var canvas = (await design.CreateDraftAsync("Flujo exportable", "Tecnologia")).Value!;
        var json = await design.ExportJsonAsync(canvas.DefinitionId);
        Assert.NotNull(json);
        Assert.Contains("\"nodos\"", json);
        Assert.Contains("\"conexiones\"", json);

        var imported = (await design.ImportJsonAsync(json!)).Value!;
        Assert.Equal(canvas.ProcessCode, imported.ProcessCode);
        Assert.Equal(canvas.Version + 1, imported.Version);
        Assert.Equal(WorkflowDesignService.EstadoBorrador, imported.Estado);
        Assert.Equal(canvas.Nodes.Count, imported.Nodes.Count);
        Assert.Equal(canvas.Edges.Count, imported.Edges.Count);
        // El layout viaja en el JSON y regresa identico.
        foreach (var node in canvas.Nodes)
        {
            var twin = Assert.Single(imported.Nodes, n => n.BpmnElementId == node.BpmnElementId);
            Assert.Equal((node.X, node.Y), (twin.X, twin.Y));
        }
    }

    // ---- (5b) SaveBpmnAsync: resync in-place del grafo desde el XML de bpmn-js (ADR-0034) ----

    [Fact]
    public async Task SaveBpmn_ResyncsGraphInPlace_PreservesParametrizationByElementId()
    {
        var tenantId = await SeedTenantAsync("Design SaveBpmn");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (engine, design) = BuildServices(ctx, tenantId);

        // Borrador minimo Inicio -> Fin (Start_1, End_1) mas una tarea con config propia.
        var canvas = (await design.CreateDraftAsync("Flujo bpmn-js", "Operaciones")).Value!;
        var start = canvas.Nodes.Single(n => n.NodeType == WorkflowNodeType.StartEvent);
        var end = canvas.Nodes.Single(n => n.NodeType == WorkflowNodeType.EndEvent);
        var task = (await design.AddNodeAsync(canvas.DefinitionId, WorkflowNodeType.Task, 240, 150)).Value!;
        Assert.True((await design.SetNodeConfigAsync(task.Id, allowsAssignment: false, restartNodeId: start.Id)).IsOk);

        // El XML que "produce bpmn-js": conserva Start/End/Task por su BpmnElementId, renombra
        // la tarea y agrega un gateway nuevo. La parametrizacion NO viaja aqui (va por tablas).
        var startId = start.BpmnElementId;
        var endId = end.BpmnElementId;
        var taskId = task.BpmnElementId;
        var xml = $"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="http://ecorex.local/bpmn">
              <bpmn:process id="P_Save">
                <bpmn:startEvent id="{startId}" name="Inicio" />
                <bpmn:task id="{taskId}" name="Aprobar (bpmn-js)" />
                <bpmn:exclusiveGateway id="Gateway_New" name="Decision" />
                <bpmn:endEvent id="{endId}" name="Fin" />
                <bpmn:sequenceFlow id="S1" sourceRef="{startId}" targetRef="{taskId}" />
                <bpmn:sequenceFlow id="S2" sourceRef="{taskId}" targetRef="Gateway_New" />
                <bpmn:sequenceFlow id="S3" sourceRef="Gateway_New" targetRef="{endId}" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var saved = await design.SaveBpmnAsync(canvas.DefinitionId, xml);
        Assert.True(saved.IsOk, saved.Error);
        // Borrador: se re-sincroniza IN PLACE (misma definicion, NO version nueva).
        Assert.Equal(canvas.DefinitionId, saved.Value!.DefinitionId);

        var reloaded = (await design.GetCanvasAsync(canvas.DefinitionId))!;
        Assert.Equal(4, reloaded.Nodes.Count); // start + task + gateway + end
        Assert.Equal(3, reloaded.Edges.Count);

        // La tarea CONSERVO su fila (mismo Id de BD) con su config, y tomo el nombre del XML.
        var reloadedTask = reloaded.Nodes.Single(n => n.BpmnElementId == taskId);
        Assert.Equal(task.Id, reloadedTask.Id);
        Assert.Equal("Aprobar (bpmn-js)", reloadedTask.Name);
        Assert.False(reloadedTask.AllowsAssignment);
        Assert.Equal(start.Id, reloadedTask.RestartNodeId);
        // El gateway nuevo se materializo.
        Assert.Contains(reloaded.Nodes, n => n.BpmnElementId == "Gateway_New" && n.NodeType == WorkflowNodeType.ExclusiveGateway);

        // El XML guardado es el de bpmn-js TAL CUAL (portabilidad, ADR-0014) e importable.
        var stored = await ctx.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == canvas.DefinitionId);
        Assert.Equal(xml, stored.BpmnXml);
        Assert.True((await engine.ImportBpmnAsync(new ImportBpmnRequest("SV-RT", "Round trip", stored.BpmnXml!))).IsOk);
    }

    [Fact]
    public async Task SaveBpmn_OnPublished_DerivesDraftAndReturnsItsId()
    {
        var tenantId = await SeedTenantAsync("Design SaveBpmn Publicada");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (engine, design) = BuildServices(ctx, tenantId);

        var definition = (await engine.ImportBpmnAsync(new ImportBpmnRequest("SVP-01", "Publicado", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(definition.Id)).IsOk);

        // Guardar sobre una publicada deriva/reusa el borrador y devuelve su Id (no el publicado).
        var saved = await design.SaveBpmnAsync(definition.Id, LinearXml);
        Assert.True(saved.IsOk, saved.Error);
        Assert.NotEqual(definition.Id, saved.Value!.DefinitionId);
        Assert.Equal(WorkflowDesignService.EstadoBorrador, saved.Value.Estado);
        // La publicada sigue intacta.
        var published = await design.GetCanvasAsync(definition.Id);
        Assert.True(published!.IsPublished);
    }

    // ---- (6) Indice: metricas reales ----

    [Fact]
    public async Task Index_ComputesRealMetrics_RunningMonthAndSuccessRate()
    {
        var tenantId = await SeedTenantAsync("Design Indice");
        await using var ctx = _fixture.CreateContext(tenantId);
        var (engine, design) = BuildServices(ctx, tenantId);

        var definition = (await engine.ImportBpmnAsync(new ImportBpmnRequest("IDX-01", "Flujo con metricas", LinearXml))).Value!;
        Assert.True((await engine.PublishAsync(definition.Id)).IsOk);

        var instance = (await engine.StartInstanceAsync(definition.Id)).Value!;
        var running = await design.ListForIndexAsync();
        var card = Assert.Single(running.Cards, c => c.ProcessCode == "IDX-01");
        Assert.Equal(WorkflowDesignService.EstadoEnMarcha, card.Estado);
        Assert.Equal(1, card.RunningInstances);
        Assert.Equal(1, card.MonthExecutions);
        Assert.Equal(0, card.SuccessRate); // sin instancias terminadas todavia
        Assert.Equal(4, card.NodeCount);
        Assert.Equal(1, running.Kpis.ActiveInstances);

        // Completar la instancia: exito = Completed / terminadas = 100%.
        var stepA = Assert.Single(await engine.GetCurrentStepsAsync(instance.Id));
        Assert.True((await engine.CompleteStepAsync(instance.Id, stepA.Id, null)).IsOk);
        var stepB = Assert.Single(await engine.GetCurrentStepsAsync(instance.Id));
        Assert.True((await engine.CompleteStepAsync(instance.Id, stepB.Id, null)).IsOk);

        var finished = await design.ListForIndexAsync();
        card = Assert.Single(finished.Cards, c => c.ProcessCode == "IDX-01");
        Assert.Equal(0, card.RunningInstances);
        Assert.Equal(1, card.MonthExecutions);
        Assert.Equal(100, card.SuccessRate);
        Assert.Equal(0, finished.Kpis.ActiveInstances);
    }

    // ---- Helpers ----

    /// <summary>start -> Task_A -> Task_B -> end (misma sintetica de WorkflowEngineTests).</summary>
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

    private static (WorkflowEngine Engine, WorkflowDesignService Design) BuildServices(EcorexDbContext ctx, Guid tenantId)
    {
        var engine = new WorkflowEngine(ctx, new TestTenantContext(tenantId),
            new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());
        return (engine, new WorkflowDesignService(ctx, engine));
    }

    private async Task<Guid> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class WorkflowDesignServiceTests_Postgres
    : WorkflowDesignServiceTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public WorkflowDesignServiceTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class WorkflowDesignServiceTests_SqlServer
    : WorkflowDesignServiceTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public WorkflowDesignServiceTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
