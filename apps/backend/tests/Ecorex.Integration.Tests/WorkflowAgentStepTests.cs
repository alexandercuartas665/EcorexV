using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la EJECUCION de agentes de IA en nodos (ola 2) en matriz dual
/// PostgreSQL / SQL Server. El proveedor de IA SIEMPRE es un doble (FakeWorkflowAgentInvoker): aqui
/// no se llama a ningun modelo real, pero SI se ejercitan de verdad el cupo del plan, el registro
/// de consumo, la auditoria del autor, el cierre por el motor y la vuelta a una persona.
///
/// Cubre los cuatro casos que definen el comportamiento:
/// (1) Autonomous: el paso se cierra y el AUTOR registrado es el agente, no una persona;
/// (2) Proposes: el paso sigue pendiente y la propuesta queda visible para quien confirme;
/// (3) fallo del proveedor: el paso vuelve a la persona con el motivo y el flujo NO avanza;
/// (4) aislamiento cross-tenant del runner y del barrido.
/// Ademas: idempotencia del disparo y cupo agotado como caso de "no pudo".
/// </summary>
public abstract class WorkflowAgentStepTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected WorkflowAgentStepTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- (1) Autonomous: cierra el paso y el autor es el AGENTE ----

    [Fact]
    public async Task Autonomous_ClosesStep_WithAgentAsAuthor_AndFlowAdvances()
    {
        var seed = await SeedTenantAsync("AgentStep Autonomous");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var scenario = await SeedScenarioAsync(ctx, seed, WorkflowAgentAutonomy.Autonomous);

        var invoker = FakeWorkflowAgentInvoker.Answering("Approved", "El monto esta dentro del presupuesto.");
        var runner = BuildRunner(ctx, seed, invoker);

        var outcome = await runner.RunAsync(scenario.AgentStepId);
        Assert.Equal(WorkflowAgentStepOutcome.Completed, outcome);

        // Re-leer desde OTRA conexion: se persistio de verdad.
        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var step = await ctx2.WorkflowStepHistories.AsNoTracking().FirstAsync(s => s.Id == scenario.AgentStepId);
        Assert.Equal(WorkflowStepStatus.Completed, step.Status);
        Assert.False(step.IsCurrent);
        Assert.Equal("Approved", step.ApprovalResult);

        // AUDITORIA DEL AUTOR: lo cerro una maquina, no una persona.
        Assert.Equal(scenario.AgentId, step.ExecutedByAiAgentId);
        Assert.Null(step.ExecutedByTenantUserId);
        Assert.NotNull(step.AgentAttemptedAt);
        // Lo que propuso queda registrado aparte de lo que quedo aplicado (se pueden comparar).
        Assert.Equal("Approved", step.AgentProposalResult);
        Assert.Null(step.AgentFailureReason);

        // El flujo AVANZO: el segundo paso quedo vigente.
        var next = await ctx2.WorkflowStepHistories.AsNoTracking()
            .FirstOrDefaultAsync(s => s.NodeId == scenario.SecondNodeId && s.IsCurrent);
        Assert.NotNull(next);
        Assert.Equal(WorkflowStepStatus.Pending, next!.Status);

        // El consumo quedo en el modulo de tokens con su fuente propia (requisito e).
        var usage = await ctx2.AiUsageLogs.AsNoTracking().SingleAsync();
        Assert.Equal(WorkflowAgentStepRunner.UsageSource, usage.Source);
        Assert.Equal(scenario.AgentId, usage.AgentId);
        Assert.True(usage.Success);
        Assert.Equal(150, usage.TotalTokens);

        // IDEMPOTENCIA: volver a pasarlo por el runner no repite la llamada ni toca el paso.
        Assert.Equal(WorkflowAgentStepOutcome.NotApplicable, await runner.RunAsync(scenario.AgentStepId));
        Assert.Equal(1, invoker.Calls);
    }

    // ---- (2) Proposes: el paso sigue pendiente y la propuesta queda visible ----

    [Fact]
    public async Task Proposes_KeepsStepPending_AndStoresProposal()
    {
        var seed = await SeedTenantAsync("AgentStep Proposes");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var scenario = await SeedScenarioAsync(ctx, seed, WorkflowAgentAutonomy.Proposes);

        var invoker = FakeWorkflowAgentInvoker.Answering("Rejected", "El proveedor no esta en la lista aprobada.");
        var runner = BuildRunner(ctx, seed, invoker);

        Assert.Equal(WorkflowAgentStepOutcome.Proposed, await runner.RunAsync(scenario.AgentStepId));

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var step = await ctx2.WorkflowStepHistories.AsNoTracking().FirstAsync(s => s.Id == scenario.AgentStepId);

        // El paso NO se cerro: sigue esperando a una persona.
        Assert.Equal(WorkflowStepStatus.Pending, step.Status);
        Assert.True(step.IsCurrent);
        Assert.Null(step.CompletedAt);
        Assert.Null(step.ApprovalResult);
        // ...pero la propuesta esta guardada y es legible.
        Assert.Equal("Rejected", step.AgentProposalResult);
        Assert.Equal("El proveedor no esta en la lista aprobada.", step.AgentProposalComment);
        Assert.NotNull(step.AgentAttemptedAt);
        Assert.Null(step.AgentFailureReason);
        // Nadie ejecuto el paso todavia: proponer no es ejecutar.
        Assert.Null(step.ExecutedByAiAgentId);

        // El paso quedo en manos del unico candidato del nodo (resolutor de asignacion por nodo).
        Assert.Equal(seed.TenantUserId, step.AssignedToTenantUserId);

        // El flujo NO avanzo.
        Assert.False(await ctx2.WorkflowStepHistories.AsNoTracking().AnyAsync(s => s.NodeId == scenario.SecondNodeId));

        // La propuesta viaja en el DTO del motor (lo que una bandeja mostraria).
        var engine = BuildEngine(ctx2, seed);
        var current = await engine.GetCurrentStepsAsync(scenario.InstanceId);
        var dto = Assert.Single(current, s => s.Id == scenario.AgentStepId);
        Assert.Equal("Rejected", dto.AgentProposalResult);

        // Cuando la PERSONA confirma, los dos autores quedan en la traza: la maquina que propuso y
        // el humano que decidio.
        var completed = await engine.CompleteStepAsync(
            scenario.InstanceId, scenario.AgentStepId, seed.TenantUserId,
            approvalResult: "Approved", approvalComment: "Se autoriza igual",
            executedByAiAgentId: scenario.AgentId);
        Assert.True(completed.IsOk, completed.Error);

        await using var ctx3 = _fixture.CreateContext(seed.TenantId);
        var confirmed = await ctx3.WorkflowStepHistories.AsNoTracking().FirstAsync(s => s.Id == scenario.AgentStepId);
        Assert.Equal(seed.TenantUserId, confirmed.ExecutedByTenantUserId);
        Assert.Equal(scenario.AgentId, confirmed.ExecutedByAiAgentId);
        Assert.Equal("Approved", confirmed.ApprovalResult);       // lo que decidio la persona
        Assert.Equal("Rejected", confirmed.AgentProposalResult);  // lo que habia propuesto el agente
    }

    // ---- (3) Fallo del proveedor: vuelve a la persona y el flujo NO avanza ----

    [Fact]
    public async Task ProviderFailure_ReturnsStepToPerson_AndFlowDoesNotAdvance()
    {
        var seed = await SeedTenantAsync("AgentStep Failure");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var scenario = await SeedScenarioAsync(ctx, seed, WorkflowAgentAutonomy.Autonomous);

        var invoker = FakeWorkflowAgentInvoker.Failing("Error llamando al proveedor Claude: 503 Service Unavailable");
        var runner = BuildRunner(ctx, seed, invoker);

        Assert.Equal(WorkflowAgentStepOutcome.ReturnedToPerson, await runner.RunAsync(scenario.AgentStepId));

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var step = await ctx2.WorkflowStepHistories.AsNoTracking().FirstAsync(s => s.Id == scenario.AgentStepId);

        // El paso sigue vivo para una persona: ni cerrado en falso ni atascado.
        Assert.Equal(WorkflowStepStatus.Pending, step.Status);
        Assert.True(step.IsCurrent);
        Assert.Null(step.ExecutedByAiAgentId);
        Assert.Null(step.ApprovalResult);
        Assert.NotNull(step.AgentFailureReason);
        Assert.Contains("503", step.AgentFailureReason!);
        // Y quedo en el MISMO destinatario que habria tenido sin agente.
        Assert.Equal(seed.TenantUserId, step.AssignedToTenantUserId);

        // El flujo NO avanzo y la instancia sigue corriendo.
        Assert.False(await ctx2.WorkflowStepHistories.AsNoTracking().AnyAsync(s => s.NodeId == scenario.SecondNodeId));
        var instance = await ctx2.WorkflowInstances.AsNoTracking().FirstAsync(i => i.Id == scenario.InstanceId);
        Assert.Equal(WorkflowInstanceStatus.Running, instance.Status);

        // La bitacora de la tarea explica el episodio en lenguaje humano.
        var note = await ctx2.TaskItemActivities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TaskItemId == scenario.TaskItemId && a.ActorName == "Agente de IA");
        Assert.NotNull(note);
        Assert.Contains("no pudo atender", note!.Text);

        // Fallo del proveedor sin tokens facturados: no se inventa consumo.
        Assert.Equal(0, await ctx2.AiUsageLogs.AsNoTracking().CountAsync());

        // No se reintenta en bucle contra un proveedor caido: el intento quedo marcado.
        Assert.Equal(WorkflowAgentStepOutcome.AlreadyAttempted, await runner.RunAsync(scenario.AgentStepId));
        Assert.Equal(1, invoker.Calls);
    }

    // ---- (4) Cupo del plan agotado: es un "no pudo", no un consumo sin control ----

    [Fact]
    public async Task ExhaustedQuota_ReturnsStepToPerson_WithoutCallingProvider()
    {
        var seed = await SeedTenantAsync("AgentStep Quota");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var scenario = await SeedScenarioAsync(ctx, seed, WorkflowAgentAutonomy.Autonomous);
        await SeedExhaustedHardQuotaAsync(ctx, seed.TenantId);

        var invoker = FakeWorkflowAgentInvoker.Answering("Approved", "No deberia llegar aqui.");
        var runner = BuildRunner(ctx, seed, invoker);

        Assert.Equal(WorkflowAgentStepOutcome.ReturnedToPerson, await runner.RunAsync(scenario.AgentStepId));
        // Ni una llamada al proveedor: sin cupo no se consume.
        Assert.Equal(0, invoker.Calls);

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var step = await ctx2.WorkflowStepHistories.AsNoTracking().FirstAsync(s => s.Id == scenario.AgentStepId);
        Assert.Equal(WorkflowStepStatus.Pending, step.Status);
        Assert.True(step.IsCurrent);
        Assert.Contains("cupo", step.AgentFailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- (5) Aislamiento cross-tenant del runner y del barrido ----

    [Fact]
    public async Task AgentStepExecution_IsTenantIsolated()
    {
        var seedA = await SeedTenantAsync("AgentStep Iso A");
        var seedB = await SeedTenantAsync("AgentStep Iso B");

        ScenarioSeed scenarioA;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            scenarioA = await SeedScenarioAsync(ctxA, seedA, WorkflowAgentAutonomy.Autonomous);
        }

        // El runner del tenant B NO puede atender (ni ver) el paso del tenant A.
        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            var invokerB = FakeWorkflowAgentInvoker.Answering("Approved", "Paso ajeno.");
            var runnerB = BuildRunner(ctxB, seedB, invokerB);
            Assert.Equal(WorkflowAgentStepOutcome.NotApplicable, await runnerB.RunAsync(scenarioA.AgentStepId));
            Assert.Equal(0, invokerB.Calls);

            // El barrido acotado al tenant B tampoco toca nada de A.
            var dispatcherB = new WorkflowAgentStepDispatcher(
                ctxB, new TestTenantContext(seedB.TenantId, seedB.PlatformUserId), runnerB,
                NullLogger<WorkflowAgentStepDispatcher>.Instance);
            Assert.Equal(0, await dispatcherB.RunPendingForTenantAsync());
            Assert.Equal(0, invokerB.Calls);
        }

        // El paso de A quedo intacto: nadie de B lo intento.
        await using (var ctxA2 = _fixture.CreateContext(seedA.TenantId))
        {
            var step = await ctxA2.WorkflowStepHistories.AsNoTracking().FirstAsync(s => s.Id == scenarioA.AgentStepId);
            Assert.Equal(WorkflowStepStatus.Pending, step.Status);
            Assert.Null(step.AgentAttemptedAt);
            Assert.Null(step.ExecutedByAiAgentId);
        }

        // El barrido de plataforma (unico punto cross-tenant) ve el trabajo de A y NO el de B
        // (B no tiene ningun nodo con agente), y solo devuelve ids de tenant.
        await using (var ctxPlatform = _fixture.CreateContext(tenantId: null))
        {
            var dispatcher = new WorkflowAgentStepDispatcher(
                ctxPlatform, new TestTenantContext(null), new ThrowingRunner(),
                NullLogger<WorkflowAgentStepDispatcher>.Instance);
            var tenants = await dispatcher.FindTenantsWithPendingAgentStepsAsync();
            Assert.Contains(seedA.TenantId, tenants);
            Assert.DoesNotContain(seedB.TenantId, tenants);

            // Sin tenant activo el barrido acotado no atiende NADA (nunca corre "sobre todos").
            Assert.Equal(0, await dispatcher.RunPendingForTenantAsync());
        }

        // Y el barrido acotado al tenant A si atiende su paso (mismo camino que usa el worker).
        await using (var ctxA3 = _fixture.CreateContext(seedA.TenantId))
        {
            var invokerA = FakeWorkflowAgentInvoker.Answering("Approved", "Todo en orden.");
            var dispatcherA = new WorkflowAgentStepDispatcher(
                ctxA3, new TestTenantContext(seedA.TenantId, seedA.PlatformUserId),
                BuildRunner(ctxA3, seedA, invokerA), NullLogger<WorkflowAgentStepDispatcher>.Instance);
            Assert.Equal(1, await dispatcherA.RunPendingForTenantAsync());
            Assert.Equal(1, invokerA.Calls);
        }
    }

    // ---- Dobles ----

    /// <summary>
    /// Doble del proveedor de IA. Sustituye SOLO la llamada de red: el cupo, el registro de
    /// consumo, la auditoria del autor y la vuelta a una persona corren con el codigo real.
    /// </summary>
    private sealed class FakeWorkflowAgentInvoker : IWorkflowAgentInvoker
    {
        private readonly WorkflowAgentInvocationResult _result;

        private FakeWorkflowAgentInvoker(WorkflowAgentInvocationResult result) => _result = result;

        public int Calls { get; private set; }

        public static FakeWorkflowAgentInvoker Answering(string result, string comment)
            => new(new WorkflowAgentInvocationResult(
                true, result, comment, null, AiProvider.Claude, "modelo-de-prueba", 100, 50));

        public static FakeWorkflowAgentInvoker Failing(string error)
            => new(WorkflowAgentInvocationResult.Failed(error, AiProvider.Claude, "modelo-de-prueba"));

        public Task<WorkflowAgentInvocationResult> InvokeAsync(
            WorkflowAgentContextDto context, CancellationToken cancellationToken = default)
        {
            Calls++;
            // El contexto de la ola 1 tiene que llegar armado hasta aqui.
            Assert.NotNull(context.Assignment);
            return Task.FromResult(_result);
        }
    }

    /// <summary>Runner que no debe ser invocado (barrido de plataforma: solo devuelve ids).</summary>
    private sealed class ThrowingRunner : IWorkflowAgentStepRunner
    {
        public Task<WorkflowAgentStepOutcome> RunAsync(Guid stepId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("El barrido de plataforma no debe atender pasos.");
    }

    // ---- Helpers ----

    private static WorkflowEngine BuildEngine(EcorexDbContext ctx, SeedData seed)
        => new(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId),
            new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster());

    private static WorkflowAgentStepRunner BuildRunner(
        EcorexDbContext ctx, SeedData seed, IWorkflowAgentInvoker invoker)
    {
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        return new WorkflowAgentStepRunner(
            ctx,
            new WorkflowAgentContextBuilder(ctx),
            invoker,
            new AiUsageService(ctx, tenantContext),
            new NodeAssigneeResolver(ctx),
            BuildEngine(ctx, seed),
            TimeProvider.System,
            NullLogger<WorkflowAgentStepRunner>.Instance);
    }

    /// <summary>
    /// Escenario real: flujo Start -> Paso con agente -> Segundo paso -> Fin, arrancado POR EL
    /// MOTOR (no a mano), con tarea asociada, agente en el primer nodo Task y un unico candidato
    /// humano para ese nodo (cargo con un funcionario) via WorkflowNodePolicy.
    /// </summary>
    private static async Task<ScenarioSeed> SeedScenarioAsync(
        EcorexDbContext ctx, SeedData seed, WorkflowAgentAutonomy autonomy)
    {
        var tenantId = seed.TenantId;

        var definition = new WorkflowDefinition
        {
            TenantId = tenantId,
            ProcessCode = $"AGX-{Guid.NewGuid():N}"[..12],
            Name = "Aprobacion de compra",
            BpmnXml = "<xml/>",
            Version = 1,
            IsPublished = true
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
        var nodeAgente = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "Task_Aprobar",
            Name = "Aprobar solicitud",
            NodeType = WorkflowNodeType.Task,
            StepNumber = 1,
            AllowsAssignment = true,
            Note = "Revisar el monto contra el presupuesto"
        };
        var nodeSegundo = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "Task_Comprar",
            Name = "Ejecutar la compra",
            NodeType = WorkflowNodeType.Task,
            StepNumber = 2,
            AllowsAssignment = true
        };
        var end = new WorkflowNode
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            BpmnElementId = "End_1",
            Name = "Fin",
            NodeType = WorkflowNodeType.EndEvent
        };
        ctx.WorkflowNodes.AddRange(start, nodeAgente, nodeSegundo, end);
        ctx.WorkflowEdges.AddRange(
            new WorkflowEdge
            {
                TenantId = tenantId,
                DefinitionId = definition.Id,
                SourceNodeId = start.Id,
                TargetNodeId = nodeAgente.Id,
                BpmnElementId = "Flow_1"
            },
            new WorkflowEdge
            {
                TenantId = tenantId,
                DefinitionId = definition.Id,
                SourceNodeId = nodeAgente.Id,
                TargetNodeId = nodeSegundo.Id,
                BpmnElementId = "Flow_2"
            },
            new WorkflowEdge
            {
                TenantId = tenantId,
                DefinitionId = definition.Id,
                SourceNodeId = nodeSegundo.Id,
                TargetNodeId = end.Id,
                BpmnElementId = "Flow_3"
            });

        // Organigrama minimo: un cargo con UN funcionario. Es el destinatario humano del nodo
        // cuando el agente no puede (mismo resolutor que usa la bandeja).
        var cargo = new OrgUnit
        {
            TenantId = tenantId,
            Name = "Aprobador",
            Classifier = OrgUnitClassifier.Cargo
        };
        var funcionario = new OrgUnit
        {
            TenantId = tenantId,
            Name = "Funcionario aprobador",
            Classifier = OrgUnitClassifier.Funcionario,
            ParentId = cargo.Id,
            TenantUserId = seed.TenantUserId
        };
        ctx.OrgUnits.AddRange(cargo, funcionario);
        ctx.WorkflowNodePolicies.Add(new WorkflowNodePolicy
        {
            TenantId = tenantId,
            WorkflowNodeId = nodeAgente.Id,
            OrgUnitId = cargo.Id
        });

        // Agente de IA asignado al primer nodo Task, con la autonomia del caso bajo prueba.
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = "Analista IA",
            Role = "Aprobador asistido",
            SystemPrompt = "Analiza solicitudes de compra.",
            Provider = AiProvider.Claude,
            IsActive = true
        };
        ctx.AiAgents.Add(agent);

        var task = new TaskItem
        {
            TenantId = tenantId,
            Number = "T00099",
            Title = "Compra de portatiles",
            Description = "Se requieren 5 equipos para el area comercial",
            Priority = TaskPriority.High,
            Status = TaskItemStatus.Active
        };
        ctx.TaskItems.Add(task);
        await ctx.SaveChangesAsync();

        ctx.WorkflowNodeAgents.Add(new WorkflowNodeAgent
        {
            TenantId = tenantId,
            NodeId = nodeAgente.Id,
            AiAgentId = agent.Id,
            Autonomy = autonomy
        });
        await ctx.SaveChangesAsync();

        // Arranque REAL por el motor: deja vigente el paso del nodo con agente.
        var engine = BuildEngine(ctx, seed);
        var started = await engine.StartInstanceAsync(definition.Id, task.Id);
        Assert.True(started.IsOk, started.Error);
        var agentStep = Assert.Single(started.Value!.CurrentSteps, s => s.NodeId == nodeAgente.Id);

        return new ScenarioSeed(
            started.Value.Id, agentStep.Id, nodeAgente.Id, nodeSegundo.Id, agent.Id, task.Id);
    }

    /// <summary>
    /// Plan con limite DURO de tokens de IA ya consumido: el escenario de "sin cupo". El consumo
    /// previo se siembra como un AiUsageLog del mes en curso, igual que lo escribiria el sistema.
    /// </summary>
    private static async Task SeedExhaustedHardQuotaAsync(EcorexDbContext ctx, Guid tenantId)
    {
        var plan = new SaasPlan
        {
            Name = $"Plan {Guid.NewGuid():N}"[..12],
            IsActive = true
        };
        ctx.SaasPlans.Add(plan);
        await ctx.SaveChangesAsync();

        ctx.SaasPlanLimits.Add(new SaasPlanLimit
        {
            PlanId = plan.Id,
            LimitKey = IAiUsageService.MonthlyTokenLimitKey,
            LimitValue = 1_000,
            EnforcementMode = LimitEnforcementMode.Hard
        });
        ctx.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        ctx.AiUsageLogs.Add(new AiUsageLog
        {
            TenantId = tenantId,
            Provider = AiProvider.Claude,
            Model = "modelo-de-prueba",
            InputTokens = 900,
            OutputTokens = 200,
            TotalTokens = 1_100,
            Source = "chat",
            Success = true
        });
        await ctx.SaveChangesAsync();
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
        var email = $"user-{tenantId:N}@agentstep.test";
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

    private sealed record ScenarioSeed(
        Guid InstanceId, Guid AgentStepId, Guid AgentNodeId, Guid SecondNodeId, Guid AgentId, Guid TaskItemId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class WorkflowAgentStepTests_Postgres
    : WorkflowAgentStepTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public WorkflowAgentStepTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class WorkflowAgentStepTests_SqlServer
    : WorkflowAgentStepTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public WorkflowAgentStepTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
