using Ecorex.Application.Forms.Builder;
using Ecorex.Application.Gestor.TemplateAgent;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Infrastructure.Forms;

/// <summary>
/// Persistencia del agente editor de plantillas (ADR-0174) sobre EcorexDbContext. Mismo patron que
/// FormBuilderChatStore: lecturas AsNoTracking y escrituras que destrackean (el DbContext scoped vive todo el
/// circuito Blazor). TenantId lo sella el interceptor; el filtro global aisla por tenant.
/// </summary>
public sealed class TemplateAgentStore : ITemplateAgentStore
{
    private readonly EcorexDbContext _db;

    public TemplateAgentStore(EcorexDbContext db) => _db = db;

    public async Task<TemplateAgentConversation> CreateConversationAsync(TemplateAgentKind kind, Guid? templateId, AiProvider provider, string? model,
        string title, Guid? startedByUserId, CancellationToken cancellationToken = default)
    {
        var conv = new TemplateAgentConversation
        {
            Kind = kind,
            TemplateId = templateId,
            Provider = provider,
            Model = model,
            Title = string.IsNullOrWhiteSpace(title) ? "Plantilla" : (title.Length > 200 ? title[..200] : title),
            StartedByUserId = startedByUserId,
            Status = FormBuilderConversationStatus.Active,
        };
        _db.TemplateAgentConversations.Add(conv);
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(conv).State = EntityState.Detached;
        return conv;
    }

    public async Task<TemplateAgentConversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _db.TemplateAgentConversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task<TemplateAgentConversation?> GetLatestConversationAsync(TemplateAgentKind kind, Guid templateId, CancellationToken cancellationToken = default)
        => await _db.TemplateAgentConversations.AsNoTracking()
            .Where(c => c.Kind == kind && c.TemplateId == templateId && c.Status == FormBuilderConversationStatus.Active)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SaveConversationAsync(TemplateAgentConversation conversation, CancellationToken cancellationToken = default)
    {
        Untrack<TemplateAgentConversation>(conversation.Id);
        _db.Attach(conversation);
        _db.Entry(conversation).State = EntityState.Modified;
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(conversation).State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<TemplateAgentMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _db.TemplateAgentMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

    public async Task<TemplateAgentMessage> AddMessageAsync(TemplateAgentMessage message, CancellationToken cancellationToken = default)
    {
        _db.TemplateAgentMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(message).State = EntityState.Detached;
        return message;
    }

    public async Task SaveMessageAsync(TemplateAgentMessage message, CancellationToken cancellationToken = default)
    {
        Untrack<TemplateAgentMessage>(message.Id);
        _db.Attach(message);
        _db.Entry(message).State = EntityState.Modified;
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(message).State = EntityState.Detached;
    }

    public async Task<FormBuilderProviderInfo?> ResolveProviderAsync(AiProvider provider, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == provider, cancellationToken);
        return cfg is null ? null : new FormBuilderProviderInfo(cfg.IsEnabled, cfg.ApiKeyEncrypted, cfg.Model, cfg.BaseUrl);
    }

    public async Task<AiProvider?> GetTemplateBuilderProviderAsync(CancellationToken cancellationToken = default)
    {
        var usable = _db.AiProviderConfigs.AsNoTracking().Where(c => c.IsEnabled && c.ApiKeyEncrypted != null);
        return await usable.Where(c => c.UseForTemplateBuilder).Select(c => (AiProvider?)c.Provider).FirstOrDefaultAsync(cancellationToken)
            ?? await usable.Where(c => c.UseForFormBuilder).Select(c => (AiProvider?)c.Provider).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string> GetTenantNameAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

    private void Untrack<TEntity>(Guid id) where TEntity : Ecorex.Domain.Common.BaseEntity
    {
        var tracked = _db.ChangeTracker.Entries<TEntity>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null) { tracked.State = EntityState.Detached; }
    }
}
