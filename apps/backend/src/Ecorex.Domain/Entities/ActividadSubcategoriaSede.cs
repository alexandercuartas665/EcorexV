using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Sede/entidad donde aplica una subcategoria (concepto) del catalogo de actividades (000270):
/// union M:N entre una <see cref="ActividadSubcategoria"/> y una <see cref="Entidad"/> (agencia,
/// area o sucursal creada en "Configuracion de la entidad"). Reemplaza el antiguo texto libre:
/// ahora el concepto apunta a entidades REALES, y al crear una actividad esas entidades alimentan
/// el selector "Empresa/Area". Vive y muere con la subcategoria (Cascade). La FK a la entidad es
/// NO ACTION (borrar la entidad no toca el catalogo). Sin sedes asociadas = aplica a TODAS.
/// TENANT-SCOPED.
/// </summary>
public class ActividadSubcategoriaSede : TenantEntity
{
    public Guid SubcategoriaId { get; set; }
    public ActividadSubcategoria? Subcategoria { get; set; }

    /// <summary>Entidad (agencia/area/sucursal) donde aplica el concepto.</summary>
    public Guid EntidadId { get; set; }
    public Entidad? Entidad { get; set; }
}
