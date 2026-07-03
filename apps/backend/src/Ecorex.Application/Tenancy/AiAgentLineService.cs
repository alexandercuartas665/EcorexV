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

    /// <summary>Vacia TODA la bitacora del tenant Y el cache de datos capturados por los agentes (deja al
    /// agente en cero). No toca mensajes del chat ni leads. Devuelve (logs, cache) borrados.</summary>
    Task<(int Logs, int Cache)> ClearAllLogsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reinicia la memoria de UNA conversacion: borra sus logs de bitacora, su cache de datos
    /// (sessionId = conversationId) y los mensajes del chat; resetea LastMessageAt para que el agente arme
    /// un contexto vacio en el siguiente mensaje. No toca el lead. Devuelve (logs, cache, mensajes).</summary>
    Task<(int Logs, int Cache, int Messages)> ResetConversationMemoryAsync(Guid conversationId, CancellationToken cancellationToken = default);
}

public sealed class AiAgentLineService : IAiAgentLineService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public AiAgentLineService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
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
}
