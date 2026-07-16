using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo DATO-A-DATO de una relacion del Contenedor de datos: dice que fila de la tabla origen
/// apunta a que fila de la tabla destino, BAJO una arista concreta (<see cref="RelationId"/>).
/// Es la FASE 2 del rediseno de relaciones (2026-07-15): la arista <c>DataModelRelation</c> dejo
/// modelado el ESQUEMA (que tabla se relaciona con cual y con que cardinalidad), y esta entidad
/// re-cablea el vinculo entre registros, que el backfill de aquella migracion descarto.
///
/// Reemplaza a <c>DataContainerLink</c>, que colgaba de una COLUMNA (tipo RelationMany, deprecado):
/// aqui el vinculo cuelga de la ARISTA, coherente con que la relacion es ortogonal al tipo de dato.
///
/// Cardinalidad (se valida en el servicio, no en el esquema):
/// - <c>ManyToOne</c>: como maximo UN vinculo por (relacion, fila origen).
/// - <c>ManyToMany</c>: varios vinculos por (relacion, fila origen).
///
/// TENANT-SCOPED. Unico por (relacion, fila origen, fila destino). Muere en cascada con su arista;
/// las FKs a filas son Restrict para no abrir multiples rutas de cascada en SQL Server (la limpieza
/// al borrar una fila la hace el servicio dentro de la transaccion).
/// </summary>
public class DataModelRelationLink : TenantEntity
{
    /// <summary>Arista (relacion) a la que pertenece el vinculo.</summary>
    public Guid RelationId { get; set; }
    public DataModelRelation? Relation { get; set; }

    /// <summary>Fila de la tabla ORIGEN de la arista (la que apunta).</summary>
    public Guid FromRowId { get; set; }
    public DataContainerRow? FromRow { get; set; }

    /// <summary>Fila de la tabla DESTINO de la arista (la referenciada).</summary>
    public Guid ToRowId { get; set; }
    public DataContainerRow? ToRow { get; set; }
}
