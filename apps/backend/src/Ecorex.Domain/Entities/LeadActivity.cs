using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Entrada del historial de actividad de un lead (modulo 2.2). Entidad TENANT-SCOPED.
/// Sirve de base para futuros eventos de negocio (lead.created, lead.stage.changed).
/// </summary>
public class LeadActivity : TenantEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string ActivityType { get; set; } = null!;
    public string? Description { get; set; }
}
