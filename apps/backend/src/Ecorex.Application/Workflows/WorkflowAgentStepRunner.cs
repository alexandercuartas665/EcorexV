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
    private readonly Voice.IRetellVoiceService _voice;
    private readonly IWorkflowAgentWhatsApp _whatsApp;
    private readonly IAgentProgressBroadcaster _progress;
    private readonly TimeProvider _clock;
    private readonly ILogger<WorkflowAgentStepRunner> _logger;

    /// <summary>ADR-0092: tope de preguntas por WhatsApp en una misma conversacion de un paso, para acotar el
    /// costo y evitar ciclos si la persona no da el dato. Superado -> el paso vuelve a una persona.</summary>
    private const int MaxWhatsAppAsks = 4;

    /// <summary>
    /// Tope de reloj de UNA corrida del agente (la llamada al proveedor + su bucle de herramientas). Acota un
    /// proveedor colgado: superado, el paso vuelve a una persona con el motivo. Configurable por entorno
    /// (ECOREX_AGENT_RUN_TIMEOUT_MIN), 6 min por defecto.
    /// </summary>
    private static readonly int RunTimeoutMinutes = ReadEnvInt("ECOREX_AGENT_RUN_TIMEOUT_MIN", 6, 1, 60);

    /// <summary>
    /// Cuanto puede quedar un paso EN ESPERA (llamada/WhatsApp) antes de que el reaper lo cierre por tiempo
    /// agotado. Evita que un paso quede colgado indefinidamente si la respuesta nunca llega. Configurable por
    /// entorno (ECOREX_AGENT_WAIT_TIMEOUT_HOURS), 6 horas por defecto.
    /// </summary>
    public static readonly int WaitTimeoutHours = ReadEnvInt("ECOREX_AGENT_WAIT_TIMEOUT_HOURS", 6, 1, 720);

    private static int ReadEnvInt(string name, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var v) ? Math.Clamp(v, min, max) : fallback;
    }

    public WorkflowAgentStepRunner(
        IApplicationDbContext db,
        IWorkflowAgentContextBuilder contextBuilder,
        IWorkflowAgentInvoker invoker,
        IAiUsageService usage,
        INodeAssigneeResolver assigneeResolver,
        IWorkflowEngine engine,
        IFormResponseService forms,
        Voice.IRetellVoiceService voice,
        IWorkflowAgentWhatsApp whatsApp,
        IAgentProgressBroadcaster progress,
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
        _voice = voice;
        _whatsApp = whatsApp;
        _progress = progress;
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
                step, nodeAgent,
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
                step, nodeAgent,
                $"Se agoto el cupo mensual de tokens de IA del plan ({quota.MonthlyLimitTokens:N0}). "
                    + "El paso queda para atencion humana.",
                cancellationToken);
        }

        // ---- Fase 2: llamar al proveedor (NADA abierto: ni transaccion, ni bloqueos) ----

        // Capa 2 (ADR-0091): progreso EN VIVO. Se resuelve el taskId una vez y el invoker llama al callback
        // cada ronda; el runner lo transmite por SignalR (best-effort, fire-and-forget) para que el nodo abierto
        // muestre el "pensamiento" del agente y los tokens creciendo. El broadcaster usa su propio IHubContext
        // (no el _db), asi que no interfiere con la regla de "nada abierto" de la fase 2.
        var taskId = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == step.InstanceId).Select(i => i.TaskItemId).FirstOrDefaultAsync(cancellationToken);

        // Log del agente (C): recolectamos las fases que el invoker reporta EN VIVO por el callback, para
        // persistirlas como bitacora legible del paso ADEMAS de transmitirlas por SignalR.
        var rounds = new List<string>();
        Action<string, long> onProgress = (phase, tokens) =>
        {
            if (!string.IsNullOrWhiteSpace(phase)) { rounds.Add(phase.Trim()); }
            if (taskId is Guid tp) { ReportProgress(step.TenantId, tp, step.NodeId, phase, tokens); }
        };

        var attemptNo = step.AgentAttemptCount + 1;

        // Tope de reloj de la corrida (B): un proveedor colgado no debe dejar el paso "trabajando" sin fin.
        WorkflowAgentInvocationResult invocation;
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(RunTimeoutMinutes));
            try
            {
                invocation = await _invoker.InvokeAsync(context, timeoutCts.Token, onProgress);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Fue el timeout de la corrida (no un apagado del proceso): se trata como "no pudo".
                AppendRun(step, attemptNo, 0, rounds, "tiempo de ejecucion agotado");
                return await ReturnToPersonAsync(
                    step, nodeAgent,
                    $"El agente supero el tiempo maximo de ejecucion ({RunTimeoutMinutes} min) sin resolver.",
                    cancellationToken);
            }
        }

        var runTokens = (long)invocation.InputTokens + invocation.OutputTokens;

        // Consumo: se registra aunque el intento fallara (los tokens de una llamada fallida a mitad
        // de camino tambien se facturan). Va en su propio SaveChanges, fuera de la transaccion de
        // abajo, para que un rollback del paso no borre un gasto que ya ocurrio.
        if (invocation.Ok || invocation.InputTokens > 0 || invocation.OutputTokens > 0)
        {
            await _usage.RecordAsync(
                nodeAgent.AiAgentId, invocation.Provider, invocation.Model,
                invocation.InputTokens, invocation.OutputTokens, UsageSource, invocation.Ok, cancellationToken);
        }

        // Tokens y bitacora del paso: se anexan a la instancia RASTREADA; cualquier SaveChanges posterior
        // (propuesta, pausa, cierre o devolucion) los persiste junto con la decision.
        step.AgentTokensUsed = (step.AgentTokensUsed ?? 0) + (int)Math.Min(runTokens, int.MaxValue);
        AppendRun(step, attemptNo, runTokens, rounds,
            invocation.Ok ? "ok" : ("no pudo: " + (invocation.Error ?? "sin detalle")));

        if (!invocation.Ok)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent,
                invocation.Error ?? "El agente no pudo resolver el paso.",
                cancellationToken);
        }

        // ---- Fase 3: decidir y persistir (transaccion corta) ----

        // ADR-0091: el agente pidio una llamada para conseguir un dato. Se coloca (asincrona) y el paso queda
        // EN ESPERA; el webhook de Retell lo reanudara con el resultado. No se toca el resto de la decision.
        if (invocation.CallRequest is not null)
        {
            return await PauseForCallAsync(step, nodeAgent, context, invocation.CallRequest, cancellationToken);
        }

        // ADR-0092: el agente pidio preguntar por WhatsApp. Se envia (asincrono) y el paso queda EN ESPERA de la
        // respuesta; la ingesta de chat lo reanudara. Mismo trato que la llamada.
        if (invocation.WhatsAppRequest is not null)
        {
            return await PauseForWhatsAppAsync(step, nodeAgent, context, invocation.WhatsAppRequest, cancellationToken);
        }

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
                    step, nodeAgent, "El agente no eligio una ruta para la compuerta.", cancellationToken);
            }
            var (targetId, targetName) = await ResolveRouteTargetAsync(step.NodeId, invocation.Route!, cancellationToken);
            if (targetId is null)
            {
                return await ReturnToPersonAsync(
                    step, nodeAgent,
                    $"El agente eligio una ruta ('{invocation.Route}') que no es una salida de esta compuerta.",
                    cancellationToken);
            }
            routeTargetId = targetId;
            routeLabel = targetName ?? invocation.Route;
        }
        else if (!isForm && string.IsNullOrWhiteSpace(invocation.Result))
        {
            return await ReturnToPersonAsync(
                step, nodeAgent, "El agente no indico un resultado para el paso.", cancellationToken);
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
                    step, nodeAgent,
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

    /// <summary>ADR-0091: el agente pidio una llamada para conseguir un dato. Se coloca via Retell (agente de
    /// voz del nodo; objetivo LlenarFormulario; whitelist = el formulario del paso) y el paso queda EN ESPERA
    /// (PendingVoiceCallId). El webhook de Retell, al analizar la llamada, limpiara AgentAttemptedAt para que
    /// el barrido re-corra al agente con el resultado en el contexto. Si no se puede colocar -> vuelve a humano.</summary>
    private async Task<WorkflowAgentStepOutcome> PauseForCallAsync(
        WorkflowStepHistory step, WorkflowNodeAgent nodeAgent, WorkflowAgentContextDto context,
        WorkflowAgentCallRequest callRequest, CancellationToken cancellationToken)
    {
        if (context.Assignment?.VoiceAiAgentId is not Guid voiceAgentId)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent, "El paso no tiene un agente de voz configurado para llamar.", cancellationToken);
        }

        var formIds = context.Node.Form is { } f ? new[] { f.DefinitionId } : Array.Empty<Guid>();
        var placed = await _voice.PlaceCallAsync(new Voice.VoicePlaceCallRequest(
            ToNumber: callRequest.Numero,
            AiAgentId: voiceAgentId,
            PromptExtra: callRequest.Objetivo,
            Objetivo: nameof(ContactCallObjetivo.LlenarFormulario),
            FormulariosPermitidos: formIds,
            ContactVariables: new Dictionary<string, string?>()), cancellationToken);

        if (!placed.Placed || string.IsNullOrWhiteSpace(placed.CallId))
        {
            return await ReturnToPersonAsync(
                step, nodeAgent, $"No se pudo colocar la llamada: {placed.Error}", cancellationToken);
        }

        // PAUSA: el paso sigue vigente y Pending, pero marcado como en espera de esta llamada. AgentAttemptedAt
        // evita que el barrido lo re-tome hasta que el webhook lo reanude (limpia AgentAttemptedAt).
        step.AgentAttemptedAt = _clock.GetUtcNow();
        step.PendingVoiceCallId = placed.CallId;
        step.ExecutedByAiAgentId = null;   // todavia no ejecuto: esta esperando el dato
        step.AgentProposalComment = Clip(callRequest.Objetivo, 2000);
        // Fecha limite (B): si la llamada nunca se resuelve, el reaper cerrara el paso pasada esta hora.
        step.AgentDeadlineAt = _clock.GetUtcNow().AddHours(WaitTimeoutHours);

        await using var transaction = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        await AddTaskNoteAsync(step,
            $"el agente solicito una llamada para conseguir un dato ({callRequest.Objetivo}); el paso espera el resultado",
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "El agente {AgentId} solicito una llamada ({CallId}) en el paso {StepId}; queda en espera.",
            nodeAgent.AiAgentId, placed.CallId, step.Id);
        return WorkflowAgentStepOutcome.WaitingForCall;
    }

    /// <summary>ADR-0092: el agente pidio preguntar por WhatsApp. Se envia el mensaje (plantilla si es contacto
    /// en frio, texto libre si la ventana de 24h esta abierta) por la linea del nodo y el paso queda EN ESPERA
    /// (PendingWhatsAppConversationId). Al entrar la respuesta, ChatIngestService limpia AgentAttemptedAt para
    /// que el barrido re-corra al agente con la respuesta en el contexto. Con tope de preguntas por conversacion
    /// para acotar el costo; si no se puede enviar o se supera el tope -> vuelve a una persona.</summary>
    private async Task<WorkflowAgentStepOutcome> PauseForWhatsAppAsync(
        WorkflowStepHistory step, WorkflowNodeAgent nodeAgent, WorkflowAgentContextDto context,
        WorkflowAgentWhatsAppRequest request, CancellationToken cancellationToken)
    {
        if (context.Assignment?.WhatsAppLineId is not Guid lineId)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent, "El paso no tiene una linea de WhatsApp configurada para preguntar.", cancellationToken);
        }

        // Tope de reintentos: si ya se venia preguntando en una conversacion y hay demasiados salientes, se corta.
        if (step.PendingWhatsAppConversationId is Guid ongoing)
        {
            var asked = await _db.Messages.AsNoTracking()
                .CountAsync(m => m.ConversationId == ongoing && m.Direction == Domain.Enums.MessageDirection.Outbound, cancellationToken);
            if (asked >= MaxWhatsAppAsks)
            {
                return await ReturnToPersonAsync(
                    step, nodeAgent,
                    $"El agente pregunto por WhatsApp {asked} veces sin conseguir el dato; el paso queda para atencion humana.",
                    cancellationToken);
            }
        }

        var sent = await _whatsApp.AskAsync(new WhatsAppAskCommand(
            step.TenantId, lineId, request.Numero, request.Pregunta,
            context.Assignment.WhatsAppTemplateName, context.Assignment.WhatsAppTemplateLang), cancellationToken);

        if (!sent.Sent || sent.ConversationId is not Guid conversationId)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent, $"No se pudo enviar el WhatsApp: {sent.Error}", cancellationToken);
        }

        // PAUSA: el paso sigue vigente y Pending, marcado como en espera de esta conversacion. AgentAttemptedAt
        // evita que el barrido lo re-tome hasta que la ingesta de chat lo reanude (limpia AgentAttemptedAt).
        step.AgentAttemptedAt = _clock.GetUtcNow();
        step.PendingWhatsAppConversationId = conversationId;
        step.ExecutedByAiAgentId = null;   // todavia no ejecuto: esta esperando el dato
        step.AgentProposalComment = Clip(request.Pregunta, 2000);
        // Fecha limite (B): si la respuesta nunca llega, el reaper cerrara el paso pasada esta hora.
        step.AgentDeadlineAt = _clock.GetUtcNow().AddHours(WaitTimeoutHours);

        await using var transaction = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        await AddTaskNoteAsync(step,
            $"el agente pregunto por WhatsApp para conseguir un dato ('{Clip(request.Pregunta, 200)}'); el paso espera la respuesta",
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "El agente {AgentId} pregunto por WhatsApp (conv {ConversationId}) en el paso {StepId}; queda en espera.",
            nodeAgent.AiAgentId, conversationId, step.Id);
        return WorkflowAgentStepOutcome.WaitingForReply;
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
        // Si veniamos de una llamada o un WhatsApp (reanudacion), ya se uso su resultado: el paso deja de esperarlos.
        step.PendingVoiceCallId = null;
        step.PendingWhatsAppConversationId = null;

        var taskId = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == step.InstanceId).Select(i => i.TaskItemId).FirstOrDefaultAsync(cancellationToken);
        if (taskId is not Guid tid)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent,
                "El paso no esta asociado a una tarea: el agente no puede diligenciar su formulario.", cancellationToken);
        }

        // Materializa (idempotente) el draft + link Pending del paso y toma el del nodo actual.
        var stepForms = await _forms.GetTaskStepFormsAsync(tid, cancellationToken);
        var target = stepForms.FirstOrDefault(f => f.WorkflowNodeId == step.NodeId);
        if (target is null)
        {
            return await ReturnToPersonAsync(
                step, nodeAgent, "No se encontro el formulario del paso para diligenciar.", cancellationToken);
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
                step, nodeAgent,
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
        WorkflowStepHistory step, WorkflowNodeAgent nodeAgent, string reason, CancellationToken cancellationToken)
    {
        // POLITICA DE FALLO (configurable por nodo). 1) REINTENTAR: si quedan reintentos, NO se marca
        // AgentAttemptedAt -> el worker retoma el paso en el proximo ciclo. Solo se cuenta el intento;
        // el nodo sigue viendose "trabajando" (no se fija motivo de fallo hasta rendirse).
        if (nodeAgent.OnFailure == WorkflowAgentFailureAction.Retry
            && step.AgentAttemptCount < nodeAgent.FailureRetries)
        {
            step.AgentAttemptCount += 1;
            step.ExecutedByAiAgentId = null;
            step.PendingWhatsAppConversationId = null;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Agente {AgentId}: paso {StepId} no resuelto; reintento {N}/{Max}.",
                nodeAgent.AiAgentId, step.Id, step.AgentAttemptCount, nodeAgent.FailureRetries);
            return WorkflowAgentStepOutcome.ReturnedToPerson;
        }

        return await FinalizeAsFailureAsync(step, nodeAgent, reason, allowRoute: true, cancellationToken);
    }

    /// <summary>
    /// Cierra el intento del agente como FALLIDO (sin reintentar): si <paramref name="allowRoute"/> y el nodo
    /// tiene ruta de contingencia, enruta por ella; si no, devuelve el paso a una persona con el motivo. La
    /// usan tanto la ruta normal de "no pudo" como el corte manual y el timeout (que ya no deben reintentar).
    /// </summary>
    private async Task<WorkflowAgentStepOutcome> FinalizeAsFailureAsync(
        WorkflowStepHistory step, WorkflowNodeAgent nodeAgent, string reason, bool allowRoute, CancellationToken cancellationToken)
    {
        var agentId = nodeAgent.AiAgentId;

        // 2) TOMAR RUTA de contingencia: cerrar el paso con esa ruta para que el motor enrute por la rama
        // de respaldo. Si el motor no la acepta, cae a "devolver a persona" mas abajo.
        if (allowRoute
            && nodeAgent.OnFailure == WorkflowAgentFailureAction.TakeRoute
            && !string.IsNullOrWhiteSpace(nodeAgent.FailureRoute))
        {
            step.AgentAttemptedAt = _clock.GetUtcNow();
            step.AgentAttemptCount += 1;
            step.AgentFailureReason = Clip(reason, 500);
            step.AgentDeadlineAt = null;
            await _db.SaveChangesAsync(cancellationToken);
            var routed = await _engine.CompleteStepAsync(
                step.InstanceId, step.Id, executedByTenantUserId: null,
                approvalResult: nodeAgent.FailureRoute!.Trim(),
                approvalComment: $"El agente no pudo resolver; ruta de contingencia. {reason}",
                executedByAiAgentId: agentId, cancellationToken: cancellationToken);
            if (routed.IsOk)
            {
                _logger.LogInformation(
                    "Agente {AgentId}: paso {StepId} enrutado por contingencia '{Route}'.",
                    agentId, step.Id, nodeAgent.FailureRoute);
                return WorkflowAgentStepOutcome.Completed;
            }
            _logger.LogWarning(
                "Agente {AgentId}: no se pudo enrutar por contingencia '{Route}' ({Err}); vuelve a persona.",
                agentId, nodeAgent.FailureRoute, routed.Error);
        }

        // 3) DEVOLVER A PERSONA (default) y, si la politica es Notify, avisar al encargado. El paso sigue
        // Pending y vigente: nunca se pierde ni se cierra en falso. Se corta cualquier espera pendiente.
        step.AgentAttemptedAt = _clock.GetUtcNow();
        step.AgentAttemptCount += 1;
        step.ExecutedByAiAgentId = null;
        step.AgentFailureReason = Clip(reason, 500);
        step.PendingVoiceCallId = null;
        step.PendingWhatsAppConversationId = null;
        step.AgentDeadlineAt = null;

        await using var transaction = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        await AssignToPersonIfUnambiguousAsync(step, cancellationToken);
        await AddTaskNoteAsync(step, $"el agente de IA no pudo atender el paso: {reason}", cancellationToken);
        if (nodeAgent.OnFailure == WorkflowAgentFailureAction.Notify
            && step.AssignedToTenantUserId is Guid notifyUser)
        {
            _db.Notifications.Add(new Notification
            {
                TenantId = step.TenantId,
                RecipientTenantUserId = notifyUser,
                Kind = NotificationKind.General,
                Title = "Un paso necesita tu atencion",
                Body = Clip($"El agente de IA no pudo completar un paso y lo dejo para ti: {reason}", 500),
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _logger.LogInformation(
            "El agente {AgentId} devolvio el paso {StepId} a atencion humana: {Reason}", agentId, step.Id, reason);
        return WorkflowAgentStepOutcome.ReturnedToPerson;
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(Guid stepId, Guid actorTenantUserId, CancellationToken cancellationToken = default)
    {
        var step = await _db.WorkflowStepHistories.FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null || !step.IsCurrent || step.Status != WorkflowStepStatus.Pending)
        {
            return false;
        }
        var nodeAgent = await _db.WorkflowNodeAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.NodeId == step.NodeId, cancellationToken);
        if (nodeAgent is null)
        {
            return false;
        }
        var who = await _db.TenantUsers.AsNoTracking()
            .Where(u => u.Id == actorTenantUserId).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
        var reason = string.IsNullOrWhiteSpace(who)
            ? "Terminado manualmente; el paso vuelve a atencion humana."
            : $"Terminado manualmente por {who}; el paso vuelve a atencion humana.";
        // Corte manual: NO sigue la ruta de contingencia (la persona esta retomando el control).
        AppendRun(step, step.AgentAttemptCount + 1, 0, Array.Empty<string>(), "cancelado por una persona");
        await FinalizeAsFailureAsync(step, nodeAgent, reason, allowRoute: false, cancellationToken);
        _logger.LogInformation("Paso {StepId} de agente terminado manualmente por {User}.", step.Id, actorTenantUserId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TimeoutAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var step = await _db.WorkflowStepHistories.FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null || !step.IsCurrent || step.Status != WorkflowStepStatus.Pending)
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(step.AgentFailureReason))
        {
            return false;   // ya estaba marcado como fallido
        }
        var nodeAgent = await _db.WorkflowNodeAgents.AsNoTracking()
            .FirstOrDefaultAsync(a => a.NodeId == step.NodeId, cancellationToken);
        if (nodeAgent is null)
        {
            return false;
        }
        var reason = $"Tiempo de espera agotado ({WaitTimeoutHours}h) sin resolver; el paso se cierra automaticamente.";
        AppendRun(step, step.AgentAttemptCount + 1, 0, Array.Empty<string>(), "tiempo de espera agotado");
        await FinalizeAsFailureAsync(step, nodeAgent, reason, allowRoute: true, cancellationToken);
        _logger.LogInformation("Paso {StepId} de agente cerrado por timeout ({Hours}h).", step.Id, WaitTimeoutHours);
        return true;
    }

    /// <summary>Anexa una entrada al log legible del agente en el paso RASTREADO (se persiste con el SaveChanges
    /// del camino que sigue). No abre transaccion ni guarda por si mismo.</summary>
    private void AppendRun(WorkflowStepHistory step, int attempt, long tokens, IReadOnlyList<string> rounds, string outcome)
        => step.AgentRunLog = WorkflowAgentRunLog.Append(step.AgentRunLog,
            new WorkflowAgentRunLogEntry(_clock.GetUtcNow(), attempt, tokens, outcome, rounds.ToArray()));

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

    /// <summary>Capa 2 (ADR-0091): transmite un latido de progreso del agente SIN esperar (fire-and-forget) y
    /// tragandose cualquier error: el progreso en vivo es decorativo y jamas debe frenar ni tumbar la corrida
    /// del agente. Usa el broadcaster (IHubContext propio), no el _db, asi que corre en paralelo al bucle.</summary>
    private void ReportProgress(Guid tenantId, Guid taskId, Guid nodeId, string phase, long tokens)
        => _ = SafeProgressAsync(tenantId, taskId, nodeId, phase, tokens);

    private async Task SafeProgressAsync(Guid tenantId, Guid taskId, Guid nodeId, string phase, long tokens)
    {
        try { await _progress.AgentProgressAsync(tenantId, taskId, nodeId, phase, tokens); }
        catch { /* best-effort: un fallo del stream no afecta al paso */ }
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
