using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Mensaje pregrabado del tenant para el chat WhatsApp (modulo 2.3). Entidad TENANT-SCOPED.
/// Agrupado por categoria (saludo, info, cotizacion, seguimiento, cierre). Puede llevar un
/// adjunto (imagen/video/audio/documento) que se envia junto al texto.
/// </summary>
public class MessageTemplate : TenantEntity
{
    /// <summary>Clave de categoria: saludo | info | cotizacion | seguimiento | cierre (u otras del tenant).</summary>
    public string Category { get; set; } = null!;

    /// <summary>Nombre corto/etiqueta de la plantilla (se muestra en los chips y en el comando slash). Opcional.</summary>
    public string? Name { get; set; }

    /// <summary>Texto del mensaje (o pie de foto si lleva media). Admite marcadores {asesor} y {destino}.</summary>
    public string Body { get; set; } = "";

    public MessageMediaType MediaType { get; set; } = MessageMediaType.None;

    /// <summary>Ruta local servible del adjunto (/uploads/chat/...). Null si es solo texto.</summary>
    public string? MediaUrl { get; set; }

    public string? MediaMimeType { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
