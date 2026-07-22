using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de los AGENTES DE IA EN NODOS (ola 1: modelo, asignacion y armado de
/// contexto; la ejecucion es la ola 2) en matriz dual PostgreSQL / SQL Server, reutilizando los
/// fixtures de TenantIsolation. Cubre:
/// (1) asignar / consultar / reemplazar / quitar el agente de un nodo, incluida la unicidad por
///     nodo (el reemplazo NO crea una segunda fila);
/// (2) que el contexto traiga las CUATRO partes con datos reales sembrados (nodo+formulario,
///     datos capturados antes, tarea+tercero, historial);
/// (3) aislamiento cross-tenant: un tenant no asigna un agente ajeno ni lee un contexto ajeno.
/// </summary>
public abstract class WorkflowNodeAgentTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected WorkflowNodeAgentTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- (1) Asignacion: asignar, consultar, reemplazar, quitar ----

    [Fact]
    public async Task NodeAgent_Assign_Read_Replace_Remove()
    {
        var seed = await SeedTenantAsync("NodeAgent CRUD");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var design = BuildDesignService(ctx, seed);

        var flow = await SeedFlowAsync(ctx, seed.TenantId);
        var agentA = await SeedAgentAsync(ctx, seed.TenantId, "Clasificador", isActive: true);
        var agentB = await SeedAgentAsync(ctx, seed.TenantId, "Redactor", isActive: false);

        // Sin asignar: null (el nodo lo atiende solo gente).
        Assert.Null(await design.GetNodeAgentAsync(flow.TaskNodeId));

        // Asignar en modo Proposes.
        var assigned = await design.SetNodeAgentAsync(flow.TaskNodeId, agentA, WorkflowAgentAutonomy.Proposes);
        Assert.True(assigned.IsOk, assigned.Error);
        Assert.Equal(agentA, assigned.Value!.AiAgentId);
        Assert.Equal(WorkflowAgentAutonomy.Proposes, assigned.Value.Autonomy);

        // Re-leer desde OTRA conexion: persistio de verdad.
        await using (var ctx2 = _fixture.CreateContext(seed.TenantId))
        {
            var read = await BuildDesignService(ctx2, seed).GetNodeAgentAsync(flow.TaskNodeId);
            Assert.NotNull(read);
            Assert.Equal("Clasificador", read!.AgentName);
            Assert.True(read.IsActive);
            Assert.Equal(WorkflowAgentAutonomy.Proposes, read.Autonomy);
        }

        // Reemplazar por otro agente y otra autonomia: UPSERT, no una segunda fila
        // (el indice unico por (TenantId, NodeId) es lo que se esta comprobando).
        var replaced = await design.SetNodeAgentAsync(flow.TaskNodeId, agentB, WorkflowAgentAutonomy.Autonomous);
        Assert.True(replaced.IsOk, replaced.Error);
        Assert.Equal(agentB, replaced.Value!.AiAgentId);
        Assert.Equal(WorkflowAgentAutonomy.Autonomous, replaced.Value.Autonomy);
        Assert.False(replaced.Value.IsActive);
        Assert.Equal(1, await ctx.WorkflowNodeAgents.CountAsync(x => x.NodeId == flow.TaskNodeId));

        // Un agente inexistente -> NotFound; un nodo que no es Task -> Invalid.
        Assert.Equal(WorkflowEngineStatus.NotFound,
            (await design.SetNodeAgentAsync(flow.TaskNodeId, Guid.CreateVersion7(), WorkflowAgentAutonomy.Proposes)).Status);
        Assert.Equal(WorkflowEngineStatus.Invalid,
            (await design.SetNodeAgentAsync(flow.StartNodeId, agentA, WorkflowAgentAutonomy.Proposes)).Status);

        // El catalogo lista los agentes del tenant con los activos primero.
        var catalog = await design.ListAgentCatalogAsync();
        Assert.Equal(2, catalog.Count);
        Assert.Equal("Clasificador", catalog[0].Name);

        // Quitar: el paso vuelve a ser 100% humano; quitar dos veces -> NotFound.
        Assert.True((await design.RemoveNodeAgentAsync(flow.TaskNodeId)).IsOk);
        Assert.Null(await design.GetNodeAgentAsync(flow.TaskNodeId));
        Assert.Equal(WorkflowEngineStatus.NotFound, (await design.RemoveNodeAgentAsync(flow.TaskNodeId)).Status);

        // Borrar el nodo cascada el vinculo; el AGENTE sobrevive (FK Restrict hacia AiAgent).
        Assert.True((await design.SetNodeAgentAsync(flow.TaskNodeId, agentA, WorkflowAgentAutonomy.Proposes)).IsOk);
        ctx.WorkflowDefinitions.Remove(await ctx.WorkflowDefinitions.FirstAsync(d => d.Id == flow.DefinitionId));
        await ctx.SaveChangesAsync();
        Assert.Equal(0, await ctx.WorkflowNodeAgents.CountAsync());
        Assert.Equal(2, await ctx.AiAgents.CountAsync());
    }

    // ---- (2) Contexto: las CUATRO partes con datos reales ----

    [Fact]
    public async Task Context_Brings_Node_PriorData_Task_And_History()
    {
        var seed = await SeedTenantAsync("NodeAgent Context");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var scenario = await SeedFullScenarioAsync(ctx, seed);

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var builder = new WorkflowAgentContextBuilder(ctx2);
        var result = await builder.BuildAsync(scenario.CurrentStepId);
        Assert.True(result.IsOk, result.Error);
        var context = result.Value!;

        Assert.Equal(scenario.InstanceId, context.InstanceId);
        Assert.Equal(scenario.CurrentStepId, context.StepId);

        // (a) Nodo actual + formulario del nodo con la definicion de sus campos.
        Assert.Equal(scenario.SecondNodeId, context.Node.NodeId);
        Assert.Equal("Aprobar solicitud", context.Node.Name);
        Assert.Equal("Revisar el monto contra el presupuesto", context.Node.Description);
        Assert.NotNull(context.Node.Form);
        Assert.Equal("FRM-APROB", context.Node.Form!.Code);
        Assert.False(context.Node.Form.FieldsTruncated);
        var decision = Assert.Single(context.Node.Form.Fields, f => f.FieldCode == "decision");
        Assert.Equal("Decision", decision.Label);
        Assert.True(decision.Required);
        Assert.Equal(FormControlType.Select, decision.ControlType);

        // El agente asignado al nodo viaja en el contexto (con su autonomia por nodo).
        Assert.NotNull(context.Assignment);
        Assert.Equal("Analista IA", context.Assignment!.AgentName);
        Assert.Equal(WorkflowAgentAutonomy.Proposes, context.Assignment.Autonomy);

        // (b) Datos capturados en pasos ANTERIORES (el formulario del paso 1, ya enviado).
        Assert.False(context.PriorData.Truncated);
        var prior = Assert.Single(context.PriorData.Forms);
        Assert.Equal("FRM-SOL", prior.FormCode);
        Assert.Equal(scenario.FirstNodeId, prior.NodeId);
        Assert.Equal("Radicar solicitud", prior.NodeName);
        Assert.False(prior.AnswersTruncated);
        var monto = Assert.Single(prior.Answers, a => a.FieldCode == "monto");
        Assert.Equal("1500000", monto.Value);
        Assert.Equal("Monto solicitado", monto.Label);
        // El formulario del paso ACTUAL (aun Pending) no cuenta como dato capturado.
        Assert.DoesNotContain(context.PriorData.Forms, f => f.FormCode == "FRM-APROB");

        // (c) Tarea que disparo el flujo + tercero/cliente derivado del concepto.
        Assert.NotNull(context.Task);
        Assert.Equal("Compra de portatiles", context.Task!.Title);
        Assert.Equal("Se requieren 5 equipos para el area comercial", context.Task.Description);
        Assert.Equal(TaskPriority.High, context.Task.Priority);
        Assert.NotNull(context.Task.Tercero);
        Assert.Equal("Cliente Uno SAS", context.Task.Tercero!.Nombre);
        Assert.Equal("900123456", context.Task.Tercero.IdValor);

        // (d) Historial de pasos, en orden cronologico, con aprobaciones y comentarios.
        Assert.Equal(2, context.History.TotalSteps);
        Assert.False(context.History.Truncated);
        Assert.Equal(2, context.History.Steps.Count);
        var first = context.History.Steps[0];
        Assert.Equal(scenario.FirstNodeId, first.NodeId);
        Assert.Equal(WorkflowStepStatus.Completed, first.Status);
        Assert.Equal("Approved", first.ApprovalResult);
        Assert.Equal("Presupuesto disponible", first.ApprovalComment);
        Assert.Equal(seed.Email, first.ExecutedByEmail);
        Assert.True(context.History.Steps[1].IsCurrent);

        // Un paso YA CERRADO no arma contexto: la decision ya se tomo, no se pagan tokens por ella.
        var closed = await builder.BuildAsync(scenario.FirstStepId);
        Assert.Equal(WorkflowEngineStatus.Invalid, closed.Status);
        // Un paso inexistente -> NotFound.
        Assert.Equal(WorkflowEngineStatus.NotFound, (await builder.BuildAsync(Guid.CreateVersion7())).Status);
    }

    // ---- (3) Aislamiento cross-tenant ----

    [Fact]
    public async Task NodeAgent_And_Context_AreTenantIsolated()
    {
        var seedA = await SeedTenantAsync("NodeAgent Iso A");
        var seedB = await SeedTenantAsync("NodeAgent Iso B");

        Guid agentA;
        Guid taskNodeA;
        Guid currentStepA;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var scenario = await SeedFullScenarioAsync(ctxA, seedA);
            taskNodeA = scenario.SecondNodeId;
            currentStepA = scenario.CurrentStepId;
            agentA = scenario.AgentId;
        }

        await using var ctxB = _fixture.CreateContext(seedB.TenantId);
        var flowB = await SeedFlowAsync(ctxB, seedB.TenantId);
        var designB = BuildDesignService(ctxB, seedB);

        // El filtro global oculta el vinculo, el nodo y el agente de A.
        Assert.Empty(await ctxB.WorkflowNodeAgents.ToListAsync());
        Assert.Null(await designB.GetNodeAgentAsync(taskNodeA));
        Assert.Empty(await designB.ListAgentCatalogAsync());

        // B NO puede asignar el agente de A a SU propio nodo: el agente ajeno no existe para B.
        var stolen = await designB.SetNodeAgentAsync(flowB.TaskNodeId, agentA, WorkflowAgentAutonomy.Autonomous);
        Assert.Equal(WorkflowEngineStatus.NotFound, stolen.Status);
        Assert.Equal(0, await ctxB.WorkflowNodeAgents.CountAsync());

        // B tampoco puede asignar a un nodo de A (aunque el agente fuera suyo).
        var ownAgentB = await SeedAgentAsync(ctxB, seedB.TenantId, "Agente B", isActive: true);
        Assert.Equal(WorkflowEngineStatus.NotFound,
            (await designB.SetNodeAgentAsync(taskNodeA, ownAgentB, WorkflowAgentAutonomy.Proposes)).Status);

        // B no lee el contexto de una instancia ajena: el paso de A no existe para B.
        var contextB = await new WorkflowAgentContextBuilder(ctxB).BuildAsync(currentStepA);
        Assert.Equal(WorkflowEngineStatus.NotFound, contextB.Status);
    }

    // ---- Helpers ----

    private static WorkflowDesignService BuildDesignService(EcorexDbContext ctx, SeedData seed)
        => new(ctx, new WorkflowEngine(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId),
            new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster()));

    /// <summary>Definicion minima con startEvent + un nodo Task.</summary>
    private static async Task<FlowSeed> SeedFlowAsync(IApplicationDbContext ctx, Guid tenantId)
    {
        var definition = new WorkflowDefinition
        {
            TenantId = tenantId,
            ProcessCode = $"AG-{Guid.NewGuid():N}"[..12],
            Name = "Flujo agente",
            BpmnXml = "<xml/>",
            Version = 1
        };
        ctx.WorkflowDefinitions.Add(definition);
        var start = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "Start_1",
            Name = "Inicio",
            NodeType = WorkflowNodeType.StartEvent
        };
        var task = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "Task_1",
            Name = "Paso",
            NodeType = WorkflowNodeType.Task,
            AllowsAssignment = true
        };
        ctx.WorkflowNodes.AddRange(start, task);
        await ctx.SaveChangesAsync();
        return new FlowSeed(definition.Id, start.Id, task.Id);
    }

    private static async Task<Guid> SeedAgentAsync(IApplicationDbContext ctx, Guid tenantId, string name, bool isActive)
    {
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = name,
            Role = "Prueba",
            SystemPrompt = "Eres un agente de prueba.",
            IsActive = isActive
        };
        ctx.AiAgents.Add(agent);
        await ctx.SaveChangesAsync();
        return agent.Id;
    }

    /// <summary>
    /// Escenario completo para el contexto: flujo de 2 pasos, formulario por paso (el del paso 1
    /// YA enviado, el del paso 2 pendiente), instancia con historial (paso 1 cerrado con
    /// aprobacion + paso 2 vigente), tarea con concepto que apunta a un unico tercero, y agente
    /// asignado al nodo del paso 2.
    /// </summary>
    private static async Task<ScenarioSeed> SeedFullScenarioAsync(EcorexDbContext ctx, SeedData seed)
    {
        var tenantId = seed.TenantId;

        // Flujo: Radicar solicitud -> Aprobar solicitud.
        var definition = new WorkflowDefinition
        {
            TenantId = tenantId,
            ProcessCode = $"AGF-{Guid.NewGuid():N}"[..12],
            Name = "Solicitud de compra",
            BpmnXml = "<xml/>",
            Version = 1,
            IsPublished = true
        };
        ctx.WorkflowDefinitions.Add(definition);
        var node1 = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "Task_Radicar",
            Name = "Radicar solicitud",
            NodeType = WorkflowNodeType.Task,
            StepNumber = 1
        };
        var node2 = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "Task_Aprobar",
            Name = "Aprobar solicitud",
            NodeType = WorkflowNodeType.Task,
            StepNumber = 2,
            Note = "Revisar el monto contra el presupuesto"
        };
        ctx.WorkflowNodes.AddRange(node1, node2);

        // Formularios: uno por paso, con sus campos.
        var formSolicitud = new FormDefinition
        {
            TenantId = tenantId,
            Code = "FRM-SOL",
            Title = "Solicitud",
            Status = FormStatus.Active
        };
        var formAprobacion = new FormDefinition
        {
            TenantId = tenantId,
            Code = "FRM-APROB",
            Title = "Aprobacion",
            Status = FormStatus.Active
        };
        ctx.FormDefinitions.AddRange(formSolicitud, formAprobacion);
        ctx.FormQuestions.AddRange(
            new FormQuestion
            {
                TenantId = tenantId,
                DefinitionId = formSolicitud.Id,
                FieldCode = "monto",
                Label = "Monto solicitado",
                ControlType = FormControlType.Number,
                SortOrder = 1
            },
            new FormQuestion
            {
                TenantId = tenantId,
                DefinitionId = formSolicitud.Id,
                FieldCode = "justificacion",
                Label = "Justificacion",
                ControlType = FormControlType.TextArea,
                SortOrder = 2
            },
            new FormQuestion
            {
                TenantId = tenantId,
                DefinitionId = formAprobacion.Id,
                FieldCode = "decision",
                Label = "Decision",
                ControlType = FormControlType.Select,
                Required = true,
                SortOrder = 1
            });
        ctx.WorkflowNodeForms.AddRange(
            new WorkflowNodeForm { TenantId = tenantId, NodeId = node1.Id, DefinitionId = formSolicitud.Id },
            new WorkflowNodeForm { TenantId = tenantId, NodeId = node2.Id, DefinitionId = formAprobacion.Id });

        // Concepto (000270) con UN unico tercero: de ahi sale el cliente del caso.
        var categoria = new ActividadCategoria { TenantId = tenantId, Codigo = "CAT1", Nombre = "Compras" };
        ctx.ActividadCategorias.Add(categoria);
        var subcategoria = new ActividadSubcategoria
        {
            TenantId = tenantId,
            CategoriaId = categoria.Id,
            Codigo = "SUB1",
            Nombre = "Compra de equipos",
            WorkflowDefinitionId = definition.Id
        };
        ctx.ActividadSubcategorias.Add(subcategoria);
        var tercero = new Tercero
        {
            TenantId = tenantId,
            Nombre = "Cliente Uno SAS",
            Tipo = TerceroTipo.Empresa,
            IdTipo = TerceroIdTipo.Nit,
            IdValor = "900123456",
            Email = "contacto@cliente-uno.test"
        };
        ctx.Terceros.Add(tercero);
        ctx.ActividadSubcategoriaTerceros.Add(new ActividadSubcategoriaTercero
        {
            TenantId = tenantId,
            SubcategoriaId = subcategoria.Id,
            TerceroId = tercero.Id
        });

        // Instancia + tarea que la disparo.
        var instance = new WorkflowInstance
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            Status = WorkflowInstanceStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        ctx.WorkflowInstances.Add(instance);
        var task = new TaskItem
        {
            TenantId = tenantId,
            Number = "T00042",
            Title = "Compra de portatiles",
            Description = "Se requieren 5 equipos para el area comercial",
            Priority = TaskPriority.High,
            Status = TaskItemStatus.InProgress,
            SubcategoriaId = subcategoria.Id,
            WorkflowInstanceId = instance.Id
        };
        ctx.TaskItems.Add(task);
        // OJO: el vinculo tarea<->instancia es circular (TaskItem.WorkflowInstanceId e
        // WorkflowInstance.TaskItemId). Si ambos lados se insertan en el MISMO SaveChanges, EF
        // no puede ordenar los comandos y falla con "circular dependency": el lado
        // instancia->tarea se cierra mas abajo, en el segundo SaveChanges.

        // Historial: paso 1 cerrado con aprobacion + paso 2 VIGENTE.
        var step1 = new WorkflowStepHistory
        {
            TenantId = tenantId,
            InstanceId = instance.Id,
            NodeId = node1.Id,
            CycleIndex = 0,
            IsCurrent = false,
            Status = WorkflowStepStatus.Completed,
            ExecutedByTenantUserId = seed.TenantUserId,
            ApprovalResult = "Approved",
            ApprovalComment = "Presupuesto disponible",
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        ctx.WorkflowStepHistories.Add(step1);
        await ctx.SaveChangesAsync();

        // Ya existen ambas filas: se cierra el lado instancia->tarea sin ciclo de insercion.
        instance.TaskItemId = task.Id;

        // Segundo SaveChanges para que CreatedAt del paso 2 sea POSTERIOR al del paso 1: el
        // orden cronologico del historial se apoya en esa columna.
        var step2 = new WorkflowStepHistory
        {
            TenantId = tenantId,
            InstanceId = instance.Id,
            NodeId = node2.Id,
            CycleIndex = 0,
            IsCurrent = true,
            Status = WorkflowStepStatus.Pending,
            AssignedToTenantUserId = seed.TenantUserId
        };
        ctx.WorkflowStepHistories.Add(step2);

        // Respuesta YA enviada del paso 1 (link Completed) + formulario del paso 2 pendiente.
        var response1 = new FormResponse
        {
            TenantId = tenantId,
            DefinitionId = formSolicitud.Id,
            Reference = task.Number,
            Status = FormResponseStatus.Submitted,
            Data = """{"monto":{"value":"1500000","type":"number"},"justificacion":{"value":"Renovacion de equipos","type":"text"}}""",
            SubmittedAt = DateTimeOffset.UtcNow.AddHours(-1),
            SubmittedByTenantUserId = seed.TenantUserId
        };
        var response2 = new FormResponse
        {
            TenantId = tenantId,
            DefinitionId = formAprobacion.Id,
            Reference = task.Number,
            Status = FormResponseStatus.Draft,
            Data = "{}"
        };
        ctx.FormResponses.AddRange(response1, response2);
        ctx.FormFlowLinks.AddRange(
            new FormFlowLink
            {
                TenantId = tenantId,
                FormResponseId = response1.Id,
                WorkflowInstanceId = instance.Id,
                WorkflowNodeId = node1.Id,
                Status = FormFlowLinkStatus.Completed
            },
            new FormFlowLink
            {
                TenantId = tenantId,
                FormResponseId = response2.Id,
                WorkflowInstanceId = instance.Id,
                WorkflowNodeId = node2.Id,
                Status = FormFlowLinkStatus.Pending
            });

        // Agente asignado al nodo del paso vigente.
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = "Analista IA",
            Role = "Aprobador asistido",
            SystemPrompt = "Analiza solicitudes de compra.",
            IsActive = true
        };
        ctx.AiAgents.Add(agent);
        await ctx.SaveChangesAsync();

        ctx.WorkflowNodeAgents.Add(new WorkflowNodeAgent
        {
            TenantId = tenantId,
            NodeId = node2.Id,
            AiAgentId = agent.Id,
            Autonomy = WorkflowAgentAutonomy.Proposes
        });
        await ctx.SaveChangesAsync();

        return new ScenarioSeed(instance.Id, node1.Id, node2.Id, step1.Id, step2.Id, agent.Id);
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
        var email = $"user-{tenantId:N}@agent.test";
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser
            {
                Email = email,
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            ctx.PlatformUsers.Add(platformUser);
            var tenantUser = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = platformUser.Id,
                Email = email
            };
            ctx.TenantUsers.Add(tenantUser);
            await ctx.SaveChangesAsync();
            tenantUserId = tenantUser.Id;
            platformUserId = platformUser.Id;
        }
        return new SeedData(tenantId, tenantUserId, platformUserId, email);
    }

    private sealed record SeedData(Guid TenantId, Guid TenantUserId, Guid PlatformUserId, string Email);

    private sealed record FlowSeed(Guid DefinitionId, Guid StartNodeId, Guid TaskNodeId);

    private sealed record ScenarioSeed(
        Guid InstanceId, Guid FirstNodeId, Guid SecondNodeId, Guid FirstStepId, Guid CurrentStepId, Guid AgentId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class WorkflowNodeAgentTests_Postgres
    : WorkflowNodeAgentTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public WorkflowNodeAgentTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class WorkflowNodeAgentTests_SqlServer
    : WorkflowNodeAgentTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public WorkflowNodeAgentTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
