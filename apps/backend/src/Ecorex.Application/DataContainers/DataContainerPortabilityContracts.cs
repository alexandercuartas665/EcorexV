using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

// ==== Exportar / importar la DEFINICION de un contenedor (DataModel) a JSON (ADR-0132) ====
// Se exporta la ESTRUCTURA (tablas, columnas, submodelos, tablas derivadas), las relaciones del ER, los
// conectores (fuentes) y los procesos (programacion del refresco). NO se exportan los DATOS (filas/celdas)
// ni los SECRETOS (credenciales cifradas): de cada conector/destino solo viaja un flag "tenia credenciales"
// para que al importar el operador las vuelva a capturar. Las referencias entre piezas van por CLAVES
// locales del paquete (t1, c1, ...) y por NOMBRE de columna, nunca por GUID: al importar se crean ids
// nuevos y se remapea todo, de modo que el paquete es portable entre tenants y entornos.

/// <summary>Una columna de una tabla dentro del paquete. Submodel referencia la tabla hija por su clave.</summary>
public sealed record PortableColumn(
    string Name,
    string? Description,
    DataContainerColumnType Type,
    int SortOrder,
    bool IsRequired,
    string? ChildTableKey = null);

/// <summary>Una tabla del contenedor (raiz o submodelo o derivada). Las referencias a otras tablas van por
/// clave local; la fuente de una derivada puede ser EXTERNA (otra tabla del tenant fuera del paquete), que
/// se guarda por nombre y al importar se intenta casar por nombre.</summary>
public sealed record PortableTable(
    string Key,
    string Name,
    string? Description,
    double CanvasX,
    double CanvasY,
    IReadOnlyList<PortableColumn> Columns,
    string? ParentKey = null,
    string? ParentFieldColumn = null,
    string? DerivedFromKey = null,
    string? DerivedFromExternalTable = null,
    string? DerivedFromExternalModel = null);

/// <summary>Arista del ER entre dos tablas del contenedor, por clave local.</summary>
public sealed record PortableRelation(string FromKey, string ToKey, DataModelRelationKind Kind, string? Name);

/// <summary>Un agregado de una derivada, con la columna origen por NOMBRE (null para Count).</summary>
public sealed record PortableAggregate(string? SourceColumn, DerivationFn Fn, string TargetColumnName);

/// <summary>Una condicion del filtro previo de una derivada, con la columna por NOMBRE.</summary>
public sealed record PortableFilter(string Column, DerivationFilterOp Op, string? Value);

/// <summary>La receta de una tabla derivada, con la tabla y columnas origen por clave/nombre (no por GUID).</summary>
public sealed record PortableDerivation(
    string? SourceKey,
    string? SourceExternalTable,
    IReadOnlyList<string> GroupByColumns,
    IReadOnlyList<PortableAggregate> Aggregates,
    IReadOnlyList<PortableFilter>? Filter,
    string KeyColumnName);

/// <summary>Un conector (fuente). SIN secretos: <see cref="HadCredentials"/> dice si la fuente original tenia
/// credenciales, para avisar al operador que debe re-capturarlas tras importar. La tabla destino va por
/// clave. Para FromTable (derivada) la receta va en <see cref="Derivation"/> (no en MappingJson).</summary>
public sealed record PortableConnector(
    string Key,
    string Name,
    ConnectorKind Kind,
    string? EndpointUrl,
    string? HttpMethod,
    ConnectorAuthKind AuthKind,
    DbEngine? DbEngine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    string? Query,
    string? HeadersJson,
    string? TokenExchangeJson,
    bool IsActive,
    bool HadCredentials,
    string? ContainerKey,
    string? MappingJson,
    PortableDerivation? Derivation);

/// <summary>Un proceso de refresco (programacion). El conector por clave; el cliente remoto por su id publico
/// (se intenta casar al importar, nunca viaja el secreto).</summary>
public sealed record PortableProcess(
    string Name,
    string? ConnectorKey,
    string? ClientPublicId,
    string? ClientName,
    ImportScheduleKind ScheduleKind,
    int? IntervalMinutes,
    string? CronExpression,
    bool IsActive,
    ApiImportMode Mode,
    string? KeyColumn);

/// <summary>El destino (1:1 con el contenedor). SIN secretos.</summary>
public sealed record PortableDestination(
    DestinationKind Kind,
    DbEngine? DbEngine,
    string? Host,
    int? Port,
    string? DatabaseName,
    string? Username,
    bool HadCredentials);

/// <summary>El paquete completo de un contenedor: estructura + relaciones + conectores + programacion, sin
/// datos ni secretos. <see cref="FormatVersion"/> permite evolucionar el formato sin romper paquetes viejos.</summary>
public sealed record ContainerPackage(
    string FormatVersion,
    string Name,
    string? Description,
    IReadOnlyList<PortableTable> Tables,
    IReadOnlyList<PortableRelation> Relations,
    IReadOnlyList<PortableConnector> Connectors,
    IReadOnlyList<PortableProcess> Processes,
    PortableDestination? Destination,
    DateTimeOffset ExportedAt)
{
    public const string CurrentFormatVersion = "1";
}

/// <summary>Resultado de importar un paquete: el contenedor nuevo y avisos para el operador (p.ej. conectores
/// cuyas credenciales hay que volver a capturar, o una fuente de derivada que no se pudo resolver).</summary>
public sealed record ContainerImportResult(DataModelDto Model, IReadOnlyList<string> Warnings);

/// <summary>
/// Exporta la DEFINICION de un contenedor a JSON e importa un paquete como un contenedor NUEVO. No mueve
/// datos (filas/celdas) ni secretos (credenciales): de cada fuente solo viaja si tenia credenciales, para
/// que el operador las re-capture. Tenant-scoped por el filtro global. Ver ADR-0132.
/// </summary>
public interface IDataContainerPortabilityService
{
    /// <summary>Serializa la definicion del contenedor a un JSON (bytes UTF-8 + nombre de archivo sugerido).
    /// Null si el contenedor no existe en el tenant.</summary>
    Task<DataExportResult?> ExportAsync(Guid modelId, CancellationToken ct = default);

    /// <summary>Crea un contenedor NUEVO a partir del paquete JSON (nombre unico en el tenant; ids nuevos y
    /// referencias remapeadas). No importa datos ni secretos. Lanza con mensaje legible si el JSON no es un
    /// paquete valido.</summary>
    Task<ContainerImportResult> ImportAsync(string json, Guid actorUserId, CancellationToken ct = default);
}
