using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.DataContainers;

// ==== Tabla DERIVADA del Contenedor de datos (ADR-0127) ====
// Una tabla derivada es una tabla MATERIALIZADA (filas reales) del mismo contenedor cuyas filas se
// calculan agrupando OTRA tabla del contenedor por columnas elegidas, con agregados. La receta
// (DerivationSpec) vive en el MappingJson de un conector ConnectorKind.FromTable, y el refresco lo
// hace el mismo ImportProcess (manual / intervalo / cron) que los demas conectores, con bitacora.

/// <summary>Funcion de agregacion de una columna derivada.</summary>
public enum DerivationFn
{
    /// <summary>Cantidad de filas del grupo. No necesita columna origen. Resultado Number.</summary>
    Count,
    /// <summary>Suma (solo Number/Decimal). Resultado Decimal.</summary>
    Sum,
    /// <summary>Promedio (solo Number/Decimal). Resultado Decimal; null si el grupo no tiene valores numericos.</summary>
    Avg,
    /// <summary>Minimo (cualquier escalar; compara por tipo). Hereda el tipo de la columna origen.</summary>
    Min,
    /// <summary>Maximo (cualquier escalar; compara por tipo). Hereda el tipo de la columna origen.</summary>
    Max
}

/// <summary>Operador del filtro opcional que se aplica a las filas del origen ANTES de agrupar.</summary>
public enum DerivationFilterOp
{
    Equals,
    NotEquals,
    Contains,
    GreaterThan,
    LessThan,
    IsEmpty,
    NotEmpty
}

/// <summary>Un agregado de la derivacion: columna origen (null para Count), funcion y nombre de la
/// columna destino en la tabla derivada.</summary>
public sealed record DerivationAggregate(Guid? SourceColumnId, DerivationFn Fn, string TargetColumnName);

/// <summary>Una condicion del filtro previo (AND entre condiciones).</summary>
public sealed record DerivationFilter(Guid ColumnId, DerivationFilterOp Op, string? Value);

/// <summary>
/// Especificacion PERSISTIDA de una derivacion (JSON en <c>DataConnector.MappingJson</c>). Forma tipada
/// para que el motor, el wizard y las pruebas hablen el mismo idioma. <see cref="KeyColumnName"/> es la
/// columna Text de la tabla derivada que guarda la clave normalizada del grupo (concatenacion de las
/// columnas de agrupacion); es la columna clave del Upsert, lo que permite que las columnas PROPIAS que
/// el usuario agregue a la derivada sobrevivan a cada refresco.
/// </summary>
public sealed record DerivationSpec(
    Guid SourceTableId,
    IReadOnlyList<Guid> GroupBy,
    IReadOnlyList<DerivationAggregate> Aggregates,
    IReadOnlyList<DerivationFilter>? Filter = null,
    string KeyColumnName = DerivationSpec.DefaultKeyColumnName)
{
    public const string DefaultKeyColumnName = "Clave";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Parsea el MappingJson de un conector FromTable. Null si no es una spec valida.</summary>
    public static DerivationSpec? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            var spec = JsonSerializer.Deserialize<DerivationSpec>(json, Json);
            if (spec is null || spec.SourceTableId == Guid.Empty) { return null; }
            return spec with
            {
                GroupBy = spec.GroupBy ?? Array.Empty<Guid>(),
                Aggregates = spec.Aggregates ?? Array.Empty<DerivationAggregate>(),
                KeyColumnName = string.IsNullOrWhiteSpace(spec.KeyColumnName) ? DefaultKeyColumnName : spec.KeyColumnName.Trim()
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Papel de una columna en la tabla derivada.</summary>
public enum DerivedColumnRole
{
    /// <summary>Columna de agrupacion (hereda nombre y tipo del origen).</summary>
    GroupBy,
    /// <summary>Clave normalizada del grupo (Text). Columna clave del Upsert.</summary>
    Key,
    /// <summary>Resultado de un agregado.</summary>
    Aggregate
}

/// <summary>Una columna que la derivacion produce: nombre, tipo y papel.</summary>
public sealed record DerivedColumnPlan(string Name, DataContainerColumnType Type, DerivedColumnRole Role);

/// <summary>Columna del origen tal como la necesita el motor (sin EF).</summary>
public sealed record SourceColumnInfo(Guid Id, string Name, DataContainerColumnType Type);

/// <summary>Resultado de calcular la derivacion en memoria: columnas producidas + filas por NOMBRE de
/// columna destino (listas para <see cref="IRowIngestService"/>).</summary>
public sealed record DerivedResult(
    IReadOnlyList<DerivedColumnPlan> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    int SourceRowsRead,
    int SourceRowsAfterFilter);

/// <summary>Vista previa del wizard: los primeros N grupos calculados con el MISMO motor del refresco.</summary>
public sealed record DerivationPreviewDto(
    IReadOnlyList<DerivedColumnPlan> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    int TotalGroups,
    int SourceRowsRead,
    int SourceRowsAfterFilter);

/// <summary>Alta de una tabla derivada: nombre de la hija, spec y programacion del refresco.</summary>
public sealed record CreateDerivedTableRequest(
    Guid ModelId,
    string Name,
    string? Description,
    DerivationSpec Spec,
    ImportScheduleKind ScheduleKind = ImportScheduleKind.Manual,
    int? IntervalMinutes = null,
    string? CronExpression = null);

/// <summary>Lo que nace al crear una derivada (todo en UNA transaccion).</summary>
public sealed record DerivedTableCreatedDto(Guid TableId, Guid ConnectorId, Guid ProcessId);

/// <summary>Desenlace de un refresco server-direct. <see cref="Deleted"/> incluye la PODA (grupos que
/// desaparecieron del origen) ademas del vaciado de Replace.</summary>
public sealed record DerivedRefreshOutcome(bool Success, int Inserted, int Updated, int Deleted, string? Error)
{
    public static DerivedRefreshOutcome Fail(string error) => new(false, 0, 0, 0, error);
}

/// <summary>
/// Servicio de tablas derivadas (ADR-0127): vista previa, alta transaccional (tabla hija + columnas +
/// conector FromTable + proceso) y refresco (motor + ingesta Upsert + poda). Tenant-scoped por el filtro
/// global. El refresco lo invoca <c>ProcessRunner</c> (boton "Actualizar datos" y scheduler), que es
/// quien abre/cierra la corrida en la bitacora.
/// </summary>
public interface IDerivedTableService
{
    /// <summary>Calcula los primeros <paramref name="take"/> grupos SIN escribir nada.</summary>
    Task<DerivationPreviewDto> PreviewAsync(DerivationSpec spec, int take = 20, CancellationToken ct = default);

    /// <summary>Crea la tabla hija, sus columnas, el conector FromTable y el proceso (Upsert por clave),
    /// en una sola transaccion. NO corre el primer refresco: eso lo hace el llamador via el runner, para
    /// que quede en la bitacora.</summary>
    Task<DerivedTableCreatedDto> CreateAsync(CreateDerivedTableRequest req, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Refresca la tabla derivada del conector: lee el origen, agrupa, ingiere con el modo
    /// indicado y, en Upsert, PODA las filas cuya clave ya no existe. Todo en transaccion. Valida antes:
    /// origen existe, origen NO es derivado (un solo nivel), columnas de la spec existen.</summary>
    Task<DerivedRefreshOutcome> RefreshAsync(Guid connectorId, ApiImportMode mode, string? keyColumnName, CancellationToken ct = default);
}
