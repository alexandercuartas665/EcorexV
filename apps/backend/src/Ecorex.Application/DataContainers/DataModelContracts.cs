using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

// ==== Contenedor (DataModel): agrupa varias tablas + relaciones + config de importacion ====

/// <summary>Fila del listado de contenedores (tarjeta): con conteos de tablas y relaciones.</summary>
public sealed record DataModelDto(
    Guid Id,
    string Name,
    string? Description,
    int TableCount,
    int RelationCount,
    DateTimeOffset UpdatedAt);

/// <summary>Una tabla dentro del contenedor, con su posicion en el lienzo y sus campos.</summary>
public sealed record ModelTableDto(
    Guid Id,
    string Name,
    string? Description,
    double CanvasX,
    double CanvasY,
    IReadOnlyList<DataContainerColumnDto> Columns);

/// <summary>Una relacion (arista del lienzo) entre dos tablas del contenedor. Es una entidad propia
/// (DataModelRelation), ORTOGONAL al tipo de dato de las columnas: tabla origen -> tabla destino con
/// una cardinalidad (N:1 / N:N) y una etiqueta opcional.</summary>
public sealed record ModelRelationDto(
    Guid Id,
    Guid FromTableId,
    Guid ToTableId,
    DataModelRelationKind Kind,
    string? Name);

/// <summary>Alta de una relacion entre dos tablas del mismo contenedor.</summary>
public sealed record SaveModelRelationRequest(
    Guid ModelId,
    Guid FromTableId,
    Guid ToTableId,
    DataModelRelationKind Kind,
    string? Name);

/// <summary>Detalle completo del contenedor para el lienzo ER: sus tablas y las relaciones entre ellas.</summary>
public sealed record DataModelDetailDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<ModelTableDto> Tables,
    IReadOnlyList<ModelRelationDto> Relations);

/// <summary>Alta/edicion de la cabecera del contenedor (nombre/descripcion).</summary>
public sealed record SaveDataModelRequest(Guid? Id, string Name, string? Description);

/// <summary>Alta/edicion de una tabla dentro de un contenedor (campos + posicion en lienzo).</summary>
public sealed record SaveModelTableRequest(
    Guid ModelId,
    Guid? TableId,
    string Name,
    string? Description,
    double CanvasX,
    double CanvasY,
    IReadOnlyList<SaveDataColumnInput> Columns);

/// <summary>Actualiza solo la posicion de una tabla en el lienzo (drag).</summary>
public sealed record UpdateTablePositionRequest(Guid TableId, double CanvasX, double CanvasY);

/// <summary>
/// CRUD del Contenedor (DataModel) y sus tablas/relaciones. Un contenedor agrupa varias tablas con
/// relaciones internas (aristas del lienzo ER). Reusa la maquinaria EAV de tablas (columnas/filas/
/// celdas) y de relaciones (Reference/RelationMany) del <see cref="IDataContainerService"/>, pero
/// scoped al modelo: las relaciones solo apuntan a tablas del MISMO contenedor. Tenant-scoped.
/// </summary>
public interface IDataModelService
{
    Task<IReadOnlyList<DataModelDto>> ListAsync(CancellationToken ct = default);
    Task<DataModelDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<DataModelDto?> SaveModelAsync(SaveDataModelRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteModelAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Crea/edita una tabla del contenedor (campos + posicion). Los campos son solo tipos de
    /// dato escalares/Submodel; las relaciones se gestionan aparte (AddRelationAsync).</summary>
    Task<ModelTableDto?> SaveTableAsync(SaveModelTableRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteTableAsync(Guid tableId, Guid actorUserId, CancellationToken ct = default);
    Task<bool> UpdateTablePositionAsync(UpdateTablePositionRequest req, CancellationToken ct = default);

    // ==== Relaciones inter-tabla (aristas del ER, entidad DataModelRelation) ====
    /// <summary>Crea una relacion entre dos tablas del MISMO contenedor (se valida). Origen != destino.</summary>
    Task<ModelRelationDto?> AddRelationAsync(SaveModelRelationRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteRelationAsync(Guid relationId, Guid actorUserId, CancellationToken ct = default);
}
