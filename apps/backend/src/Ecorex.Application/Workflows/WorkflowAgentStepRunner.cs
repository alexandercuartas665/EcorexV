using Ecorex.Application.Common;
using Ecorex.Application.Forms;
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
    private readonly IFormResponseService _forms;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorkflowAgentStepRunner> _logger;

    public WorkflowAgentStepRunner(
        IApplicationDbContext db,
        IWorkflowAgentContextBuilder contextBuilder,
        IWorkflowAgentInvoker invoker,
        IAiUsageService usage,
        INodeAssigneeResolver assigneeResolver,
        IWorkflowEngine engine,
        IFormResponseService forms,
        TimeProvider clock,
        ILogger<WorkflowAgentStepRunner> logger)
    {
        _db = db;
        _contextBuilder = contextBuilder;
        _invoker = invoker;
        _usage = usage;
        _assigneeResolver = assigneeResolver;
        _engine = engine;
        _forms = forms;
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

        // El tipo de nodo decide la FORMA de la decision: una COMPUERTA elige una RUTA (ola B), un Task con
        // formulario lo LLENA (ola C, Fields != null), y un Task de decision fija un RESULTADO.
        var isGateway = context.Node.NodeType == WorkflowNodeType.ExclusiveGateway;
        var isForm = invocation.Fields is not null;

        // En una compuerta se resuelve la ruta ANTES de tocar el paso: si el agente no eligio una salida
        // valida, es un "no pudo" y el paso vuelve a una persona (nunca se enruta a ciegas).
        Guid? routeTargetId = null;
        string? routeLabel = null;
        if (isGateway)
        {
            if (string.IsNullOrWhiteSpace(invocation.Route))
            {
                return await ReturnToPersonAsync(
                    step, nodeAgent.AiAgentId, "El agente no eligio una ruta para la compuerta.", cancellationToken);
            }
            var (targetId, targetName) = await ResolveRouteTargetAsync(step.NodeId, invocation.Route!, cancellationToken);
            if (targetId is null)
            {
                return await ReturnToPersonAsync(
                    step, nodeAgent.AiAgentId,
                    $"El agente eligio una ruta ('{invocation.Route}') que no es una salida de esta compuerta.",
                    cancellationToken);
            }
            routeTargetId = targetId;
            routeLabel = targetName ?? invocation.Route;
        }
        else if (!isForm && string.IsNullOrWhiteSpace(invocation.Result))
        {
            return await ReturnToPersonAsync(
                step, nodeAgent.AiAgentId, "El agente no indico un resultado para el paso.", cancellationToken);
        }

        var now = _clock.GetUtcNow();
        step.AgentAttemptedAt = now;
        // La propuesta guardada: la ruta en una compuerta, "Formulario" en un llenado, o el resultado en un Task.
        step.AgentProposalResult = isGateway ? Clip(routeLabel, 20) : (isForm ? "Formulario" : invocation.Result);
        step.AgentProposalComment = invocation.Comment;

        // FORMULARIO (ola C): se atiende como una persona -> materializa el draft/link del paso y lo guarda por
        // SaveAsync (misma validacion, reglas on-submit y cierre de paso). Autonomo=envia, Propone=deja el draft.
        if (isForm)
        {
            return await SubmitAgentFormAsync(step, nodeAgent, invocation, cancellationToken);
        }

        if (nodeAgent.Autonomy == WorkflowAgentAutonomy.Autonomous)
        {
            // El agente CIERRA el paso. El autor registrado es el agente (executedByAiAgentId) y el
            // usuario queda en null: nadie humano tomo esta decision y la traza no debe sugerir que si.
            // El motor abre su transaccion y guarda tambien los campos de arriba (misma instancia
            // rastreada por el DbContext scoped), asi que el intento y el cierre son atomicos.
            var completed = isGateway
                ? await _engine.ChooseGatewayRouteAsync(
                    step.InstanceId, step.Id, routeTargetId!.Value, tenantUserId: null,
                    note: invocation.Comment, executedByAiAgentId: nodeAgent.AiAgentId, cancellationToken: cancellationToken)
                : await _engine.CompleteStepAsync(
                    step.InstanceId, step.Id, executedByTenantUserId: null,
                    approvalResult: invocation.Result, approvalComment: invocation.Comment,
                    executedByAiAgentId: nodeAgent.AiAgentId, cancellationToken: cancellationToken);

            if (!completed.IsOk && completed.Status != WorkflowEngineStatus.StuckDetected)
            {
                // El motor rechazo el avance (conflicto de concurrencia, instancia ya cerrada...).
                // No se puede dejar el paso a medias: vuelve a una persona con el motivo.
                var decision = isGateway ? $"la ruta '{routeLabel}'" : $"'{invocation.Result}'";
                _logger.LogWarning(
                    "El agente {AgentId} no pudo avanzar el paso {StepId}: {Error}",
                    nodeAgent.AiAgentId, step.Id, completed.Error);
                return await ReturnToPersonAsync(
                    step, nodeAgent.AiAgentId,
                    $"El agente eligio {decision} pero el flujo no lo acepto: {completed.Error}",
                    cancellationToken);
            }
            return WorkflowAgentStepOutcome.Completed;
        }

        // Modo Proposes: el paso NO se cierra. Queda vigente, con la propuesta guardada aparte de
        // ApprovalResult/ApprovalComment (que son de quien confirme) para que se puedan comparar.
        await using var transaction = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        await AssignToPersonIfUnambiguousAsync(step, cancellationToken);
        var propuesta = isGateway
            ? $"el agente propuso la ruta '{routeLabel}' en la compuerta; falta confirmacion de una persona"
            : $"el agente propuso '{invocation.Result}' para el paso; falta confirmacion de una persona";
        await AddTaskNoteAsync(step, propuesta, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowAgentStepOutcome.Proposed;
    }

    /// <summary>Mapea la 'ruta' que devolvio el agente (clave = BpmnElementId del destino, o su nombre) a un
    /// nodo destino que sea salida DIRECTA de la compuerta. Match unico por clave y, si no, por nombre
    /// (case-insensitive). (null, null) si no hay una unica coincidencia: el runner lo trata como "no pudo".</summary>
    private async Task<(Guid? TargetId, string? TargetName)> ResolveRouteTargetAsync(
        Guid gatewayNodeId, string route, CancellationToken cancellationToken)
    {
        var candidates = await (
            from e in _db.WorkflowEdges.AsNoTracking()
            join t in _db.WorkflowNodes.AsNoTracking() on e.TargetNodeId equals t.Id
            where e.SourceNodeId == gatewayNodeId
            select new { t.Id, t.BpmnElementId, t.Name }).ToListAsync(cancellationToken);

        var key = route.Trim();
        var byKey = candidates.Where(c => string.Equals(c.BpmnElementId, key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byKey.Count == 1) { return (byKey[0].Id, byKey[0].Name); }
        var byName = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byName.Count == 1) { return (byName[0].Id, byName[0].Name); }
        return (null, null);
    }

    /// <summary>Diligencia (y, en modo Autonomo, ENVIA) el formulario del paso con los valores que fijo el
    /// agente (ADR-0090 ola C). Reutiliza el MISMO camino de una persona: GetTaskStepFormsAsync materializa el
    /// draft + FormFlowLink del paso, y SaveAsync valida por tipo, corre las reglas on-submit y, al enviar,
    /// completa el paso via el motor. Si no valida, el paso vuelve a una persona con el error (nunca se fuerza).</summary>
    private async Task<WorkflowAgentStepOutcome> SubmitAgentFormAsync(
        WorkflowStepHistory step, WorkflowNodeAgent nodeAgent, WorkflowAgentInvocationResult invocation,
        CancellationToken cancellationToken)
    {
        var autonomous = nodeAgent.Autonomy == WorkflowAgentAutonomy.Autonomous;

        var taskId = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == step.InstanceId).Select(i => i.TaskItemId).FirstOrDefaultAsync(cancellationToken);
        if (taskId is not Guid tid)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent.AiAgentId,
                "El paso no esta asociado a una tarea: el agente no puede diligenciar su formulario.", cancellationToken);
        }

        // Materializa (idempotente) el draft + link Pending del paso y toma el del nodo actual.
        var stepForms = await _forms.GetTaskStepFormsAsync(tid, cancellationToken);
        var target = stepForms.FirstOrDefault(f => f.WorkflowNodeId == step.NodeId);
        if (target is null)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent.AiAgentId, "No se encontro el formulario del paso para diligenciar.", cancellationToken);
        }

        // El Type del FormFieldValue lo re-deriva SaveAsync de la definicion del campo; aqui solo viaja el valor.
        var data = invocation.Fields!.ToDictionary(
            kv => kv.Key, kv => new FormFieldValue(kv.Value, "text"), StringComparer.Ordinal);

        var saved = await _forms.SaveAsync(
            target.ResponseId, data, submit: autonomous, submittedByTenantUserId: null,
            approvalResult: null, hiddenFieldCodes: null, executedByAiAgentId: nodeAgent.AiAgentId,
            cancellationToken: cancellationToken);

        if (!saved.IsOk)
        {
            var detail = saved.FieldErrors is { Count: > 0 }
                ? string.Join("; ", saved.FieldErrors.Select(e => $"{e.Key}: {e.Value}"))
                : saved.Error;
            _logger.LogInformation(
                "El agente {AgentId} lleno el formulario del paso {StepId} pero no valido: {Detail}",
                nodeAgent.AiAgentId, step.Id, detail);
            return await ReturnToPersonAsync(
                step, nodeAgent.AiAgentId,
                $"El formulario que lleno el agente no paso la validacion: {detail}", cancellationToken);
        }

        if (autonomous)
        {
            // SaveAsync(submit) ya guardo el draft, corrio las reglas y cerro el paso via el FormFlowLink
            // (y persistio AgentAttemptedAt/AgentProposalResult del 'step' rastreado, en su transaccion).
            return WorkflowAgentStepOutcome.Completed;
        }

        // Propone: el draft quedo lleno (SaveAsync submit=false); el paso sigue vigente para que una persona
        // lo revise y lo envie. Se anota y se asigna si es inequivoco (igual que una propuesta de decision).
        await using var transaction = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        await AssignToPersonIfUnambiguousAsync(step, cancellationToken);
        await AddTaskNoteAsync(step,
            "el agente diligencio el formulario del paso; falta que una persona lo revise y lo envie", cancellationToken);
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
