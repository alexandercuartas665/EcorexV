using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Sede (local/sucursal) del salon. Entidad TENANT-SCOPED. Cada asesor/estacion (Resource) puede
/// vincularse a una sede para saber donde atiende.
/// </summary>
public class Sede : TenantEntity
{
    public string Name { get; set; } = null!;
    public string City { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}
