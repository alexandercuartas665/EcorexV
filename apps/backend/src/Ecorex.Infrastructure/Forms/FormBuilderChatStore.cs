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
        return conv;
    }

    public async Task<FormBuilderConversation?> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken = default)
        => await _db.FormBuilderConversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

    public async Task SaveConversationAsync(FormBuilderConversation conversation, CancellationToken cancellationToken = default)
    {
        _db.FormBuilderConversations.Update(conversation);
        await _db.SaveChangesAsync(cancellationToken);
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
        return message;
    }

    public async Task SaveMessageAsync(FormBuilderMessage message, CancellationToken cancellationToken = default)
    {
        _db.FormBuilderMessages.Update(message);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
