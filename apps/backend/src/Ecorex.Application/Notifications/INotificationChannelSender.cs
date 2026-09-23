namespace Ecorex.Application.Notifications;

/// <summary>Canal de una notificacion saliente. Compartido por el Cierre del agente (ADR-0099) y las
/// reglas de notificacion por nodo de flujo (ADR-0100).</summary>
public enum NotifyChannel
{
    Correo = 0,
    WhatsApp = 1,        // plantilla HSM a un telefono (YCloud)
    WhatsAppGrupo = 2,   // texto plano a un grupo de Evolution ("...@g.us")
    Telegram = 3         // texto a un chat/grupo de Telegram (bot del tenant)
}

/// <summary>
/// Envio de bajo nivel por canal (correo / WhatsApp plantilla / WhatsApp grupo / Telegram). Centraliza lo
/// comun a todos los motores de notificacion: mapeo de variables de plantilla HSM, carga del token del bot
/// de Telegram del tenant, y las llamadas a los servicios de canal. Best-effort: NUNCA lanza; devuelve
/// false si no se pudo enviar (el llamador decide si registra el fallo). No compone el cuerpo ni resuelve
/// destinatarios: eso es responsabilidad de cada motor (Cierre / nodo).
/// </summary>
public interface INotificationChannelSender
{
    Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>Plantilla HSM (YCloud) a un telefono. Los <paramref name="tokens"/> se mapean a las variables
    /// de la plantilla POR NOMBRE (segun su VariablesJson). Devuelve false si falta el telefono o la plantilla.</summary>
    Task<bool> SendWhatsAppTemplateAsync(Guid lineId, string phone, string templateName, string? language,
        IReadOnlyDictionary<string, string> tokens, Guid actorUserId,
        // Adjunto opcional (Evolution): documento que se manda EN EL MISMO mensaje que la plantilla (el cuerpo
        // va como caption), en vez de una notificacion aparte. Ej. la cotizacion por tarea.
        string? attachmentBase64 = null, string? attachmentMime = null, string? attachmentFileName = null,
        CancellationToken cancellationToken = default);

    /// <summary>Texto plano a un grupo de Evolution (jid "...@g.us") desde una linea Evolution.</summary>
    Task<bool> SendWhatsAppGroupAsync(Guid lineId, string groupJid, string text, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Adjunta un DOCUMENTO (base64) a un telefono desde la linea, con caption opcional. Lo usan las
    /// reglas de nodo para mandar el PDF de un formulario junto a la plantilla. Evolution lo envia como archivo;
    /// YCloud requiere la media por URL publica (no soportado aqui, devuelve false).</summary>
    Task<bool> SendWhatsAppDocumentAsync(Guid lineId, string phone, string base64, string? mimeType, string? fileName,
        string? caption, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Texto a un chat/grupo de Telegram usando el bot del tenant activo (token cifrado). Devuelve
    /// false si el tenant no tiene bot habilitado o el chat es vacio.</summary>
    Task<bool> SendTelegramAsync(string chatId, string text, CancellationToken cancellationToken = default);
}
