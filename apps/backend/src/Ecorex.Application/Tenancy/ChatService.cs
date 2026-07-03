using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class ChatService : IChatService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IWhatsAppConnectorService _connector;
    private readonly IChatBroadcaster _broadcaster;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditWriter _audit;

    public ChatService(IApplicationDbContext db, ITenantContext tenantContext, IWhatsAppConnectorService connector, IChatBroadcaster broadcaster, TimeProvider timeProvider, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _connector = connector;
        _broadcaster = broadcaster;
        _timeProvider = timeProvider;
        _audit = audit;
    }

    public async Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Conversations
            .AsNoTracking()
            // Solo activas y CON al menos un mensaje. last_message_at es NULL exactamente cuando la
            // conversacion nunca tuvo mensaje (la crea GetOrCreateFor*Async al abrir el chat desde otro
            // modulo); esas "vacias" no se muestran y reaparecen solas al entrar/salir el primer mensaje.
            .Where(c => c.ArchivedAt == null && c.LastMessageAt != null)
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => new ConversationDto(c.Id, c.ContactPhone, c.ContactName, c.LeadId, c.LastMessageAt, c.WhatsAppLineId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationDto>> ListArchivedConversationsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Conversations
            .AsNoTracking()
            .Where(c => c.ArchivedAt != null)
            .OrderByDescending(c => c.ArchivedAt)
            .Select(c => new ConversationDto(c.Id, c.ContactPhone, c.ContactName, c.LeadId, c.LastMessageAt, c.WhatsAppLineId))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SetConversationArchivedAsync(Guid conversationId, bool archived, CancellationToken cancellationToken = default)
    {
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null) { return false; }
        conv.ArchivedAt = archived ? _timeProvider.GetUtcNow() : null;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> SearchConversationIdsByMessageAsync(string term, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term)) { return Array.Empty<Guid>(); }
        // LOWER(body) LIKE '%term%' (insensible a may/min, sin depender del proveedor Npgsql).
        var t = term.Trim().ToLower();
        return await _db.Messages
            .AsNoTracking()
            .Where(m => m.Body != null && m.Body.ToLower().Contains(t))
            .Select(m => m.ConversationId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatClearResult> ClearAllConversationsAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new ChatClearResult(0, 0); }

        // Ids de TODAS las conversaciones del tenant (activas + archivadas). El query filter global ya
        // restringe al tenant activo, asi que nunca toca otros tenants.
        var conversationIds = await _db.Conversations.Select(c => c.Id).ToListAsync(cancellationToken);
        if (conversationIds.Count == 0) { return new ChatClearResult(0, 0); }

        // Dependientes SIN FK relacional (no cascadean): logs del agente y cache de sesion. SessionId solo
        // coincide con ConversationId en el chat real; las pruebas de sandbox usan AgentId y no se tocan.
        await _db.AiAgentRunLogs.Where(l => conversationIds.Contains(l.ConversationId)).ExecuteDeleteAsync(cancellationToken);
        await _db.AiAgentCacheValues.Where(v => conversationIds.Contains(v.SessionId)).ExecuteDeleteAsync(cancellationToken);
        // Mensajes (cascadean por FK, pero los borramos explicitos por claridad/orden) y luego conversaciones.
        var messages = await _db.Messages.Where(m => conversationIds.Contains(m.ConversationId)).ExecuteDeleteAsync(cancellationToken);
        var conversations = await _db.Conversations.Where(c => conversationIds.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);

        // No se borran leads: la conversacion apunta al lead, no al reves.
        _audit.Write(actorUserId, "chat.clear-all", nameof(Conversation), null, null, new { conversations, messages }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new ChatClearResult(conversations, messages);
    }

    public async Task<ChatSendResult> DeleteMessageForEveryoneAsync(Guid messageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var msg = await _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (msg is null) { return new ChatSendResult(false, null, "El mensaje no existe."); }
        // Solo se pueden eliminar PARA TODOS los mensajes propios (salientes).
        if (msg.Direction != MessageDirection.Outbound)
        {
            return new ChatSendResult(false, null, "Solo puedes eliminar para todos los mensajes que tu enviaste.");
        }
        // Mensajes antiguos (enviados antes de esta funcion) no guardaron el id de WhatsApp: no se pueden borrar para todos.
        if (string.IsNullOrWhiteSpace(msg.ExternalId))
        {
            return new ChatSendResult(false, null, "Este mensaje no se puede eliminar para todos (es anterior a esta funcion o no tiene id de WhatsApp).");
        }

        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == msg.ConversationId, cancellationToken);
        if (conv is null) { return new ChatSendResult(false, null, "Conversacion no encontrada."); }
        if (conv.WhatsAppLineId is not Guid lineId)
        {
            return new ChatSendResult(false, null, "La conversacion no tiene una linea asociada.");
        }

        // Borrado PARA TODOS en WhatsApp (Evolution). Si falla, NO quitamos la fila local (para no
        // mentir: el mensaje seguiria visible en el telefono del cliente).
        var del = await _connector.DeleteMessageForEveryoneAsync(lineId, conv.ContactPhone, msg.ExternalId, cancellationToken);
        if (!del.Ok)
        {
            return new ChatSendResult(false, null, del.Error ?? "No se pudo eliminar en WhatsApp.");
        }

        // OK en WhatsApp: quitamos la fila local (el archivo de media queda como huerfano en /uploads, inocuo).
        _db.Messages.Remove(msg);
        conv.LastMessageAt = await _db.Messages
            .Where(m => m.ConversationId == conv.Id && m.Id != msg.Id)
            .OrderByDescending(m => m.SentAt)
            .Select(m => (DateTimeOffset?)m.SentAt)
            .FirstOrDefaultAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return new ChatSendResult(true, null, null);
    }

    public async Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await _db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.ConversationId, m.Direction, m.Body, m.MessageType, m.SentAt, m.MediaType, m.MediaUrl, m.MediaMimeType, m.SentByName, m.Reaction))
            .ToListAsync(cancellationToken);
    }

    public async Task<MessageDto?> SendAsync(Guid conversationId, string body, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var message = new Message
        {
            TenantId = conversation.TenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Body = body,
            MessageType = "text",
            SentAt = now
        };
        _db.Messages.Add(message);
        conversation.LastMessageAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        return new MessageDto(message.Id, message.ConversationId, message.Direction, message.Body, message.MessageType, message.SentAt);
    }

    public async Task<ConversationDto?> GetOrCreateForLeadAsync(Guid leadId, CancellationToken cancellationToken = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
        if (lead is null || string.IsNullOrWhiteSpace(lead.ContactPhone))
        {
            return null;
        }
        var phone = Digits(lead.ContactPhone);

        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.LeadId == leadId || c.ContactPhone == phone, cancellationToken);
        if (conv is null)
        {
            if (_tenantContext.TenantId is not Guid tenantId)
            {
                return null;
            }
            conv = new Conversation { TenantId = tenantId, ContactPhone = phone, ContactName = lead.ContactName, LeadId = leadId };
            _db.Conversations.Add(conv);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else if (conv.LeadId is null)
        {
            conv.LeadId = leadId;
            await _db.SaveChangesAsync(cancellationToken);
        }
        return new ConversationDto(conv.Id, conv.ContactPhone, conv.ContactName, conv.LeadId, conv.LastMessageAt, conv.WhatsAppLineId);
    }

    public async Task<ConversationDto?> GetOrCreateForPhoneAsync(string phone, string? contactName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone)) { return null; }
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var digits = Digits(phone);
        if (digits.Length == 0) { return null; }

        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.ContactPhone == digits, cancellationToken);
        if (conv is null)
        {
            conv = new Conversation { TenantId = tenantId, ContactPhone = digits, ContactName = contactName };
            _db.Conversations.Add(conv);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else if (string.IsNullOrWhiteSpace(conv.ContactName) && !string.IsNullOrWhiteSpace(contactName))
        {
            conv.ContactName = contactName;
            await _db.SaveChangesAsync(cancellationToken);
        }
        return new ConversationDto(conv.Id, conv.ContactPhone, conv.ContactName, conv.LeadId, conv.LastMessageAt, conv.WhatsAppLineId);
    }

    public async Task<ChatSendResult> SendViaLineAsync(Guid conversationId, Guid lineId, string body, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ChatSendResult(false, null, "El mensaje esta vacio.");
        }
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }

        // COMANDO DE TOMA DE CONTROL: si el asesor envia "Manejo_asesor", agregamos el numero a la lista
        // negra (el agente deja de responderle) y NO enviamos el comando al cliente; dejamos una nota de sistema.
        if (AgentControlCommands.IsTakeControl(body))
        {
            await AgentControlCommands.BlockNumberForLineAsync(_db, lineId, conv.ContactPhone, cancellationToken);
            var cmdNow = _timeProvider.GetUtcNow();
            var note = new Message
            {
                TenantId = conv.TenantId,
                ConversationId = conv.Id,
                Direction = MessageDirection.Outbound,
                Body = "Tomaste el control de esta conversacion. El asistente ya no respondera a este numero.",
                MessageType = "text",
                SentByName = "Sistema",
                SentAt = cmdNow
            };
            _db.Messages.Add(note);
            conv.LastMessageAt = cmdNow;
            conv.WhatsAppLineId = lineId;
            await _db.SaveChangesAsync(cancellationToken);
            var noteDto = new MessageDto(note.Id, note.ConversationId, note.Direction, note.Body, note.MessageType, note.SentAt, note.MediaType, note.MediaUrl, note.MediaMimeType, note.SentByName);
            try { await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, noteDto, cancellationToken); } catch { /* best-effort */ }
            return new ChatSendResult(true, noteDto, null);
        }

        // Envio real por la linea elegida (Evolution).
        var send = await _connector.SendTestAsync(lineId, conv.ContactPhone, body, actorUserId, cancellationToken);
        if (!send.Ok)
        {
            return new ChatSendResult(false, null, send.Error);
        }

        var sender = await ResolveSenderAsync(actorUserId, conv.TenantId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = body.Trim(),
            MessageType = "text",
            ExternalId = send.MessageId,
            SentByTenantUserId = sender.Id,
            SentByName = sender.Name,
            SentAt = now
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = now;
        conv.WhatsAppLineId = lineId;
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body, msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, msg.MediaMimeType, msg.SentByName);
        await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, dto, cancellationToken);
        return new ChatSendResult(true, dto, null);
    }

    public async Task<ChatSendResult> SendMediaViaLineAsync(Guid conversationId, Guid lineId, MessageMediaType mediaType, string base64, string localUrl, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (mediaType == MessageMediaType.None || string.IsNullOrWhiteSpace(base64))
        {
            return new ChatSendResult(false, null, "Adjunto invalido.");
        }
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }

        var send = await _connector.SendMediaAsync(lineId, conv.ContactPhone, mediaType, base64, mimeType, fileName, caption, actorUserId, cancellationToken);
        if (!send.Ok)
        {
            return new ChatSendResult(false, null, send.Error);
        }

        var sender = await ResolveSenderAsync(actorUserId, conv.TenantId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = caption?.Trim() ?? "",
            MessageType = mediaType.ToString().ToLowerInvariant(),
            MediaType = mediaType,
            MediaUrl = localUrl,
            MediaMimeType = mimeType,
            ExternalId = send.MessageId,
            SentByTenantUserId = sender.Id,
            SentByName = sender.Name,
            SentAt = now
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = now;
        conv.WhatsAppLineId = lineId;
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body, msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, msg.MediaMimeType, msg.SentByName);
        await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, dto, cancellationToken);
        return new ChatSendResult(true, dto, null);
    }

    public async Task<ChatSendResult> SendLocationViaLineAsync(Guid conversationId, Guid lineId, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conv is null)
        {
            return new ChatSendResult(false, null, "Conversacion no encontrada.");
        }

        var send = await _connector.SendLocationAsync(lineId, conv.ContactPhone, latitude, longitude, name, actorUserId, cancellationToken);
        if (!send.Ok)
        {
            return new ChatSendResult(false, null, send.Error);
        }

        var sender = await ResolveSenderAsync(actorUserId, conv.TenantId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var msg = new Message
        {
            TenantId = conv.TenantId,
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Body = string.IsNullOrWhiteSpace(name) ? "Ubicacion" : name!.Trim(),
            MessageType = "location",
            MediaType = MessageMediaType.Location,
            MediaUrl = $"{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            ExternalId = send.MessageId,
            SentByTenantUserId = sender.Id,
            SentByName = sender.Name,
            SentAt = now
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = now;
        conv.WhatsAppLineId = lineId;
        await _db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(msg.Id, msg.ConversationId, msg.Direction, msg.Body, msg.MessageType, msg.SentAt, msg.MediaType, msg.MediaUrl, null, msg.SentByName);
        await _broadcaster.MessageAddedAsync(conv.TenantId, conv.Id, dto, cancellationToken);
        return new ChatSendResult(true, dto, null);
    }

    public async Task<IReadOnlyDictionary<string, LeadChatStateDto>> GetUnansweredByPhoneAsync(CancellationToken cancellationToken = default)
    {
        var convs = await _db.Conversations.AsNoTracking()
            .Select(c => new { c.Id, c.ContactPhone })
            .ToListAsync(cancellationToken);
        if (convs.Count == 0) { return new Dictionary<string, LeadChatStateDto>(); }

        var msgs = await _db.Messages.AsNoTracking()
            .Select(m => new { m.ConversationId, m.Direction, m.SentAt })
            .ToListAsync(cancellationToken);
        var byConv = msgs.GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<string, LeadChatStateDto>();
        foreach (var c in convs)
        {
            if (!byConv.TryGetValue(c.Id, out var list)) { continue; }
            var lastOut = list.Where(m => m.Direction == MessageDirection.Outbound)
                .Select(m => (DateTimeOffset?)m.SentAt).Max();
            var unanswered = list.Where(m => m.Direction == MessageDirection.Inbound && (lastOut is null || m.SentAt > lastOut)).ToList();
            if (unanswered.Count == 0) { continue; }

            var key = Digits(c.ContactPhone);
            if (string.IsNullOrEmpty(key)) { continue; }
            result[key] = new LeadChatStateDto(unanswered.Count, unanswered.Min(m => m.SentAt));
        }
        return result;
    }

    // Resuelve el asesor (TenantUser) a partir del PlatformUser autenticado, con nombre para mostrar.
    private async Task<(Guid? Id, string? Name)> ResolveSenderAsync(Guid actorPlatformUserId, Guid tenantId, CancellationToken ct)
    {
        if (actorPlatformUserId == Guid.Empty) { return (null, null); }
        var tu = await _db.TenantUsers
            .IgnoreQueryFilters()
            .Include(u => u.PlatformUser)
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.PlatformUserId == actorPlatformUserId, ct);
        if (tu is null) { return (null, null); }
        var name = string.IsNullOrWhiteSpace(tu.PlatformUser?.DisplayName) ? tu.Email : tu.PlatformUser!.DisplayName;
        return (tu.Id, name);
    }

    private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());
}
