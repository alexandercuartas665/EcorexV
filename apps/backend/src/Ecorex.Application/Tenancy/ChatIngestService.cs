using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class ChatIngestService : IChatIngestService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IChatBroadcaster _broadcaster;
    private readonly IAgentReplyQueue _agentQueue;
    private readonly TimeProvider _timeProvider;

    public ChatIngestService(IApplicationDbContext db, ISecretProtector secretProtector, IChatBroadcaster broadcaster, IAgentReplyQueue agentQueue, TimeProvider timeProvider)
    {
        _db = db;
        _secretProtector = secretProtector;
        _broadcaster = broadcaster;
        _agentQueue = agentQueue;
        _timeProvider = timeProvider;
    }

    public async Task<ChatIngestResult> IngestAsync(Guid tenantId, string? providedToken, IngestMessageRequest payload, CancellationToken cancellationToken = default)
    {
        // Sin JWT: validamos el token del webhook contra la config Evolution del tenant.
        var config = await _db.TenantEvolutionConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null || string.IsNullOrEmpty(config.ApiTokenEncrypted)
            || string.IsNullOrEmpty(providedToken) || !TokenMatches(config.ApiTokenEncrypted, providedToken))
        {
            return ChatIngestResult.Unauthorized;
        }

        return await IngestTrustedAsync(tenantId, payload, cancellationToken: cancellationToken);
    }

    // Persiste un entrante ya autorizado por el llamador (webhook crudo de Evolution validado
    // con token global + instancia conocida). Mantiene idempotencia y difusion en tiempo real.
    public async Task<ChatIngestResult> IngestTrustedAsync(Guid tenantId, IngestMessageRequest payload, bool enqueueDispatch = true, CancellationToken cancellationToken = default)
    {
        // Idempotencia por id externo.
        var duplicate = await _db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId && m.ExternalId == payload.ExternalMessageId, cancellationToken);
        if (duplicate)
        {
            return ChatIngestResult.Duplicate;
        }

        var phone = payload.ContactPhone.Trim();
        var lineId = payload.WhatsAppLineId;
        // Conversacion por (tenant, linea, contacto): el mismo numero en dos lineas son hilos separados,
        // lo que separa la sesion de cache del agente y evita confundir clientes.
        var conversation = await _db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.WhatsAppLineId == lineId && c.ContactPhone == phone, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var sentAt = payload.SentAt ?? now;

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = tenantId,
                ContactPhone = phone,
                ContactName = payload.ContactName?.Trim(),
                WhatsAppLineId = lineId,
                LastMessageAt = sentAt
            };
            _db.Conversations.Add(conversation);
        }
        else
        {
            conversation.LastMessageAt = sentAt;
            if (conversation.WhatsAppLineId is null && lineId is not null) { conversation.WhatsAppLineId = lineId; }
            if (conversation.ContactName is null && payload.ContactName is not null)
            {
                conversation.ContactName = payload.ContactName.Trim();
            }
        }

        var message = new Message
        {
            TenantId = tenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            ExternalId = payload.ExternalMessageId,
            Body = payload.Body,
            MessageType = string.IsNullOrWhiteSpace(payload.MessageType) ? "text" : payload.MessageType!.Trim(),
            MediaType = payload.MediaType,
            MediaUrl = payload.MediaUrl,
            MediaMimeType = payload.MediaMimeType,
            SentAt = sentAt
        };
        _db.Messages.Add(message);

        await _db.SaveChangesAsync(cancellationToken);

        var dto = new MessageDto(message.Id, message.ConversationId, message.Direction, message.Body,
            message.MessageType, message.SentAt, message.MediaType, message.MediaUrl, message.MediaMimeType, message.SentByName);
        await _broadcaster.MessageAddedAsync(tenantId, conversation.Id, dto, cancellationToken);

        // Si la conversacion llego por una linea, encolamos la atencion del agente (el despachador
        // decide si hay un agente conectado a esa linea; aqui no bloqueamos el webhook).
        if (lineId is not null && enqueueDispatch)
        {
            _agentQueue.Schedule(tenantId, conversation.Id);
        }

        return ChatIngestResult.Accepted;
    }

    private bool TokenMatches(string encrypted, string provided)
    {
        string actual;
        try
        {
            actual = _secretProtector.Unprotect(encrypted);
        }
        catch
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(provided));
    }
}
