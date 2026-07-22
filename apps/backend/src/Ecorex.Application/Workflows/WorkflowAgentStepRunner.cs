using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion de <see cref="IWorkflowAgentStepRunner"/>.
///
/// ORDEN DELIBERADO de las tres fases, y el porque de cada frontera:
///   1. LEER (sin transaccion): paso, agente del nodo, contexto de la ola 1 y cupo del plan.
///   2. LLAMAR AL PROVEEDOR (sin transaccion, sin nada abierto): tarda segundos y puede fallar.
///      Sostener una transaccion mientras se espera a un tercero por red bloquearia filas del
///      historial durante todo ese tiempo y un timeout del proveedor revertiria trabajo del flujo
///      que nada tiene que ver con el. Por eso la llamada vive en IWorkflowAgentInvoker y aqui no
///      hay ni un BeginTransaction abierto cuando se hace.
///   3. DECIDIR Y PERSISTIR (transaccion corta): marca de intento + autor + propuesta/motivo, y en
///      modo Autonomous el cierre del paso via el motor, que aporta su propio avance en cascada.
///
/// El consumo (AiUsageLog) se registra ANTES de abrir la transaccion de la fase 3: los tokens ya se
/// gastaron en el proveedor, asi que el registro no puede quedar atado a que la escritura del paso
/// prospere. Un consumo que se pierde en un rollback es un consumo que el tenant no paga y la
/// plataforma si.
/// </summary>
public sealed class WorkflowAgentStepRunner : IWorkflowAgentStepRunner
{
    /// <summary>Fuente que queda en AiUsageLog para distinguir este gasto del chat y las pruebas.</summary>
    public const string UsageSource = "workflow-agent";

    private readonly IApplicationDbContext _db;
    private readonly IWorkflowAgentContextBuilder _contextBuilder;
    private readonly IWorkflowAgentInvoker _invoker;
    private readonly IAiUsageService _usage;
    private readonly INodeAssigneeResolver _assigneeResolver;
    private readonly IWorkflowEngine _engine;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorkflowAgentStepRunner> _logger;

    public WorkflowAgentStepRunner(
        IApplicationDbContext db,
        IWorkflowAgentContextBuilder contextBuilder,
        IWorkflowAgentInvoker invoker,
        IAiUsageService usage,
        INodeAssigneeResolver assigneeResolver,
        IWorkflowEngine engine,
        TimeProvider clock,
        ILogger<WorkflowAgentStepRunner> logger)
    {
        _db = db;
        _contextBuilder = contextBuilder;
        _invoker = invoker;
        _usage = usage;
        _assigneeResolver = assigneeResolver;
        _engine = engine;
        _clock = clock;
        _logger = logger;
    }

    public async Task<WorkflowAgentStepOutcome> RunAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        // ---- Fase 1: leer ----

        // Sin filtro manual por TenantId (regla 1 de CLAUDE.md): un paso de otro tenant simplemente
        // no existe para esta consulta, asi que el aislamiento no depende de que nadie se acuerde.
        var step = await _db.WorkflowStepHistories.FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null)
        {
            return WorkflowAgentStepOutcome.NotApplicable;
        }
        if (!step.IsCurrent || step.Status != WorkflowStepStatus.Pending)
        {
            // El paso ya se cerro (o lo cerro una persona mientras el barrido lo tomaba).
            return WorkflowAgentStepOutcome.NotApplicable;
        }
        if (step.AgentAttemptedAt is not null)
        {
            return WorkflowAgentStepOutcome.AlreadyAttempted;
        }

        var nodeAgent = await _db.WorkflowNodeAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.NodeId == step.NodeId, cancellationToken);
        if (nodeAgent is null)
        {
            return WorkflowAgentStepOutcome.NoAgent;
        }

        var contextResult = await _contextBuilder.BuildAsync(step.Id, cancellationToken);
        if (!contextResult.IsOk || contextResult.Value is null)
        {
            // No se pudo armar el contexto: es un "no pudo" como cualquier otro, con su motivo.
            return await ReturnToPersonAsync(
                step, nodeAgent.AiAgentId,
                $"No se pudo preparar el contexto del paso para el agente: {contextResult.Error}",
                cancellationToken);
        }
        var context = contextResult.Value;

        // Cupo del plan (requisito d): un agente NO consume sin control. Si el limite es duro y ya
        // se agoto el mes, ni siquiera se llama al proveedor y el paso vuelve a una persona: el
        // proceso del cliente sigue corriendo aunque su cupo de IA se haya acabado.
        var quota = await _usage.GetQuotaAsync(cancellationToken);
        if (quota.Exceeded && quota.Hard)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent.AiAgentId,
                $"Se agoto el cupo mensual de tokens de IA del plan ({quota.MonthlyLimitTokens:N0}). "
                    + "El paso queda para atencion humana.",
                cancellationToken);
        }

        // ---- Fase 2: llamar al proveedor (NADA abierto: ni transaccion, ni bloqueos) ----

        var invocation = await _invoker.InvokeAsync(context, cancellationToken);

        // Consumo: se registra aunque el intento fallara (los tokens de una llamada fallida a mitad
        // de camino tambien se facturan). Va en su propio SaveChanges, fuera de la transaccion de
        // abajo, para que un rollback del paso no borre un gasto que ya ocurrio.
        if (invocation.Ok || invocation.InputTokens > 0 || invocation.OutputTokens > 0)
        {
            await _usage.RecordAsync(
                nodeAgent.AiAgentId, invocation.Provider, invocation.Model,
                invocation.InputTokens, invocation.OutputTokens, UsageSource, invocation.Ok, cancellationToken);
        }

        if (!invocation.Ok)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent.AiAgentId,
                invocation.Error ?? "El agente no pudo resolver el paso.",
                cancellationToken);
        }

        // ---- Fase 3: decidir y persistir (transaccion corta) ----

        var now = _clock.GetUtcNow();
        step.AgentAttemptedAt = now;
        step.AgentProposalResult = invocation.Result;
        step.AgentProposalComment = invocation.Comment;

        if (nodeAgent.Autonomy == WorkflowAgentAutonomy.Autonomous)
        {
            // El agente CIERRA el paso. El autor registrado es el agente (executedByAiAgentId) y el
            // usuario queda en null: nadie humano tomo esta decision y la traza no debe sugerir que si.
            // El motor abre su transaccion y guarda tambien los campos de arriba (misma instancia
            // rastreada por el DbContext scoped), asi que el intento y el cierre son atomicos.
            var completed = await _engine.CompleteStepAsync(
                step.InstanceId, step.Id, executedByTenantUserId: null,
                approvalResult: invocation.Result, approvalComment: invocation.Comment,
                executedByAiAgentId: nodeAgent.AiAgentId, cancellationToken: cancellationToken);

            if (!completed.IsOk && completed.Status != WorkflowEngineStatus.StuckDetected)
            {
                // El motor rechazo el cierre (conflicto de concurrencia, instancia ya cerrada...).
                // No se puede dejar el paso a medias: vuelve a una persona con el motivo.
                _logger.LogWarning(
                    "El agente {AgentId} no pudo cerrar el paso {StepId}: {Error}",
                    nodeAgent.AiAgentId, step.Id, completed.Error);
                return await ReturnToPersonAsync(
                    step, nodeAgent.AiAgentId,
                    $"El agente decidio '{invocation.Result}' pero el flujo no acepto el cierre: {completed.Error}",
                    cancellationToken);
            }
            return WorkflowAgentStepOutcome.Completed;
        }

        // Modo Proposes: el paso NO se cierra. Queda vigente, con la propuesta guardada aparte de
        // ApprovalResult/ApprovalComment (que son de quien confirme) para que se puedan comparar.
        await using var transaction = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        await AssignToPersonIfUnambiguousAsync(step, cancellationToken);
        await AddTaskNoteAsync(step,
            $"el agente propuso '{invocation.Result}' para el paso; falta confirmacion de una persona",
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowAgentStepOutcome.Proposed;
    }

    /// <summary>
    /// El agente no pudo: el paso SIGUE vigente y Pending (el flujo no avanza), se anota el motivo
    /// legible y se deja en manos de quien lo habria atendido si el nodo no tuviera agente.
    /// Tambien marca AgentAttemptedAt, para no reintentar en bucle contra un proveedor caido.
    /// </summary>
    private async Task<WorkflowAgentStepOutcome> ReturnToPersonAsync(
        WorkflowStepHistory step, Guid agentId, string reason, CancellationToken cancellationToken)
    {
        step.AgentAttemptedAt = _clock.GetUtcNow();
        step.ExecutedByAiAgentId = null;   // nadie ejecuto el paso todavia: solo se intento
        step.AgentFailureReason = Clip(reason, 500);

        await using var transaction = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        await AssignToPersonIfUnambiguousAsync(step, cancellationToken);
        await AddTaskNoteAsync(step, $"el agente de IA no pudo atender el paso: {reason}", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "El agente {AgentId} devolvio el paso {StepId} a atencion humana: {Reason}", agentId, step.Id, reason);
        return WorkflowAgentStepOutcome.ReturnedToPerson;
    }

    /// <summary>
    /// Destinatario humano del paso, con el MISMO resolutor por nodo que usa la bandeja
    /// (INodeAssigneeResolver sobre las WorkflowNodePolicy del nodo).
    ///
    /// Solo se asigna cuando hay UN candidato: con varios se deja sin asignar a proposito, porque
    /// asi es exactamente como se comporta un paso sin agente -la bandeja lo muestra a todo el
    /// grupo y lo toma quien pueda- y elegir a uno al azar seria peor que no elegir. Si el paso ya
    /// tenia dueno, no se toca.
    /// </summary>
    private async Task AssignToPersonIfUnambiguousAsync(WorkflowStepHistory step, CancellationToken cancellationToken)
    {
        if (step.AssignedToTenantUserId is not null)
        {
            return;
        }
        var candidates = await _assigneeResolver.ResolveCandidatesAsync(step.NodeId, cancellationToken);
        if (candidates.Count == 1)
        {
            step.AssignedToTenantUserId = candidates[0];
        }
    }

    /// <summary>
    /// Deja la nota en la bitacora de la tarea asociada (si el flujo nacio de una). Es lo que hace
    /// legible el episodio para el humano que abre el caso, sin obligarlo a mirar el historial tecnico.
    /// </summary>
    private async Task AddTaskNoteAsync(WorkflowStepHistory step, string text, CancellationToken cancellationToken)
    {
        var taskId = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == step.InstanceId)
            .Select(i => i.TaskItemId)
            .FirstOrDefaultAsync(cancellationToken);
        if (taskId is not Guid id)
        {
            return;
        }
        _db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = step.TenantId,
            TaskItemId = id,
            Type = TaskActivityType.Action,
            ActorName = "Agente de IA",
            Text = Clip(text, 2000)!
        });
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
