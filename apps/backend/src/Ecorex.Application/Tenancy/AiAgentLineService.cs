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

/// <summary>Ola 4 (ADR-0190, opcion A): CONTACTO del riel de la bitacora, agrupando WhatsApp y voz por telefono.
/// <paramref name="ContactKey"/> es la clave de agrupacion (ultimos 10 digitos del telefono); <paramref name="ConversationId"/>
/// es la conversacion de WhatsApp de respaldo (si existe) para los botones de reiniciar memoria/liberar; null si el
/// contacto solo tuvo voz. <paramref name="HasVoice"/> indica que hay al menos una llamada de voz en el historial.</summary>
public sealed record AgentContactDto(
    string ContactKey, string? ContactName, string ContactPhone, Guid? ConversationId, string? LineLabel,
    DateTimeOffset? LastActivityAt, int Events, bool HasVoice);

/// <summary>Ola 4 (ADR-0190): grabacion de una llamada de voz para la pestana "Grabacion de llamadas"
/// (reproductor + fecha + duracion + estado), desde <see cref="Ecorex.Domain.Entities.VoiceCall.RecordingUrl"/>.</summary>
public sealed record VoiceRecordingDto(
    Guid VoiceCallId, string ContactPhone, DateTimeOffset At, int? DurationSeconds, string Status, string RecordingUrl, string? Objetivo);

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

/// <summary>ENTREGA de un saliente de WhatsApp que SI salio (DeliveryStatus "sent"/"delivered"/"read"). Para la
/// pestana "Entregados" de la bitacora: ver que mensajes del flujo (y de SARA) llegaron, no solo los que fallaron.
/// DeliveryStatus = ultimo estado reportado por Meta via webhook.</summary>
public sealed record AgentDeliveryDto(
    Guid ConversationId, string? ContactName, string ContactPhone, string? LineLabel,
    DateTimeOffset SentAt, string? Body, string DeliveryStatus, string? SentByName);

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

    /// <summary>Ola 4 (ADR-0190, opcion A): riel de la bitacora agrupado por CONTACTO (telefono), uniendo las
    /// entradas de WhatsApp (por conversacion) y las de VOZ (por llamada). Mas recientes primero. Tenant-scoped.
    /// Filtros opcionales (pagina de la bitacora): <paramref name="day"/> = solo actividad de ese dia en la zona del tenant
    /// (<paramref name="zone"/>; si es null se lee Tenant.TimeZoneId, fallback America/Bogota) y <paramref name="search"/> =
    /// nombre o telefono del contacto. Con filtro la pagina pasa un <paramref name="take"/> holgado.</summary>
    Task<IReadOnlyList<AgentContactDto>> ListAttendedContactsAsync(int take = 50, DateOnly? day = null, string? search = null,
        TimeZoneInfo? zone = null, CancellationToken cancellationToken = default);

    /// <summary>Ola 4: bitacora COMPLETA de un contacto (por su clave de telefono), intercalando WhatsApp y voz,
    /// ordenada cronologicamente. Tenant-scoped.</summary>
    Task<IReadOnlyList<AgentRunLogEntryDto>> GetContactLogAsync(string contactKey, CancellationToken cancellationToken = default);

    /// <summary>Ola 4: grabaciones de llamadas de voz del tenant (las que tienen RecordingUrl), mas recientes
    /// primero, para la pestana "Grabacion de llamadas". Tenant-scoped.</summary>
    Task<IReadOnlyList<VoiceRecordingDto>> ListVoiceRecordingsAsync(int take = 100, CancellationToken cancellationToken = default);

    /// <summary>ADR-0120: conversaciones "tomadas por el flujo" ahora mismo (un paso vigente espera respuesta por
    /// WhatsApp). Es la lista visible de "esta linea atiende el flujo de la tarea X"; se vacia sola cuando el
    /// agente del nodo resuelve la salida (se limpia PendingWhatsAppConversationId).</summary>
    Task<IReadOnlyList<FlowHeldConversationDto>> ListFlowHeldConversationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Fallos de ENTREGA de salientes de WhatsApp (DeliveryStatus "failed"), mas recientes primero.
    /// Tenant-scoped (filtro global). Para ver en la app por que "no llego" un mensaje sin entrar al servidor
    /// (ADR-0135: codigo + motivo real de Meta en DeliveryError).</summary>
    Task<IReadOnlyList<AgentDeliveryFailureDto>> ListDeliveryFailuresAsync(int take = 100, CancellationToken cancellationToken = default);

    /// <summary>ENTREGAS de salientes de WhatsApp que SI salieron (DeliveryStatus "sent"/"delivered"/"read"), mas
    /// recientes primero. Tenant-scoped. Para ver en la bitacora que mensajes del flujo (y de SARA) llegaron al
    /// cliente, no solo los fallos. El estado se refresca con el webhook de YCloud (sent -> delivered -> read).</summary>
    Task<IReadOnlyList<AgentDeliveryDto>> ListDeliveriesAsync(int take = 100, CancellationToken cancellationToken = default);

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
        // Conversaciones de WhatsApp que tienen actividad de bitacora, mas recientes primero. (Esta vista legacy
        // la consume el API de agentes/MCP; es solo WhatsApp. El riel humano usa ListAttendedContactsAsync, que
        // ademas intercala voz.) Ola 4: se excluyen las entradas de voz (ConversationId null).
        var grouped = await _db.AiAgentRunLogs.AsNoTracking()
            .Where(l => l.ConversationId != null)
            .GroupBy(l => l.ConversationId!.Value)
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

    /// <summary>Clave de contacto para agrupar WhatsApp y voz: ultimos 10 digitos del telefono (empareja +57XXXXXXXXXX
    /// con el local XXXXXXXXXX). Vacio si no hay digitos.</summary>
    private static string ContactKey(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    public async Task<IReadOnlyList<AgentContactDto>> ListAttendedContactsAsync(int take = 50, DateOnly? day = null, string? search = null,
        TimeZoneInfo? zone = null, CancellationToken cancellationToken = default)
    {
        // Dia del TENANT (regla 9): [00:00, 24:00) de su zona convertido a UTC. Asi Events/Last reflejan solo ese dia y
        // solo aparecen los contactos con actividad ese dia.
        IQueryable<AiAgentRunLog> logs = _db.AiAgentRunLogs.AsNoTracking();
        if (day is DateOnly d)
        {
            if (zone is null)
            {
                string? tzId = null;
                if (_tenant.TenantId is Guid tid)
                {
                    tzId = await _db.Tenants.AsNoTracking().Where(t => t.Id == tid).Select(t => t.TimeZoneId).FirstOrDefaultAsync(cancellationToken);
                }
                zone = Scheduling.ScheduledJobRecurrence.ResolveTimeZone(tzId);
            }
            var (startUtc, endUtc) = DayWindowUtc(d, zone);
            logs = logs.Where(l => l.OccurredAt >= startUtc && l.OccurredAt < endUtc);
        }

        // Agregados de bitacora por los DOS origenes: WhatsApp (por conversacion) y voz (por llamada).
        var waGroups = await logs
            .Where(l => l.ConversationId != null)
            .GroupBy(l => l.ConversationId!.Value)
            .Select(g => new { Id = g.Key, Last = g.Max(x => x.OccurredAt), Events = g.Count() })
            .ToListAsync(cancellationToken);
        var voiceGroups = await logs
            .Where(l => l.VoiceCallId != null)
            .GroupBy(l => l.VoiceCallId!.Value)
            .Select(g => new { Id = g.Key, Last = g.Max(x => x.OccurredAt), Events = g.Count() })
            .ToListAsync(cancellationToken);
        if (waGroups.Count == 0 && voiceGroups.Count == 0) { return Array.Empty<AgentContactDto>(); }

        var convIds = waGroups.Select(g => g.Id).ToList();
        var convs = await _db.Conversations.AsNoTracking().Where(c => convIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ContactName, c.ContactPhone, c.WhatsAppLineId, c.LastMessageAt }).ToListAsync(cancellationToken);
        var callIds = voiceGroups.Select(g => g.Id).ToList();
        var calls = await _db.VoiceCalls.AsNoTracking().Where(v => callIds.Contains(v.Id))
            .Select(v => new { v.Id, v.ToNumber }).ToListAsync(cancellationToken);
        var lineLabels = await _db.WhatsAppLines.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => string.IsNullOrWhiteSpace(l.PhoneNumber) ? l.InstanceName : l.PhoneNumber!, cancellationToken);

        // Buckets por contacto (clave = telefono normalizado). Cada bucket junta WhatsApp + voz del mismo numero.
        var buckets = new Dictionary<string, AgentContactAccum>(StringComparer.Ordinal);
        AgentContactAccum Bucket(string key)
        {
            if (!buckets.TryGetValue(key, out var b)) { b = new AgentContactAccum(); buckets[key] = b; }
            return b;
        }

        foreach (var g in waGroups)
        {
            var c = convs.FirstOrDefault(x => x.Id == g.Id);
            if (c is null) { continue; }
            var key = ContactKey(c.ContactPhone);
            if (key.Length == 0) { continue; }
            var b = Bucket(key);
            b.Events += g.Events;
            if (b.Last is null || g.Last > b.Last) { b.Last = g.Last; }
            b.Phone ??= c.ContactPhone;
            if (string.IsNullOrWhiteSpace(b.Name) && !string.IsNullOrWhiteSpace(c.ContactName)) { b.Name = c.ContactName; }
            // Conversacion de respaldo = la mas reciente (para reiniciar memoria/liberar).
            if (b.ConversationId is null || (c.LastMessageAt is not null && (b.ConvLastAt is null || c.LastMessageAt > b.ConvLastAt)))
            {
                b.ConversationId = c.Id;
                b.ConvLastAt = c.LastMessageAt;
                if (c.WhatsAppLineId is Guid lid && lineLabels.TryGetValue(lid, out var lbl)) { b.LineLabel = lbl; }
            }
        }
        foreach (var g in voiceGroups)
        {
            var v = calls.FirstOrDefault(x => x.Id == g.Id);
            if (v is null) { continue; }
            var key = ContactKey(v.ToNumber);
            if (key.Length == 0) { continue; }
            var b = Bucket(key);
            b.Events += g.Events;
            if (b.Last is null || g.Last > b.Last) { b.Last = g.Last; }
            b.Phone ??= v.ToNumber;
            b.HasVoice = true;
        }

        return buckets
            .Select(kv => new AgentContactDto(
                kv.Key, kv.Value.Name, kv.Value.Phone ?? kv.Key, kv.Value.ConversationId, kv.Value.LineLabel,
                kv.Value.Last, kv.Value.Events, kv.Value.HasVoice))
            .Where(c => MatchesContact(c, search))
            .OrderByDescending(c => c.LastActivityAt)
            .Take(take)
            .ToList();
    }

    /// <summary>Ventana UTC [inicio, fin) del dia <paramref name="day"/> en la zona <paramref name="zone"/>.</summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DayWindowUtc(DateOnly day, TimeZoneInfo zone)
    {
        static DateTimeOffset Utc(DateOnly d, TimeZoneInfo z)
            => new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), z), TimeSpan.Zero);
        return (Utc(day, zone), Utc(day.AddDays(1), zone));
    }

    /// <summary>Buscador del riel: el texto en el nombre o el telefono del contacto (sin mayusculas); si trae digitos,
    /// tambien compara solo digitos (encuentra "300 123" en "+573001234567").</summary>
    public static bool MatchesContact(AgentContactDto c, string? search)
    {
        var term = (search ?? string.Empty).Trim();
        if (term.Length == 0) { return true; }
        if ((c.ContactName ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)) { return true; }
        if ((c.ContactPhone ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)) { return true; }
        var digits = new string(term.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { return false; }
        var phoneDigits = new string((c.ContactPhone ?? string.Empty).Where(char.IsDigit).ToArray());
        return phoneDigits.Contains(digits, StringComparison.Ordinal);
    }

    private sealed class AgentContactAccum
    {
        public string? Name;
        public string? Phone;
        public Guid? ConversationId;
        public DateTimeOffset? ConvLastAt;
        public string? LineLabel;
        public DateTimeOffset? Last;
        public int Events;
        public bool HasVoice;
    }

    public async Task<IReadOnlyList<AgentRunLogEntryDto>> GetContactLogAsync(string contactKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactKey)) { return Array.Empty<AgentRunLogEntryDto>(); }
        // Conversaciones y llamadas del contacto (tenant-scoped). Se normaliza el telefono en memoria para emparejar
        // formatos (+57 vs local), igual que el riel; el volumen de la bitacora de un tenant lo admite.
        var convIds = (await _db.Conversations.AsNoTracking().Select(c => new { c.Id, c.ContactPhone }).ToListAsync(cancellationToken))
            .Where(c => ContactKey(c.ContactPhone) == contactKey).Select(c => c.Id).ToList();
        var callIds = (await _db.VoiceCalls.AsNoTracking().Select(v => new { v.Id, v.ToNumber }).ToListAsync(cancellationToken))
            .Where(v => ContactKey(v.ToNumber) == contactKey).Select(v => v.Id).ToList();
        if (convIds.Count == 0 && callIds.Count == 0) { return Array.Empty<AgentRunLogEntryDto>(); }

        return await _db.AiAgentRunLogs.AsNoTracking()
            .Where(l => (l.ConversationId != null && convIds.Contains(l.ConversationId.Value))
                || (l.VoiceCallId != null && callIds.Contains(l.VoiceCallId.Value)))
            .OrderBy(l => l.OccurredAt)
            .Select(l => new AgentRunLogEntryDto(l.OccurredAt, l.Kind, l.Title, l.Content, l.Response))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VoiceRecordingDto>> ListVoiceRecordingsAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        var rows = await _db.VoiceCalls.AsNoTracking()
            .Where(v => v.RecordingUrl != null && v.RecordingUrl != "")
            .OrderByDescending(v => v.StartedAt ?? v.CreatedAt)
            .Take(take)
            .Select(v => new { v.Id, v.ToNumber, v.StartedAt, v.CreatedAt, v.DurationSeconds, v.Status, v.RecordingUrl, v.Objetivo })
            .ToListAsync(cancellationToken);
        return rows.Select(v => new VoiceRecordingDto(
            v.Id, v.ToNumber, v.StartedAt ?? v.CreatedAt, v.DurationSeconds, v.Status.ToString(), v.RecordingUrl!, v.Objetivo)).ToList();
    }

    public async Task<IReadOnlyList<FlowHeldConversationDto>> ListFlowHeldConversationsAsync(CancellationToken cancellationToken = default)
    {
        // Pasos vigentes que "poseen" una conversacion (preguntaron por WhatsApp y esperan la respuesta). El
        // filtro global por tenant aplica a todas (son TenantEntity). La liberacion es automatica: cuando el
        // agente resuelve la salida, PendingWhatsAppConversationId se limpia y la fila desaparece de esta lista.
        // Ola 4 (ADR-0190, 4.6b): se parte de la CONVERSACION tomada (FlowHoldStepId), no de PendingWhatsAppConversationId,
        // para cubrir TAMBIEN el candado de VOZ (un paso de voz estampa el hold en la conversacion del contacto pero su
        // pending es PendingVoiceCallId, no PendingWhatsAppConversationId). Asi el operador ve y puede soltar ambos.
        var rows = await (
            from c in _db.Conversations.AsNoTracking()
            where c.FlowHoldStepId != null
            join h in _db.WorkflowStepHistories.AsNoTracking() on c.FlowHoldStepId equals h.Id
            where h.IsCurrent
            join i in _db.WorkflowInstances.AsNoTracking() on h.InstanceId equals i.Id
            join t in _db.TaskItems.AsNoTracking() on i.TaskItemId equals t.Id
            join n in _db.WorkflowNodes.AsNoTracking() on h.NodeId equals n.Id
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

    public async Task<IReadOnlyList<AgentDeliveryDto>> ListDeliveriesAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        // Salientes que SI salieron (sent/delivered/read), mas recientes primero. Tenant-scoped (filtro global).
        // El flujo ahora estampa ExternalId + estado "sent" en su nota saliente, asi que el webhook la va
        // actualizando a delivered/read; SARA/chat tambien persisten su saliente -> ambos entran aqui.
        var ok = new[] { "sent", "delivered", "read" };
        var msgs = await _db.Messages.AsNoTracking()
            .Where(m => m.Direction == MessageDirection.Outbound && m.DeliveryStatus != null && ok.Contains(m.DeliveryStatus))
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .Select(m => new { m.ConversationId, m.SentAt, m.Body, m.DeliveryStatus, m.SentByName })
            .ToListAsync(cancellationToken);
        if (msgs.Count == 0) { return Array.Empty<AgentDeliveryDto>(); }

        var convIds = msgs.Select(m => m.ConversationId).Distinct().ToList();
        var convs = await _db.Conversations.AsNoTracking().Where(c => convIds.Contains(c.Id)).ToListAsync(cancellationToken);
        var lineLabels = await _db.WhatsAppLines.AsNoTracking()
            .ToDictionaryAsync(l => l.Id, l => string.IsNullOrWhiteSpace(l.PhoneNumber) ? l.InstanceName : l.PhoneNumber!, cancellationToken);

        return msgs.Select(m =>
        {
            var c = convs.FirstOrDefault(x => x.Id == m.ConversationId);
            string? lineLabel = c?.WhatsAppLineId is Guid lid && lineLabels.TryGetValue(lid, out var lbl) ? lbl : null;
            return new AgentDeliveryDto(m.ConversationId, c?.ContactName, c?.ContactPhone ?? "?", lineLabel,
                m.SentAt, m.Body, m.DeliveryStatus!, m.SentByName);
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
        // Ola 4 (ADR-0190): se libera el paso DUENO del hold de esta conversacion (FlowHoldStepId), asi cubre tanto el
        // hold de WhatsApp como el de VOZ (cuyo pending es PendingVoiceCallId). CancelAsync devuelve el paso a una
        // persona, limpia los pending y suelta el hold (ReleaseConversationHoldAsync por dueno), asi SARA retoma.
        var holderStepId = await _db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.FlowHoldStepId)
            .FirstOrDefaultAsync(cancellationToken);
        if (holderStepId is not Guid stepId) { return 0; }
        var isCurrent = await _db.WorkflowStepHistories.AsNoTracking()
            .AnyAsync(s => s.Id == stepId && s.IsCurrent, cancellationToken);
        if (!isCurrent) { return 0; }
        return await _agentRunner.CancelAsync(stepId, actor, cancellationToken) ? 1 : 0;
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
