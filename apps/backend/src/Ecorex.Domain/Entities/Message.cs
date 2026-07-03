using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Mensaje de una conversacion (modulo 2.3). Entidad TENANT-SCOPED. ExternalId (id de Evolution)
/// da idempotencia a la ingesta entrante: indice unico (TenantId, ExternalId).
/// </summary>
public class Message : TenantEntity
{
    public Guid ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public MessageDirection Direction { get; set; }
    public string? ExternalId { get; set; }
    public string Body { get; set; } = "";
    public string MessageType { get; set; } = "text";
    public DateTimeOffset SentAt { get; set; }

    /// <summary>Asesor (TenantUser) que envio el mensaje saliente. Null en entrantes (los manda el cliente).</summary>
    public Guid? SentByTenantUserId { get; set; }

    /// <summary>Nombre del asesor que respondio (snapshot al enviar). Null en entrantes.</summary>
    public string? SentByName { get; set; }

    /// <summary>Tipo de adjunto. None para mensajes de solo texto.</summary>
    public MessageMediaType MediaType { get; set; } = MessageMediaType.None;

    /// <summary>Ruta local servible del adjunto (/uploads/chat/...) o, para ubicacion, "lat,lng".</summary>
    public string? MediaUrl { get; set; }

    public string? MediaMimeType { get; set; }

    /// <summary>Emoji de reaccion sobre este mensaje (uno por mensaje, como en WhatsApp). Null = sin reaccion.</summary>
    public string? Reaction { get; set; }
}
