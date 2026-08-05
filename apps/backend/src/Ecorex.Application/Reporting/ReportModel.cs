namespace Ecorex.Application.Reporting;

// Modelo NEUTRO del motor de reportes (Ola 1). Estas piezas son la "capa de definicion" propia,
// independiente de la libreria que pinta (Bold Reports / ECharts). No hay SQL crudo ni columnas
// fisicas aqui: todo se referencia por el nombre logico del catalogo semantico. Ver ADR-0051 y la
// spec del vault "Capa 6 / Motor de Reportes y BI" (doc 02).

/// <summary>Tipo logico de un campo reportable. Acota lo que la UI y la IA pueden pedir.</summary>
public enum ReportFieldType
{
    Text,
    Number,
    Decimal,
    Date,
    Boolean
}

/// <summary>Origen de una fuente reportable: entidad nativa (curada por el dev), contenedor EAV o una
/// fuente EXTERNA gobernada (ADR-0064, base de datos ajena leida en vivo por el conector).</summary>
public enum ReportSourceKind
{
    Native,
    Container,
    External
}

/// <summary>Operadores de filtro admitidos. El datasource los traduce a EF parametrizado.</summary>
public enum ReportFilterOperator
{
    Equals,
    NotEquals,
    Contains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Between,
    In
}

/// <summary>Funciones de agregacion admitidas sobre un campo agrupado.</summary>
public enum ReportAggregateFunction
{
    Count,
    Sum,
    Avg,
    Min,
    Max
}

/// <summary>
/// Un campo reportable del catalogo: nombre logico + tipo + que operaciones admite. La UI y la IA
/// SOLO pueden referenciar campos que existen aqui: es el limite de seguridad (lo que no esta en el
/// catalogo no es reportable).
/// </summary>
/// <param name="Key">Nombre logico estable (ej. "Status" para nativa, o el Id de columna para contenedor).</param>
/// <param name="DisplayName">Etiqueta de negocio para el usuario.</param>
/// <param name="Type">Tipo logico del campo.</param>
/// <param name="CanFilter">Si admite aparecer en un filtro.</param>
/// <param name="CanGroup">Si admite agruparse (group by).</param>
/// <param name="CanAggregate">Si admite ser el campo de una agregacion numerica (Sum/Avg/Min/Max).</param>
public sealed record ReportField(
    string Key,
    string DisplayName,
    ReportFieldType Type,
    bool CanFilter = true,
    bool CanGroup = true,
    bool CanAggregate = false);

/// <summary>
/// Descriptor de una fuente reportable (entidad nativa o contenedor). Es lo que publica el catalogo.
/// </summary>
public sealed record ReportSourceDescriptor(
    string Key,
    string DisplayName,
    ReportSourceKind Kind,
    IReadOnlyList<ReportField> Fields)
{
    /// <summary>Busca un campo por su clave logica (case-insensitive). Null si no existe.</summary>
    public ReportField? FindField(string fieldKey) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, fieldKey, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Columna de un resultado ya materializado.</summary>
public sealed record ReportColumn(string Key, string DisplayName, ReportFieldType Type);

/// <summary>
/// Resultado NEUTRO de una consulta de reporte: columnas + filas (valores por columna en el mismo
/// orden que <see cref="Columns"/>). Alimenta por igual al visor Bold (como JSON/Web data source) y
/// a ECharts (como series del "option"). Nunca expone entidades EF ni columnas fisicas.
/// </summary>
public sealed class ReportDataSet
{
    public ReportDataSet(IReadOnlyList<ReportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<ReportColumn> Columns { get; }
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; }
    public int RowCount => Rows.Count;

    public static ReportDataSet Empty { get; } =
        new(Array.Empty<ReportColumn>(), Array.Empty<IReadOnlyList<object?>>());
}
