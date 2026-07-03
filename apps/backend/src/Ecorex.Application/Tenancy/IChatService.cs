namespace Ecorex.Application.Tenancy;

/// <summary>Lectura y envio de chat para asesores autenticados del tenant activo (modulo 2.3).</summary>
public interface IChatService
{
    Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageDto>> ListMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>Conversaciones archivadas (ocultas de la bandeja activa), mas recientes primero.</summary>
    Task<IReadOnlyList<ConversationDto>> ListArchivedConversationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Archiva (oculta) o restaura una conversacion. False si no existe en el tenant.</summary>
    Task<bool> SetConversationArchivedAsync(Guid conversationId, bool archived, CancellationToken cancellationToken = default);

    /// <summary>Ids de conversaciones cuyos mensajes contienen el texto (insensible a may/min). Vacio si term vacio.</summary>
    Task<IReadOnlyList<Guid>> SearchConversationIdsByMessageAsync(string term, CancellationToken cancellationToken = default);

    /// <summary>Elimina un mensaje saliente PARA TODOS en WhatsApp y, si tiene exito, lo borra localmente.</summary>
    Task<ChatSendResult> DeleteMessageForEveryoneAsync(Guid messageId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Vacia TODO el historial de conversaciones del tenant activo (conversaciones, mensajes, logs y
    /// cache de sesion del agente). Accion destructiva e irreversible; NO borra leads. Solo local (no toca WhatsApp).</summary>
    Task<ChatClearResult> ClearAllConversationsAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Persiste un mensaje saliente. El envio real via Evolution Connector queda diferido. Null si la conversacion no existe en el tenant.</summary>
    Task<MessageDto?> SendAsync(Guid conversationId, string body, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve (o crea) la conversacion del lead segun su telefono. Null si el lead no tiene telefono.</summary>
    Task<ConversationDto?> GetOrCreateForLeadAsync(Guid leadId, CancellationToken cancellationToken = default);

    /// <summary>Devuelve (o crea) la conversacion por telefono del contacto, sin lead. Para abrir el chat desde otros modulos.</summary>
    Task<ConversationDto?> GetOrCreateForPhoneAsync(string phone, string? contactName, CancellationToken cancellationToken = default);

    /// <summary>Conversaciones con mensajes entrantes sin responder, indexadas por telefono (solo digitos). Para colorear el pipeline.</summary>
    Task<IReadOnlyDictionary<string, LeadChatStateDto>> GetUnansweredByPhoneAsync(CancellationToken cancellationToken = default);

    /// <summary>Envia un mensaje por una linea WhatsApp (Evolution real) y lo persiste como saliente.</summary>
    Task<ChatSendResult> SendViaLineAsync(Guid conversationId, Guid lineId, string body, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia un adjunto (imagen/video/audio/documento) por una linea y lo persiste. base64 va a Evolution; localUrl se guarda para mostrar.</summary>
    Task<ChatSendResult> SendMediaViaLineAsync(Guid conversationId, Guid lineId, Domain.Enums.MessageMediaType mediaType, string base64, string localUrl, string? mimeType, string? fileName, string? caption, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Envia una ubicacion por una linea y la persiste.</summary>
    Task<ChatSendResult> SendLocationViaLineAsync(Guid conversationId, Guid lineId, double latitude, double longitude, string? name, Guid actorUserId, CancellationToken cancellationToken = default);
}
