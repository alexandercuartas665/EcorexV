namespace CubotNails.Application.Tenancy;

/// <summary>Lectura y envio de chat para asesores autenticados del tenant activo (modulo 2.3).</summary>
public interface IChatService
{
    Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Persiste un mensaje saliente. El envio real via Evolution Connector queda diferido. Null si la conversacion no existe en el tenant.</summary>
    Task<MessageDto?> SendAsync(Guid conversationId, string body, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve (o crea) la conversacion del lead segun su telefono. Null si el lead no tiene telefono.</summary>
    Task<ConversationDto?> GetOrCreateForLeadAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Conversaciones con mensajes entrantes sin responder, indexadas por telefono (solo digitos). Para colorear el pipeline.</summary>
    Task<IReadOnlyDictionary<string, LeadChatStateDto>> GetUnansweredByPhoneAsync(CancellationToken cancellationToken = default);

    /// <summary>Envia un mensaje por una linea WhatsApp (Evolution real) y lo persiste como saliente.</summary>
    Task<ChatSendResult> SendViaLineAsync(Guid conversationId, Guid lineId, string body, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia un adjunto (imagen/video/audio/documento) por una linea y lo persiste. base64 va a Evolution; localUrl se guarda para mostrar.</summary>
    Task<ChatSendResult> SendMediaViaLineAsync(Guid conversationId, Guid lineId, Domain.Enums.MessageMediaType mediaType, string base64, string localUrl, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia una ubicacion por una linea y la persiste.</summary>
    Task<ChatSendResult> SendLocationViaLineAsync(Guid conversationId, Guid lineId, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default);
}
