using CubotNails.Domain.Common;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Conversacion de WhatsApp con un contacto (modulo 2.3). Entidad TENANT-SCOPED.
/// Una por (TenantId, ContactPhone). Puede asociarse a un lead.
/// </summary>
public class Conversation : TenantEntity
{
    public string ContactPhone { get; set; } = null!;
    public string? ContactName { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? WhatsAppLineId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
}
