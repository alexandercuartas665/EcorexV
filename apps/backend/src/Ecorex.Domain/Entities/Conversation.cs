using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Conversacion de WhatsApp con un contacto (modulo 2.3). Entidad TENANT-SCOPED.
/// Una por (TenantId, ContactPhone). Puede asociarse a un lead.
/// </summary>
public class Conversation : TenantEntity
{
    public string ContactPhone { get; set; } = null!;

    /// <summary>
    /// Jid COMPLETO del contacto en WhatsApp (key.remoteJid del webhook), con su sufijo:
    /// "@s.whatsapp.net" para numeros reales o "@lid" para contactos por LID (identificador de
    /// privacidad de WhatsApp que NO es un telefono). Se usa como destino real del envio saliente;
    /// null en conversaciones viejas (antes de esta funcion), donde se reconstruye desde ContactPhone.
    /// </summary>
    public string? RemoteJid { get; set; }

    public string? ContactName { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? WhatsAppLineId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }

    /// <summary>Cuando se archivo la conversacion (se oculta de la bandeja activa). Null = activa.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
}
