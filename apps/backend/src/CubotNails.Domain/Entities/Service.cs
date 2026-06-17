using CubotNails.Domain.Common;
using CubotNails.Domain.Enums;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Servicio del catalogo del salon (manicure, pedicure, acrilicas...). Entidad TENANT-SCOPED.
/// La duracion define cuanto ocupa la cita; el precio es el precio base (un asesor de imagen
/// puede sobreescribirlo via <see cref="ResourceServiceLink"/>).
/// </summary>
public class Service : TenantEntity
{
    public string Name { get; set; } = null!;
    /// <summary>Descripcion del servicio (que es, en que consiste). Opcional.</summary>
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public string? Category { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Precio y duracion de un servicio SEGUN el largo de cabello (corto/medio/largo/muy largo). TENANT-SCOPED.
/// Es opcional: si un servicio no define tarifas por largo, se usa el precio/duracion base del servicio.
/// </summary>
public class ServicePriceTier : TenantEntity
{
    public Guid ServiceId { get; set; }
    public Service? Service { get; set; }
    public HairLength Length { get; set; }
    public decimal Price { get; set; }
    public int DurationMinutes { get; set; }
}

/// <summary>Imagen de un servicio (archivo subido a wwwroot/uploads/services). TENANT-SCOPED.</summary>
public class ServiceImage : TenantEntity
{
    public Guid ServiceId { get; set; }
    public Service? Service { get; set; }
    public string Url { get; set; } = null!;
    public string? FileName { get; set; }
    public int SortOrder { get; set; }
}
