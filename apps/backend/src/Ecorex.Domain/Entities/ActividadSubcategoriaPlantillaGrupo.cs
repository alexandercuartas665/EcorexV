using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Grupo de plantillas de documento habilitado para una subcategoria de actividad (000270): union M:N entre
/// una <see cref="ActividadSubcategoria"/> y un <see cref="DocumentTemplateGroup"/>. Define QUE grupos de
/// plantillas puede usar la tarea al redactar un documento (pueden ser varios). Vive y muere con la
/// subcategoria (Cascade). La FK al grupo es NO ACTION (borrar/inactivar un grupo no toca el catalogo).
/// TENANT-SCOPED.
/// </summary>
public class ActividadSubcategoriaPlantillaGrupo : TenantEntity
{
    public Guid SubcategoriaId { get; set; }
    public ActividadSubcategoria? Subcategoria { get; set; }

    /// <summary>Grupo de plantillas de documento habilitado para el concepto.</summary>
    public Guid GroupId { get; set; }
    public DocumentTemplateGroup? Group { get; set; }
}
