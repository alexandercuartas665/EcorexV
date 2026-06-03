using CubotNails.Domain.Common;
using CubotNails.Domain.Enums;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Bloqueo de agenda por asesor o GLOBAL del salon (Modulo 2.4). Entidad TENANT-SCOPED.
/// <see cref="ResourceId"/> es obligatorio si <see cref="Scope"/> es <c>Resource</c> y null si es
/// <c>Global</c>. Si <see cref="StartTime"/>/<see cref="EndTime"/> son null, bloquea el dia completo.
/// </summary>
public class ScheduleException : TenantEntity
{
    public ExceptionScope Scope { get; set; } = ExceptionScope.Global;
    public Guid? ResourceId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public ExceptionReason Reason { get; set; } = ExceptionReason.Closed;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public string? Note { get; set; }
}
