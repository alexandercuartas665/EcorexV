using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Relacion inter-tabla (arista del ER) de un Contenedor de datos: objeto de PRIMERA CLASE,
/// independiente de las columnas. Une dos tablas del MISMO modelo (origen -> destino) con una
/// cardinalidad. Reemplaza el diseno anterior donde la relacion era un TIPO de columna
/// (Reference/RelationMany + ReferencedContainerId): tipo de dato y relacion son propiedades
/// ortogonales. TENANT-SCOPED. Muere en cascada con su modelo.
/// </summary>
public class DataModelRelation : TenantEntity
{
    public Guid ModelId { get; set; }
    public DataModel? Model { get; set; }

    /// <summary>Tabla origen de la relacion (la que "apunta").</summary>
    public Guid FromTableId { get; set; }
    public DataContainer? FromTable { get; set; }

    /// <summary>Tabla destino de la relacion (la referenciada).</summary>
    public Guid ToTableId { get; set; }
    public DataContainer? ToTable { get; set; }

    public DataModelRelationKind Kind { get; set; } = DataModelRelationKind.ManyToOne;

    /// <summary>Etiqueta opcional de la arista (ej. "Cliente"). En el backfill hereda el nombre de la columna.</summary>
    public string? Name { get; set; }
}
