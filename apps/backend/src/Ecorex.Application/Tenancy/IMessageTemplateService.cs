namespace Ecorex.Application.Tenancy;

/// <summary>Gestion de mensajes pregrabados del tenant activo para el chat WhatsApp (modulo 2.3).</summary>
public interface IMessageTemplateService
{
    Task<IReadOnlyList<MessageTemplateDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea una plantilla. Null si no hay tenant activo.</summary>
    Task<MessageTemplateDto?> CreateAsync(CreateMessageTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza una plantilla. Null si no existe en el tenant.</summary>
    Task<MessageTemplateDto?> UpdateAsync(Guid id, UpdateMessageTemplateRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Inserta las plantillas del prototipo si el tenant aun no tiene ninguna. Devuelve cuantas creo.</summary>
    Task<int> SeedDefaultsAsync(CancellationToken cancellationToken = default);
}
