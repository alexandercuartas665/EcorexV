using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

// ==== Contenedor de datos (modelos dinamicos EAV con anidamiento) ====
// Portado de CUBOT.redmanager y evolucionado: los contenedores forman un ARBOL (submodelos), y
// se agrega la configuracion de importacion (conector/cliente/proceso) en contratos aparte.

/// <summary>Fila del listado de contenedores raiz (tarjeta/lista).</summary>
public sealed record DataContainerDto(
    Guid Id,
    string Name,
    string? Description,
    DataSourceKind SourceKind,
    int ColumnCount,
    int RowCount,
    DateTimeOffset UpdatedAt);

/// <summary>Columna (campo) de un contenedor. Submodel apunta al contenedor hijo (anidado). Las
/// relaciones inter-tabla ya NO son columnas: son la entidad DataModelRelation (arista del ER).</summary>
public sealed record DataContainerColumnDto(
    Guid Id,
    string Name,
    string? Description,
    DataContainerColumnType Type,
    int SortOrder,
    bool IsRequired,
    Guid? ChildContainerId,
    string? ChildContainerName);

/// <summary>Detalle de un contenedor con su esquema (columnas). SourceKind y anidamiento incluidos.</summary>
public sealed record DataContainerDetailDto(
    Guid Id,
    string Name,
    string? Description,
    DataSourceKind SourceKind,
    Guid? ParentContainerId,
    Guid? ParentFieldId,
    IReadOnlyList<DataContainerColumnDto> Columns);

/// <summary>Una fila con sus valores por ColumnId. ValuesByColumnId incluye escalares y Reference
/// (esta ultima guarda el id del registro destino). LinksByColumnId trae los ids destino de cada
/// campo RelationMany (N:N). Las columnas Submodel no van aqui (se navegan aparte).</summary>
public sealed record DataContainerRowDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    Dictionary<Guid, string?> ValuesByColumnId,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? LinksByColumnId = null);

/// <summary>Opcion para el selector de una relacion: un registro de la tabla destino con su etiqueta.</summary>
public sealed record RowOptionDto(Guid Id, string Label);

/// <summary>
/// Consulta paginada de los registros de una tabla. A diferencia de ListRowsAsync (que trae un tope
/// y filtra EN MEMORIA), esta se resuelve EN EL SERVIDOR: es la que consume el modulo publicado, donde
/// la tabla puede crecer. <paramref name="Search"/> busca el texto en CUALQUIER celda de la fila;
/// <paramref name="Filters"/> exige coincidencia por columna (AND entre columnas).
/// </summary>
/// <param name="SortColumnId">Columna por la que ordenar; null = por fecha de creacion.</param>
public sealed record DataRowQuery(
    Guid ContainerId,
    Guid? ParentRowId = null,
    string? Search = null,
    IReadOnlyDictionary<Guid, string>? Filters = null,
    Guid? SortColumnId = null,
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 50);

/// <summary>Una pagina de registros. <paramref name="Total"/> es el total que casa con el filtro
/// (no el de la pagina), para poder pintar el paginador.</summary>
public sealed record DataRowPageDto(
    IReadOnlyList<DataContainerRowDto> Rows,
    int Total,
    int Page,
    int PageSize);

/// <summary>Input para upsert de columna (Id null = nueva). ChildContainerId solo para Submodel.</summary>
public sealed record SaveDataColumnInput(
    Guid? Id,
    string Name,
    string? Description,
    DataContainerColumnType Type,
    int SortOrder,
    bool IsRequired,
    Guid? ChildContainerId = null);

/// <summary>Crear/editar un contenedor. ParentContainerId/ParentFieldId != null = sub-contenedor (submodelo).</summary>
public sealed record SaveDataContainerRequest(
    Guid? Id,
    string Name,
    string? Description,
    DataSourceKind SourceKind,
    IReadOnlyList<SaveDataColumnInput> Columns,
    Guid? ParentContainerId = null,
    Guid? ParentFieldId = null);

/// <summary>Upsert de fila. ParentRowId/ParentFieldId != null = fila de una sub-tabla anidada.
/// ValuesByColumnId lleva escalares y Reference (id del registro destino). LinksByColumnId lleva,
/// por cada campo RelationMany, la lista de ids destino vinculados (reemplaza el set actual).</summary>
public sealed record SaveDataRowRequest(
    Guid ContainerId,
    Guid? RowId,
    Dictionary<Guid, string?> ValuesByColumnId,
    Guid? ParentRowId = null,
    Guid? ParentFieldId = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? LinksByColumnId = null);

public sealed record DataImportResult(
    bool Success,
    int RowsImported,
    int RowsFailed,
    IReadOnlyList<string> Errors);

public sealed record DataExportResult(string FileName, byte[] Bytes);

/// <summary>
/// CRUD del Contenedor de datos (modelos EAV con anidamiento) + import/export Excel. Tenant-scoped
/// por el filtro global. El borrado de un contenedor arrastra su arbol (cascada). El Excel opera
/// sobre las columnas ESCALARES del contenedor indicado (los submodelos se capturan por la UI).
/// </summary>
public interface IDataContainerService
{
    /// <summary>Lista los contenedores RAIZ del tenant (los submodelos no aparecen aqui).</summary>
    Task<IReadOnlyList<DataContainerDto>> ListAsync(CancellationToken ct = default);
    Task<DataContainerDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Sub-contenedores (submodelos) directos de un contenedor, para el arbol de la UI.</summary>
    Task<IReadOnlyList<DataContainerDetailDto>> ListChildrenAsync(Guid parentContainerId, CancellationToken ct = default);

    Task<DataContainerDetailDto?> SaveAsync(SaveDataContainerRequest req, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Crea/edita una tabla que pertenece a un contenedor (DataModel), estampando su
    /// ModelId y su posicion en el lienzo (CanvasX/Y). Reusa la misma maquinaria de columnas que
    /// SaveAsync (incluye Reference/RelationMany/Submodel). Usado por IDataModelService.</summary>
    Task<DataContainerDetailDto?> SaveTableAsync(
        Guid modelId, Guid? tableId, string name, string? desc, double x, double y,
        IReadOnlyList<SaveDataColumnInput> columns, Guid actorUserId, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<DataContainerRowDto>> ListRowsAsync(Guid containerId, string? search = null, Guid? parentRowId = null, int take = 500, CancellationToken ct = default);

    /// <summary>
    /// Pagina de registros resuelta EN EL SERVIDOR (busqueda, filtros por columna, orden y paginado).
    /// Es la via del modulo publicado; <see cref="ListRowsAsync"/> se queda para el configurador y los
    /// selectores, donde el tope en memoria basta. OJO: el orden por una columna es ALFABETICO, porque
    /// el modelo EAV persiste todo valor como string (ver DataContainerCell); un campo numerico ordena
    /// "10" antes que "9". Corregirlo pide una clave de orden tipada por celda (pendiente).
    /// </summary>
    Task<DataRowPageDto> ListRowsPagedAsync(DataRowQuery query, CancellationToken ct = default);

    /// <summary>Registros de una tabla (contenedor raiz) con una etiqueta legible, para poblar el
    /// selector de un campo Reference/RelationMany que apunta a esa tabla.</summary>
    Task<IReadOnlyList<RowOptionDto>> ListRowOptionsAsync(Guid containerId, int take = 500, CancellationToken ct = default);
    Task<DataContainerRowDto?> SaveRowAsync(SaveDataRowRequest req, Guid actorUserId, CancellationToken ct = default);
    Task<bool> DeleteRowAsync(Guid rowId, Guid actorUserId, CancellationToken ct = default);

    Task<DataImportResult> ImportFromExcelAsync(Guid containerId, Stream xlsxStream, Guid actorUserId, CancellationToken ct = default);
    Task<DataExportResult?> ExportToExcelAsync(Guid containerId, CancellationToken ct = default);
}
