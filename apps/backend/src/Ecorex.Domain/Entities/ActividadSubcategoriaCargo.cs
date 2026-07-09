using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Permiso por cargo de una subcategoria de actividad (000270): union M:N entre una
/// <see cref="ActividadSubcategoria"/> y un cargo del organigrama (OrgUnit Classifier=Cargo,
/// modulo 000850). Vive y muere con la subcategoria (Cascade). La FK al cargo es NO ACTION
/// (borrar un cargo no toca el catalogo). TENANT-SCOPED.
/// </summary>
public class ActividadSubcategoriaCargo : TenantEntity
{
    public Guid SubcategoriaId { get; set; }
    public ActividadSubcategoria? Subcategoria { get; set; }

    /// <summary>Cargo del organigrama (OrgUnit con Classifier=Cargo).</summary>
    public Guid OrgUnitId { get; set; }
    public OrgUnit? OrgUnit { get; set; }
}
