namespace Ecorex.Application.Reporting;

// La DEFINICION declarativa de una consulta (el "spec"). Es un dato, no codigo: la arma el usuario
// arrastrando campos, o la genera la IA por instruccion (Ola 4). Referencia SOLO nombres logicos del
// catalogo; el query-builder (IReportDataSource) lo traduce a EF parametrizado y tenant-safe.

/// <summary>Un filtro sobre un campo del catalogo. Los valores viajan como texto y se convierten
/// segun el tipo logico del campo al traducir a EF.</summary>
public sealed record ReportFilter(
    string FieldKey,
    ReportFilterOperator Operator,
    IReadOnlyList<string?> Values)
{
    public static ReportFilter Eq(string field, string? value) =>
        new(field, ReportFilterOperator.Equals, new[] { value });

    public static ReportFilter Between(string field, string? from, string? to) =>
        new(field, ReportFilterOperator.Between, new[] { from, to });

    public static ReportFilter In(string field, params string?[] values) =>
        new(field, ReportFilterOperator.In, values);
}

/// <summary>Una agregacion sobre un campo (con group by declarado aparte en el spec).</summary>
public sealed record ReportAggregate(string FieldKey, ReportAggregateFunction Function);

/// <summary>Orden por un campo.</summary>
public sealed record ReportSort(string FieldKey, bool Descending = false);

/// <summary>
/// Especificacion declarativa de una consulta de reporte. Sin SQL: solo nombres logicos del catalogo.
///
/// Dos modos:
/// - TABULAR: <see cref="GroupBy"/> vacio -> devuelve <see cref="Fields"/> tal cual, filtrado/ordenado.
/// - AGREGADO: <see cref="GroupBy"/> con al menos un campo -> agrupa y devuelve las columnas de
///   agrupacion + una columna por cada <see cref="Aggregates"/>.
/// </summary>
public sealed record ReportQuerySpec
{
    /// <summary>Clave de la fuente en el catalogo (ej. "native:taskitem" o "container:{guid}").</summary>
    public required string SourceKey { get; init; }

    /// <summary>Campos a proyectar en modo tabular. Ignorado en modo agregado (se usan GroupBy+Aggregates).</summary>
    public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ReportFilter> Filters { get; init; } = Array.Empty<ReportFilter>();

    public IReadOnlyList<string> GroupBy { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ReportAggregate> Aggregates { get; init; } = Array.Empty<ReportAggregate>();

    public IReadOnlyList<ReportSort> Sort { get; init; } = Array.Empty<ReportSort>();

    /// <summary>Limite de filas (opcional). Guarda razonable contra resultados enormes.</summary>
    public int? Top { get; init; }

    public bool IsAggregated => GroupBy.Count > 0 || Aggregates.Count > 0;
}

/// <summary>
/// Contexto de ejecucion de un reporte. Lleva el tenant y el usuario. NOTA: el aislamiento real NO
/// depende de este objeto sino del filtro global tenant del DbContext (fail-closed): aunque un
/// llamador mintiera aqui, la consulta seguiria acotada al tenant activo del contexto EF. Este ctx es
/// informativo y para politicas futuras.
/// </summary>
public sealed record ReportContext(Guid TenantId, Guid? UserId = null);

/// <summary>
/// Error de VALIDACION del spec contra el catalogo (fuente inexistente, campo fuera del catalogo,
/// operacion no admitida por el campo). Es el limite de seguridad: se rechaza antes de tocar la BD.
/// </summary>
public sealed class ReportValidationException : Exception
{
    public ReportValidationException(string message) : base(message) { }
}
