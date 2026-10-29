using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>Una linea del tenant vista desde la config de un agente: estado del binding con ESTE agente.</summary>
public sealed record AgentLineDto(
    Guid LineId,
    string InstanceName,
    string? PhoneNumber,
    WhatsAppLineStatus LineStatus,
    bool Connected,        // este agente esta atendiendo la linea
    bool AutoConfirm,      // autonomo (true) vs sugerencia (false)
    string? OtherAgentName // si otra agente atiende la linea, su nombre
);

/// <summary>Entrada de la bitacora de atencion del agente.</summary>
public sealed record AgentRunLogEntryDto(DateTimeOffset OccurredAt, AiAgentRunLogKind Kind, string Title, string? Content, string? Response);

/// <summary>Conversacion atendida por un agente (para el listado de la bitacora).</summary>
public sealed record AgentConversationDto(Guid ConversationId, string? ContactName, string ContactPhone, string? LineLabel, DateTimeOffset? LastActivityAt, int Events);

/// <summary>ADR-0120: conversacion "tomada por el flujo" (un paso vigente de una tarea espera una respuesta por
/// esta linea). Mientras exista, el agente de la linea (SARA) se calla; el flujo la libera al resolver la salida.</summary>
public sealed record FlowHeldConversationDto(
    Guid ConversationId, string? ContactName, string ContactPhone, string? LineLabel,
    string TaskNumber, string TaskTitle, string NodeName, DateTimeOffset? HeldUntil);

/// <summary>Fallo de ENTREGA de un saliente de WhatsApp (YCloud/Meta lo marco "failed": no llego). Para la
/// pestana "Fallos de envio" de la bitacora, para ver en la app POR QUE no llego sin entrar al servidor.
/// DeliveryError = codigo + motivo real de Meta (ADR-0135); null si Meta no dio detalle.</summary>
public sealed record AgentDeliveryFailureDto(
    Guid ConversationId, string? ContactName, string ContactPhone, string? LineLabel,
    DateTimeOffset SentAt, string? Body, string? DeliveryError, string? SentByName);

/// <summary>
/// Gestiona el vinculo entre agentes de IA y lineas de WhatsApp (conectar/desconectar, modo autonomo)
/// y expone la bitacora de atencion. Todo tenant-scoped.
/// </summary>
public interface IAiAgentLineService
{
    Task<IReadOnlyList<AgentLineDto>> ListLinesForAgentAsync(Guid agentId, CancellationToken cancellationToken = default);
    Task SetConnectedAsync(Guid agentId, Guid lineId, bool connected, Guid actorUserId, CancellationToken cancellationToken = default);
    Task SetAutoConfirmAsync(Guid agentId, Guid lineId, bool autoConfirm, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentConversationDto>> ListAttendedConversationsAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRunLogEntryDto>> GetConversationLogAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>ADR-0120: conversaciones "tomadas por el flujo" ahora mismo (un paso vigente espera respuesta por
    /// WhatsApp). Es la lista visible de "esta linea atiende el flujo de la tarea X"; se vacia sola cuando el
    /// agente del nodo resuelve la salida (se limpia PendingWhatsAppConversationId).</summary>
    Task<IReadOnlyList<FlowHeldConversationDto>> ListFlowHeldConversationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fallos de ENTREGA de salientes de WhatsApp (DeliveryStatus "failed"), mas recientes primero.
    /// Tenant-scoped (filtro global). Para ver en la app por que "no llego" un mensaje sin entrar al servidor
    /// (ADR-0135: codigo + motivo real de Meta en DeliveryError).</summary>
    Task<IReadOnlyList<AgentDeliveryFailureDto>> ListDeliveryFailuresAsync(int take = 100, CancellationToken cancellationToken = default);

    /// <summary>ADR-0139: para los salientes marcados "failed" SIN motivo (DeliveryError vacio), CONSULTA YCloud por su
    /// id y rellena DeliveryError con el codigo + texto real de Meta (ej. "codigo 131026 - Message undeliverable"),
    /// para que la pestana "Fallos de envio" deje de decir "sin detalle de Meta". Solo lectura contra YCloud (no
    /// reenvia nada). Devuelve cuantos motivos se actualizaron. Tenant-scoped.</summary>
    Task<int> RefreshDeliveryFailureReasonsAsync(int take = 50, CancellationToken cancellationToken = default);

    /// <summary>ADR-0120: LIBERA manualmente la(s) linea(s) que un flujo tomo para esta conversacion. Devuelve el
    /// paso a atencion humana y suelta la conversacion (limpia PendingWhatsAppConversationId), de modo que el
    /// agente de la linea (SARA) vuelve a atender. Devuelve cuantos pasos se liberaron.</summary>
    Task<int> ReleaseFlowHoldAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Vacia TODA la bitacora del tenant Y el cache de datos capturados por los agentes (deja al
    /// agente en cero). No toca mensajes del chat ni leads. Devuelve (logs, cache) borrados.</summary>
    Task<(int Logs, int Cache)> ClearAllLogsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reinicia la memoria de UNA conversacion: borra sus logs de bitacora, su cache de datos
    /// (sessionId = conversationId) y los mensajes del chat; resetea LastMessageAt para que el agente arme
    /// un contexto vacio en el siguiente mensaje. No toca el lead. Devuelve (logs, cache, mensajes).</summary>
    Task<(int Logs, int Cache, int Messages)> ResetConversationMemoryAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Reinicio NO DESTRUCTIVO de la memoria del agente para UN contacto: marca AgentContextResetAt en
    /// la conversacion, asi el agente arranca de cero en el siguiente mensaje, PERO conserva los mensajes del
    /// chat (legibles), los logs, el cache y el lead. Devuelve false si la conversacion no existe / otro tenant.</summary>
    Task<bool> ResetConversationAgentContextAsync(Guid conversationId, CancellationToken cancellationToken = default);
}

public sealed class AiAgentLineService : IAiAgentLineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;
    private readonly Workflows.IWorkflowAgentStepRunner _agentRunner;
    private readonly IWhatsAppConnectorService _connector;

    public AiAgentLineService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit, Workflows.IWorkflowAgentStepRunner agentRunner, IWhatsAppConnectorService connector)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _agentRunner = agentRunner;
        _connector = connector;
    }

    public async Task<IReadOnlyList<AgentLineDto>> ListLinesForAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var lines = await _db.WhatsAppLines.AsNoTracking().OrderBy(l => l.InstanceName).ToListAsync(cancellationToken);
        var bindings = await _db.AiAgentLineBindings.AsNoTracking().ToListAsync(cancellationToken);
        var agentNames = await _db.AiAgents.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        return lines.Select(l =>
        {
            var b = bindings.FirstOrDefault(x => x.WhatsAppLineId == l.Id);
            var mine = b is not null && b.AgentId == agentId;
            string? other = b is not null && b.AgentId != agentId && agentNames.TryGetValue(b.AgentId, out var n) ? n : null;
            return new AgentLineDto(l.Id, l.InstanceName, l.PhoneNumber, l.Status,
                mine && b!.IsConnected, mine && b!.AutoConfirm, other);
        }).ToList();
    }

    public async Task SetConnectedAsync(Guid agentId, Guid lineId, bool connected, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        var binding = await _db.AiAgentLineBindings.FirstOrDefaultAsync(b => b.WhatsAppLineId == lineId, cancellationToken);
        if (binding is null)
        {
            binding = new AiAgentLineBinding
            {
                TenantId = tenantId,
                AgentId = agentId,
                WhatsAppLineId = lineId,
                IsConnected = connected,
                AutoConfirm = false // por defecto modo sugerencia
            };
            _db.AiAgentLineBindings.Add(binding);
        }
        else
        {
            // Conectar a este agente reasigna la linea (una linea = a lo sumo un agente).
            binding.AgentId = agentId;
            binding.IsConnected = connected;
        }
        _audit.Write(actorUserId, connected ? "agent.line.connect" : "agent.line.disconnect", nameof(AiAgentLineBinding), binding.Id, null, new { agentId, lineId }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetAutoConfirmAsync(Guid agentId, Guid lineId, bool autoConfirm, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        var binding = await _db.AiAgentLineBindings.FirstOrDefaultAsync(b => b.WhatsAppLineId == lineId && b.AgentId == agentId, cancellationToken);
        if (binding is null) { return; }
        binding.AutoConfirm = autoConfirm;
        _audit.Write(actorUserId, "agent.line.autoconfirm", nameof(AiAgentLineBinding), binding.Id, null, new { autoConfirm }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentConversationDto>> ListAttendedConversationsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        // Conversaciones que tienen actividad de bitacora, mas recientes primero.
        var grouped = await _db.AiAgentRunLogs.AsNoTracking()
            .GroupBy(l => l.ConversationId)
            .Select(g => new { ConversationId = g.Key, Last = g.Max(x => x.OccurredAt), Events = g.Count() })
            .OrderByDescending(g => g.Last)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (grouped.Count == 0) { return Array.Empty<AgentConversationDto>(); }

        var convIds = grouped.Select(g => g.ConversationId).ToList();
        var convs = await _db.Conversations.AsNoTracking().Where(c => convIds.Contains(c.Id)).ToListAsync(cancellationToken);
        var lineLabels = await _db.WhatsAppLines.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => string.IsNullOrWhiteSpace(l.PhoneNumber) ? l.InstanceName : l.PhoneNumber!, cancellationToken);

        return grouped.Select(g =>
        {
            var c = convs.FirstOrDefault(x => x.Id == g.ConversationId);
            string? lineLabel = c?.WhatsAppLineId is Guid lid && lineLabels.TryGetValue(lid, out var lbl) ? lbl : null;
            return new AgentConversationDto(g.ConversationId, c?.ContactName, c?.ContactPhone ?? "?", lineLabel, g.Last, g.Events);
        }).ToList();
    }

    public async Task<IReadOnlyList<AgentRunLogEntryDto>> GetConversationLogAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _db.AiAgentRunLogs.AsNoTracking()
            .Where(l => l.ConversationId == conversationId)
            .OrderBy(l => l.OccurredAt)
            .Select(l => new AgentRunLogEntryDto(l.OccurredAt, l.Kind, l.Title, l.Content, l.Response))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FlowHeldConversationDto>> ListFlowHeldConversationsAsync(CancellationToken cancellationToken = default)
    {
        // Pasos vigentes que "poseen" una conversacion (preguntaron por WhatsApp y esperan la respuesta). El
        // filtro global por tenant aplica a todas (son TenantEntity). La liberacion es automatica: cuando el
        // agente resuelve la salida, PendingWhatsAppConversationId se limpia y la fila desaparece de esta lista.
        var rows = await (
            from h in _db.WorkflowStepHistories.AsNoTracking()
            where h.IsCurrent && h.PendingWhatsAppConversationId != null
            join i in _db.WorkflowInstances.AsNoTracking() on h.InstanceId equals i.Id
            join t in _db.TaskItems.AsNoTracking() on i.TaskItemId equals t.Id
            join n in _db.WorkflowNodes.AsNoTracking() on h.NodeId equals n.Id
            join c in _db.Conversations.AsNoTracking() on h.PendingWhatsAppConversationId equals c.Id
            select new
            {
                c.Id,
                c.ContactName,
                c.ContactPhone,
                c.WhatsAppLineId,
                t.Number,
                t.Title,
                NodeName = n.Name,
                h.AgentDeadlineAt
            }).ToListAsync(cancellationToken);
        if (rows.Count == 0) { return Array.Empty<FlowHeldConversationDto>(); }

        var lineLabels = await _db.WhatsAppLines.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => string.IsNullOrWhiteSpace(l.PhoneNumber) ? l.InstanceName : l.PhoneNumber!, cancellationToken);

        return rows.Select(r =>
        {
            string? lineLabel = r.WhatsAppLineId is Guid lid && lineLabels.TryGetValue(lid, out var lbl) ? lbl : null;
            return new FlowHeldConversationDto(
                r.Id, r.ContactName, r.ContactPhone ?? "?", lineLabel,
                r.Number, r.Title ?? "", r.NodeName ?? "", r.AgentDeadlineAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<AgentDeliveryFailureDto>> ListDeliveryFailuresAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        // Salientes que YCloud/Meta marco "failed" (no llegaron), mas recientes primero. Tenant-scoped (filtro
        // global). El motivo real (codigo + texto de Meta) vive en DeliveryError (ADR-0135).
        var msgs = await _db.Messages.AsNoTracking()
            .Where(m => m.Direction == MessageDirection.Outbound && m.DeliveryStatus == "failed")
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .Select(m => new { m.ConversationId, m.SentAt, m.Body, m.DeliveryError, m.SentByName })
            .ToListAsync(cancellationToken);
        if (msgs.Count == 0) { return Array.Empty<AgentDeliveryFailureDto>(); }

        var convIds = msgs.Select(m => m.ConversationId).Distinct().ToList();
        var convs = await _db.Conversations.AsNoTracking().Where(c => convIds.Contains(c.Id)).ToListAsync(cancellationToken);
        var lineLabels = await _db.WhatsAppLines.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => string.IsNullOrWhiteSpace(l.PhoneNumber) ? l.InstanceName : l.PhoneNumber!, cancellationToken);

        return msgs.Select(m =>
        {
            var c = convs.FirstOrDefault(x => x.Id == m.ConversationId);
            string? lineLabel = c?.WhatsAppLineId is Guid lid && lineLabels.TryGetValue(lid, out var lbl) ? lbl : null;
            return new AgentDeliveryFailureDto(m.ConversationId, c?.ContactName, c?.ContactPhone ?? "?", lineLabel,
                m.SentAt, m.Body, m.DeliveryError, m.SentByName);
        }).ToList();
    }

    public async Task<int> RefreshDeliveryFailureReasonsAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        // Salientes "failed" SIN motivo y con id de proveedor, mas recientes primero. TRACKED: se actualiza DeliveryError.
        var failed = await _db.Messages
            .Where(m => m.Direction == MessageDirection.Outbound && m.DeliveryStatus == "failed"
                && (m.DeliveryError == null || m.DeliveryError == "") && m.ExternalId != null)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (failed.Count == 0) { return 0; }

        // Linea de cada mensaje (via su conversacion) para resolver la API key YCloud al consultar.
        var convIds = failed.Select(m => m.ConversationId).Distinct().ToList();
        var convLine = await _db.Conversations.AsNoTracking()
            .Where(c => convIds.Contains(c.Id))
            .Select(c => new { c.Id, c.WhatsAppLineId })
            .ToDictionaryAsync(c => c.Id, c => c.WhatsAppLineId, cancellationToken);

        var updated = 0;
        foreach (var m in failed)
        {
            if (cancellationToken.IsCancellationRequested) { break; }
            if (!convLine.TryGetValue(m.ConversationId, out var lineIdN) || lineIdN is not Guid lineId) { continue; }
            var probe = await _connector.ProbeDeliveryStatusAsync(lineId, m.ExternalId!, cancellationToken);
            if (!probe.Ok) { continue; }   // linea no YCloud, sin key o no se pudo consultar: se deja igual
            var reason = FormatDeliveryReason(probe.ErrorCode, probe.ErrorMessage);
            if (reason is null) { continue; }   // YCloud tampoco trajo detalle: nada que rellenar
            m.DeliveryError = reason;            // solo se enriquece el MOTIVO; el estado sigue "failed"
            updated++;
        }
        if (updated > 0) { await _db.SaveChangesAsync(cancellationToken); }
        return updated;
    }

    // Mismo formato que el webhook de estado (Program.cs): "codigo X - texto" / "codigo X" / "texto"; null si no hay nada.
    private static string? FormatDeliveryReason(string? code, string? message)
        => !string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(message) ? $"codigo {code} - {message}"
            : !string.IsNullOrWhiteSpace(code) ? $"codigo {code}"
            : !string.IsNullOrWhiteSpace(message) ? message
            : null;

    public async Task<int> ReleaseFlowHoldAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (_tenant.UserId is not Guid actor) { return 0; }
        // Pasos vigentes que poseen esta conversacion. CancelAsync devuelve el paso a una persona y limpia los
        // pending (misma via que el "Terminar" del agente), asi que la linea queda libre y SARA retoma.
        var stepIds = await _db.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.IsCurrent && s.PendingWhatsAppConversationId == conversationId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var released = 0;
        foreach (var stepId in stepIds)
        {
            if (await _agentRunner.CancelAsync(stepId, actor, cancellationToken)) { released++; }
        }
        return released;
    }

    public async Task<(int Logs, int Cache)> ClearAllLogsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (0, 0); }
        // El filtro global por tenant aplica a ambas (son TenantEntity); ExecuteDelete corre el DELETE en BD.
        // Se borra TAMBIEN el cache de datos capturados: "limpiar historial" debe dejar al agente en cero
        // (si no, el cache quedaba huerfano). No se tocan mensajes del chat ni leads.
        var logs = await _db.AiAgentRunLogs.ExecuteDeleteAsync(cancellationToken);
        var cache = await _db.AiAgentCacheValues.ExecuteDeleteAsync(cancellationToken);
        _audit.Write(_tenant.UserId ?? Guid.Empty, "agent.log.clear-all", nameof(AiAgentRunLog), null, null, new { logs, cache }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return (logs, cache);
    }

    public async Task<(int Logs, int Cache, int Messages)> ResetConversationMemoryAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return (0, 0, 0); }
        var logs = await _db.AiAgentRunLogs.Where(l => l.ConversationId == conversationId).ExecuteDeleteAsync(cancellationToken);
        var cache = await _db.AiAgentCacheValues.Where(v => v.SessionId == conversationId).ExecuteDeleteAsync(cancellationToken);
        var messages = await _db.Messages.Where(m => m.ConversationId == conversationId).ExecuteDeleteAsync(cancellationToken);
        // LastMessageAt apuntaba al ultimo mensaje recien borrado; lo reseteamos para que la bandeja no
        // muestre una fecha que ya no corresponde y el agente arranque de cero.
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is not null) { conv.LastMessageAt = null; }
        _audit.Write(_tenant.UserId ?? Guid.Empty, "agent.log.reset-conversation", nameof(Conversation), conversationId, null, new { logs, cache, messages }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return (logs, cache, messages);
    }

    public async Task<bool> ResetConversationAgentContextAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return false; }
        // El query filter global por tenant garantiza que solo se resuelve una conversacion del tenant activo;
        // si es cross-tenant, FirstOrDefault devuelve null (regla inviolable #1: no filtrar a mano por tenant).
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null) { return false; }
        // Reinicio NO DESTRUCTIVO: el runtime (AgentConversationService) solo considera mensajes con
        // SentAt > AgentContextResetAt, asi que el agente arranca de cero, PERO NO se borran mensajes/logs/cache
        // ni se toca el lead. El historial del chat sigue legible para el humano.
        conv.AgentContextResetAt = DateTimeOffset.UtcNow;
        _audit.Write(_tenant.UserId ?? Guid.Empty, "agent.conversation.context-reset",
            nameof(Conversation), conversationId, null, new { resetAt = conv.AgentContextResetAt }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
