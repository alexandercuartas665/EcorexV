using Ecorex.Application.Forms.Builder;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Infrastructure.Forms;

/// <summary>
/// Persistencia de las conversaciones del asistente de creacion de formularios, sobre EcorexDbContext.
/// Lecturas AsNoTracking + escrituras Add/Update para no arrastrar estado entre llamadas. El TenantId lo
/// sella el AuditableTenantInterceptor y el filtro global de consulta garantiza el aislamiento por tenant.
/// </summary>
public sealed class FormBuilderChatStore : IFormBuilderChatStore
{
    private readonly EcorexDbContext _db;

    public FormBuilderChatStore(EcorexDbContext db) => _db = db;

    public async Task<FormBuilderConversation> CreateConversationAsync(
        Guid? formDefinitionId, AiProvider provider, string? model, string title,
        Guid? startedByTenantUserId, CancellationToken cancellationToken = default)
    {
        var conv = new FormBuilderConversation
        {
            FormDefinitionId = formDefinitionId,
            Provider = provider,
            Model = model,
            Title = string.IsNullOrWhiteSpace(title) ? "Nuevo formulario" : title,
            StartedByTenantUserId = startedByTenantUserId,
            Status = FormBuilderConversationStatus.Active,
        };
        _db.FormBuilderConversations.Add(conv);
        await _db.SaveChangesAsync(cancellationToken);
        Detach(conv);
        return conv;
    }

    public async Task<FormBuilderConversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _db.FormBuilderConversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task SaveConversationAsync(FormBuilderConversation conversation, CancellationToken cancellationToken = default)
    {
        DetachTrackedById<FormBuilderConversation>(conversation.Id);
        _db.Attach(conversation);
        _db.Entry(conversation).State = EntityState.Modified;
        await _db.SaveChangesAsync(cancellationToken);
        Detach(conversation);
    }

    public async Task<IReadOnlyList<FormBuilderMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _db.FormBuilderMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

    public async Task<FormBuilderMessage> AddMessageAsync(FormBuilderMessage message, CancellationToken cancellationToken = default)
    {
        _db.FormBuilderMessages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);
        Detach(message);
        return message;
    }

    public async Task SaveMessageAsync(FormBuilderMessage message, CancellationToken cancellationToken = default)
    {
        DetachTrackedById<FormBuilderMessage>(message.Id);
        _db.Attach(message);
        _db.Entry(message).State = EntityState.Modified;
        await _db.SaveChangesAsync(cancellationToken);
        Detach(message);
    }

    // El DbContext scoped vive por TODO el circuito Blazor Server; para no acumular entidades trackeadas
    // entre turnos (y evitar "instance with the same key is already being tracked"), destrackeamos siempre.
    private void Detach<TEntity>(TEntity entity) where TEntity : class => _db.Entry(entity).State = EntityState.Detached;

    private void DetachTrackedById<TEntity>(Guid id) where TEntity : Ecorex.Domain.Common.BaseEntity
    {
        var tracked = _db.ChangeTracker.Entries<TEntity>().FirstOrDefault(e => e.Entity.Id == id);
        if (tracked is not null) { tracked.State = EntityState.Detached; }
    }

    public async Task<FormBuilderProviderInfo?> ResolveProviderAsync(AiProvider provider, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == provider, cancellationToken);
        return cfg is null ? null : new FormBuilderProviderInfo(cfg.IsEnabled, cfg.ApiKeyEncrypted, cfg.Model, cfg.BaseUrl);
    }

    public async Task<AiProvider?> GetFormBuilderProviderAsync(CancellationToken cancellationToken = default)
        => await _db.AiProviderConfigs.AsNoTracking()
            .Where(c => c.UseForFormBuilder && c.IsEnabled && c.ApiKeyEncrypted != null)
            .Select(c => (AiProvider?)c.Provider)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<string?> GetFormTitleAsync(Guid formDefinitionId, CancellationToken cancellationToken = default)
        => await _db.FormDefinitions.AsNoTracking().Where(d => d.Id == formDefinitionId).Select(d => d.Title).FirstOrDefaultAsync(cancellationToken);

    public async Task<string> GetTenantNameAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => await _db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
}
