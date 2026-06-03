using CubotNails.Domain.Common;

namespace CubotNails.Domain.Entities;

/// <summary>
/// Servicio habilitado para un recurso, con precio personalizado opcional. Entidad TENANT-SCOPED.
/// Regla de precio: si el recurso es <c>Image</c> y existe <see cref="PriceOverride"/> != null, ese
/// precio sobreescribe el del catalogo; las estaciones (<c>Station</c>) siempre usan el precio base.
/// </summary>
public class ResourceServiceLink : TenantEntity
{
    public Guid ResourceId { get; set; }
    public Guid ServiceId { get; set; }
    public decimal? PriceOverride { get; set; }
}
