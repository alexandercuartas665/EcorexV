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

    /// <summary>Si el asesor tiene login propio (TenantUser con rol Professional).</summary>
    public Guid? LinkedTenantUserId { get; set; }
}
