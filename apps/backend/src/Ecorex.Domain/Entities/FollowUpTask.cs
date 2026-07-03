using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Tarea de seguimiento/recordatorio asociada a un lead (modulo 2.5). Entidad TENANT-SCOPED.
/// Las alertas por inactividad automaticas (workers) se integraran en una fase posterior.
/// </summary>
public class FollowUpTask : TenantEntity
{
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }
    public string Title { get; set; } = null!;
    public string? Notes { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public FollowUpTaskStatus Status { get; set; } = FollowUpTaskStatus.Pending;
    public Guid? AssignedToTenantUserId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
