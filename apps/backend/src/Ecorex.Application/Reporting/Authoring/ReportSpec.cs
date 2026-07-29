using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Reporting.Authoring;

// El JSON-spec: el artefacto declarativo que la IA genera por instruccion (Ola 4) y que el usuario
// abre/ajusta. Es un DTO amable para JSON (enums como texto), distinto del ReportQuerySpec interno,
// para desacoplar el esquema que ve la IA de los tipos del motor. NO lleva SQL ni columnas fisicas:
// solo nombres logicos del catalogo, validados antes de ejecutarse.

public enum ReportChartKind
{
    Table,
    Bar,
    Pie,
    Line
}

public sealed class ReportFilterSpec
{
    public string Field { get; set; } = "";
    public ReportFilterOperator Op { get; set; } = ReportFilterOperator.Equals;
    public List<string?> Values { get; set; } = new();
}

public sealed class ReportAggregateSpec
{
    public string Field { get; set; } = "";
    public ReportAggregateFunction Function { get; set; } = ReportAggregateFunction.Count;
}

public sealed class ReportSortSpec
{
    public string Field { get; set; } = "";
    public bool Desc { get; set; }
}

/// <summary>
/// La definicion declarativa de un reporte (dashboard). El titulo + la consulta (fuente/campos/
/// filtros/agrupacion/agregados/orden) + la presentacion (tipo de grafico). Se guarda como SpecJson
/// en <c>ReportDefinition</c> y se ejecuta traduciendolo a <see cref="ReportQuerySpec"/>.
/// </summary>
public sealed class ReportSpec
{
    public string Title { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public ReportChartKind Chart { get; set; } = ReportChartKind.Table;

    public List<string> Fields { get; set; } = new();
    public List<ReportFilterSpec> Filters { get; set; } = new();
    public List<string> GroupBy { get; set; } = new();
    public List<ReportAggregateSpec> Aggregates { get; set; } = new();
    public List<ReportSortSpec> Sort { get; set; } = new();
    public int? Top { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ReportSpec? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ReportSpec>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Traduce al spec interno del motor (que el datasource valida contra el catalogo).</summary>
    public ReportQuerySpec ToQuerySpec() => new()
    {
        SourceKey = SourceKey,
        Fields = Fields.ToArray(),
        Filters = Filters.Select(f => new ReportFilter(f.Field, f.Op, f.Values.ToArray())).ToArray(),
        GroupBy = GroupBy.ToArray(),
        Aggregates = Aggregates.Select(a => new ReportAggregate(a.Field, a.Function)).ToArray(),
        Sort = Sort.Select(s => new ReportSort(s.Field, s.Desc)).ToArray(),
        Top = Top
    };
}
