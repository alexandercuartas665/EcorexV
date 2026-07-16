using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Una relacion SALIENTE de la tabla que se edita (arista donde es origen), con el estado de los
/// vinculos de una fila concreta y las opciones para elegir destino.
/// </summary>
/// <param name="Kind">ManyToOne = como maximo un destino; ManyToMany = varios.</param>
/// <param name="LinkedRowIds">Filas destino ya vinculadas a la fila en edicion (vacio si es nueva).</param>
/// <param name="Options">Filas de la tabla destino, con etiqueta legible, para el selector.</param>
public sealed record RowRelationDto(
    Guid RelationId,
    string? Name,
    DataModelRelationKind Kind,
    Guid ToTableId,
    string ToTableName,
    IReadOnlyList<Guid> LinkedRowIds,
    IReadOnlyList<RowOptionDto> Options);

public enum RelationLinkStatus { Ok = 0, NotFound, Invalid }

public sealed record RelationLinkResult(RelationLinkStatus Status, string? Error)
{
    public bool IsOk => Status == RelationLinkStatus.Ok;
    public static RelationLinkResult Ok() => new(RelationLinkStatus.Ok, null);
    public static RelationLinkResult NotFound(string error) => new(RelationLinkStatus.NotFound, error);
    public static RelationLinkResult Invalid(string error) => new(RelationLinkStatus.Invalid, error);
}

/// <summary>
/// Vinculos DATO-A-DATO de las relaciones del Contenedor de datos (FASE 2 del rediseno de
/// relaciones): que fila apunta a que fila bajo una arista <c>DataModelRelation</c>.
///
/// La arista modela el ESQUEMA (que tabla se relaciona con cual y con que cardinalidad); esto
/// re-cablea el vinculo entre REGISTROS, que el backfill de aquella migracion descarto a proposito.
/// La cardinalidad se valida aqui (el esquema no puede: es la misma tabla para N:1 y N:N).
///
/// TENANT-SCOPED via el filtro global; aqui NUNCA se filtra a mano por TenantId.
/// </summary>
public interface IDataRelationLinkService
{
    /// <summary>
    /// Relaciones salientes de una tabla, con los vinculos de <paramref name="rowId"/> (null = fila
    /// nueva: devuelve las relaciones con sus opciones y sin vinculos). Es lo que pinta el editor.
    /// </summary>
    Task<IReadOnlyList<RowRelationDto>> ListForRowAsync(
        Guid containerId, Guid? rowId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reemplaza el set de vinculos de (arista, fila origen). Lista vacia = desvincula todo.
    /// Valida: la arista existe; la fila origen es de su tabla origen; cada destino es de su tabla
    /// destino; y en ManyToOne, como maximo UN destino.
    /// </summary>
    Task<RelationLinkResult> SetLinksAsync(
        Guid relationId, Guid fromRowId, IReadOnlyList<Guid> toRowIds, CancellationToken cancellationToken = default);
}
