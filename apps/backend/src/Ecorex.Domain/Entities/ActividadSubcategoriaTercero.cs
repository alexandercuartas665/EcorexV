using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Tercero/cliente aplicable de una subcategoria de actividad (000270): union M:N entre una
/// <see cref="ActividadSubcategoria"/> y un <see cref="Tercero"/> del Directorio General
/// (000232). Vive y muere con la subcategoria (Cascade). La FK al tercero es NO ACTION
/// (borrar/inactivar un tercero no toca el catalogo). TENANT-SCOPED.
/// </summary>
public class ActividadSubcategoriaTercero : TenantEntity
{
    public Guid SubcategoriaId { get; set; }
    public ActividadSubcategoria? Subcategoria { get; set; }

    /// <summary>Tercero del Directorio General (000232).</summary>
    public Guid TerceroId { get; set; }
    public Tercero? Tercero { get; set; }
}
