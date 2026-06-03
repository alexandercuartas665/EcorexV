using CubotNails.Domain.Common;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Turno base recurrente de un recurso para un dia de la semana (Modulo 2.1). Entidad TENANT-SCOPED.
/// Un recurso puede tener varios turnos por dia (manana + tarde). Cupos = floor((End-Start)/SlotMinutes).
/// </summary>
public class ShiftTemplate : TenantEntity
{
    public Guid ResourceId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int SlotMinutes { get; set; } = 30;
}
