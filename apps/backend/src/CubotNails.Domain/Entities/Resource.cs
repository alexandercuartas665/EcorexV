using CubotNails.Domain.Common;
using CubotNails.Domain.Enums;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Recurso agendable del salon: asesor de imagen (<see cref="ResourceKind.Image"/>) o estacion
/// (<see cref="ResourceKind.Station"/>). Entidad TENANT-SCOPED. El color pinta su columna en el
/// Dia del salon y sus citas en la vista semanal.
/// </summary>
public class Resource : TenantEntity
{
    public string Name { get; set; } = null!;
    public ResourceKind Kind { get; set; } = ResourceKind.Image;
    public string? Color { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Como se ofrece la disponibilidad de este recurso: grilla de turnos fijos (clasico) o por
    /// duracion del servicio (continuo). La defensa anti-solapamiento es la misma en ambos.
    /// </summary>
    public SchedulingMode SchedulingMode { get; set; } = SchedulingMode.SlotGrid;

    /// <summary>Minutos de margen (limpieza/preparacion) reservados despues de cada cita. Entra en el anti-solapamiento.</summary>
    public int BufferMinutes { get; set; }

    /// <summary>Si el asesor tiene login propio (TenantUser con rol Professional).</summary>
    public Guid? LinkedTenantUserId { get; set; }

    /// <summary>Sede (local) donde atiende este asesor/estacion. Opcional.</summary>
    public Guid? SedeId { get; set; }
}
