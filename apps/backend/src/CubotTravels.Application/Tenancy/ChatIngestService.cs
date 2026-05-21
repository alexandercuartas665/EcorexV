using System.Security.Cryptography;
using System.Text;
using CubotTravels.Application.Common;
using CubotTravels.Domain.Entities;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

public sealed class ChatIngestService : IChatIngestService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly TimeProvider _timeProvider;

    public ChatIngestService(IApplicationDbContext db, ISecretProtector secretProtector, TimeProvider timeProvider)
    {
        _db = db;
        _secretProtector = secretProtector;
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

        // Idempotencia por id externo.
        var duplicate = await _db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId && m.ExternalId == payload.ExternalMessageId, cancellationToken);
        if (duplicate)
        {
            return ChatIngestResult.Duplicate;
        }

        var phone = payload.ContactPhone.Trim();
        var conversation = await _db.Conversations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ContactPhone == phone, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var sentAt = payload.SentAt ?? now;

        if (conversation is null)
        {
            conversation = new Conversation
            {
                TenantId = tenantId,
                ContactPhone = phone,
                ContactName = payload.ContactName?.Trim(),
                LastMessageAt = sentAt
            };
            _db.Conversations.Add(conversation);
        }
        else
        {
            conversation.LastMessageAt = sentAt;
            if (conversation.ContactName is null && payload.ContactName is not null)
            {
                conversation.ContactName = payload.ContactName.Trim();
            }
        }

        _db.Messages.Add(new Message
        {
            TenantId = tenantId,
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            ExternalId = payload.ExternalMessageId,
            Body = payload.Body,
            MessageType = string.IsNullOrWhiteSpace(payload.MessageType) ? "text" : payload.MessageType!.Trim(),
            SentAt = sentAt
        });

        await _db.SaveChangesAsync(cancellationToken);
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
