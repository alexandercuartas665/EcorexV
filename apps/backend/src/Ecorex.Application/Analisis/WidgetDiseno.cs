using System.Text.Json;
using Ecorex.Application.Reporting;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Analisis;

/// <summary>Un filtro elegido en el modal del widget (campo + operador + valor/es).</summary>
public sealed record WidgetFiltroDiseno(string Campo, ReportFilterOperator Operador, string? Valor, string? Valor2 = null);

/// <summary>
/// Lo que el usuario arma en el modal "Agregar / editar widget" (ADR-0158 Ola 3): tipo -> fuente ->
/// medida (funcion + campo) -> dimension -> filtros -> presentacion. Se traduce a un
/// <see cref="ReportQuerySpec"/> (lo que se guarda) y se reconstruye desde el para editar.
/// </summary>
public sealed record WidgetDiseno
{
    public AnalisisTipoWidget Tipo { get; init; } = AnalisisTipoWidget.Kpi;

    public string Titulo { get; init; } = string.Empty;

    public string SourceKey { get; init; } = string.Empty;

    public ReportAggregateFunction Funcion { get; init; } = ReportAggregateFunction.Count;

    /// <summary>Campo medido (null con Count).</summary>
    public string? CampoMedida { get; init; }

    /// <summary>Campo por el que se agrupa (obligatorio en barras/dona/linea; opcional en KPI).</summary>
    public string? Dimension { get; init; }

    public IReadOnlyList<WidgetFiltroDiseno> Filtros { get; init; } = Array.Empty<WidgetFiltroDiseno>();

    /// <summary>true = ordena por la medida de mayor a menor; false = por la dimension (eje cronologico).</summary>
    public bool OrdenarPorMedida { get; init; } = true;

    public int ColSpan { get; init; } = 1;

    public WidgetConfig Config { get; init; } = new();
}

/// <summary>Presentacion del widget, guardada en <c>AnalisisWidget.ConfigJson</c>.</summary>
public sealed record WidgetConfig
{
    /// <summary>Categorias visibles antes de agrupar el resto en "Otros" (barras/dona).</summary>
    public int? Top { get; init; }

    /// <summary>numero | decimal | moneda | porcentaje.</summary>
    public string? Formato { get; init; }

    /// <summary>Tono de la paleta del workspace: blue, violet, green, amber, rose, slate.</summary>
    public string? Tono { get; init; }

    /// <summary>Umbrales del KPI: por debajo de Bajo = alerta, desde Alto = bien (o al reves si MayorEsMejor=false).</summary>
    public decimal? UmbralBajo { get; init; }

    public decimal? UmbralAlto { get; init; }

    public bool MayorEsMejor { get; init; } = true;

    public static readonly string[] Tonos = { "blue", "violet", "green", "amber", "rose", "slate" };

    public static readonly string[] Formatos = { "numero", "decimal", "moneda", "porcentaje" };

    public static WidgetConfig Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new WidgetConfig();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new WidgetConfig();
            }

            return JsonSerializer.Deserialize<WidgetConfig>(json, AnalisisJson.Options) ?? new WidgetConfig();
        }
        catch (JsonException)
        {
            return new WidgetConfig();
        }
    }

    public string? ToJson()
    {
        var limpio = this with
        {
            Top = Top is > 0 ? Top : null,
            Formato = Formatos.Contains(Formato) ? Formato : null,
            Tono = Tonos.Contains(Tono) ? Tono : null
        };
        return limpio == new WidgetConfig() ? null : JsonSerializer.Serialize(limpio, AnalisisJson.Options);
    }

    /// <summary>Tono del KPI segun los umbrales (null si no hay umbrales o no hay valor).</summary>
    public string? TonoPorUmbral(decimal? valor)
    {
        if (valor is not decimal v || (UmbralBajo is null && UmbralAlto is null))
        {
            return null;
        }

        var malo = MayorEsMejor ? "rose" : "green";
        var bueno = MayorEsMejor ? "green" : "rose";
        if (UmbralBajo is decimal b && v < b)
        {
            return malo;
        }

        if (UmbralAlto is decimal a && v >= a)
        {
            return bueno;
        }

        return "amber";
    }
}

/// <summary>Traduccion WidgetDiseno &lt;-&gt; ReportQuerySpec + validacion contra el catalogo. Puro.</summary>
public static class WidgetDisenoMapper
{
    /// <summary>Tipos que el modal ofrece en esta ola y si exigen dimension.</summary>
    public static readonly IReadOnlyList<(AnalisisTipoWidget Tipo, string Nombre, bool PideDimension)> Tipos = new[]
    {
        (AnalisisTipoWidget.Kpi, "KPI", false),
        (AnalisisTipoWidget.Barras, "Barras", true),
        (AnalisisTipoWidget.Dona, "Dona", true)
    };

    public static bool PideDimension(AnalisisTipoWidget tipo) =>
        Tipos.FirstOrDefault(t => t.Tipo == tipo).PideDimension;

    public static string AggregateKey(ReportAggregateFunction fn, string? campo) =>
        fn == ReportAggregateFunction.Count ? "Count" : $"{fn}_{campo}";

    public static ReportQuerySpec ToSpec(WidgetDiseno d)
    {
        var agg = new ReportAggregate(d.Funcion == ReportAggregateFunction.Count ? string.Empty : d.CampoMedida ?? string.Empty, d.Funcion);
        var dim = string.IsNullOrWhiteSpace(d.Dimension) ? null : d.Dimension;
        return new ReportQuerySpec
        {
            SourceKey = d.SourceKey,
            Filters = d.Filtros.Select(ToFilter).ToList(),
            GroupBy = dim is null ? Array.Empty<string>() : new[] { dim },
            Aggregates = new[] { agg },
            Sort = dim is null
                ? Array.Empty<ReportSort>()
                : new[] { d.OrdenarPorMedida ? new ReportSort(AggregateKey(d.Funcion, d.CampoMedida), Descending: true) : new ReportSort(dim) }
        };
    }

    public static ReportFilter ToFilter(WidgetFiltroDiseno f) => f.Operador switch
    {
        ReportFilterOperator.Between => new ReportFilter(f.Campo, f.Operador, new[] { f.Valor, f.Valor2 }),
        ReportFilterOperator.In => new ReportFilter(f.Campo, f.Operador,
            (f.Valor ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)),
        _ => new ReportFilter(f.Campo, f.Operador, new[] { f.Valor })
    };

    /// <summary>Reconstruye el diseno desde un widget guardado (para editarlo).</summary>
    public static WidgetDiseno FromWidget(AnalisisWidgetDto w)
    {
        var spec = AnalisisJson.DeserializeSpec(w.SpecJson);
        var agg = spec?.Aggregates.FirstOrDefault();
        var dim = spec?.GroupBy.FirstOrDefault();
        return new WidgetDiseno
        {
            Tipo = w.Tipo,
            Titulo = w.Titulo,
            SourceKey = spec?.SourceKey ?? string.Empty,
            Funcion = agg?.Function ?? ReportAggregateFunction.Count,
            CampoMedida = agg is null || agg.Function == ReportAggregateFunction.Count ? null : agg.FieldKey,
            Dimension = dim,
            Filtros = spec?.Filters.Select(f => new WidgetFiltroDiseno(
                f.FieldKey, f.Operator,
                f.Operator == ReportFilterOperator.In ? string.Join(", ", f.Values) : f.Values.ElementAtOrDefault(0),
                f.Values.ElementAtOrDefault(1))).ToList() ?? new List<WidgetFiltroDiseno>(),
            OrdenarPorMedida = spec is null || spec.Sort.Count == 0 || !string.Equals(spec.Sort[0].FieldKey, dim, StringComparison.OrdinalIgnoreCase),
            ColSpan = w.ColSpan,
            Config = WidgetConfig.Leer(w.ConfigJson)
        };
    }

    /// <summary>Errores del diseno frente al descriptor de la fuente (vacio = valido).</summary>
    public static IReadOnlyList<string> Validar(WidgetDiseno d, ReportSourceDescriptor? fuente)
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Titulo))
        {
            errores.Add("Ponle un titulo al widget.");
        }

        if (fuente is null)
        {
            errores.Add("Elige una fuente de datos.");
            return errores;
        }

        if (d.Funcion != ReportAggregateFunction.Count)
        {
            var campo = string.IsNullOrWhiteSpace(d.CampoMedida) ? null : fuente.FindField(d.CampoMedida);
            if (campo is null || !campo.CanAggregate)
            {
                errores.Add("La medida necesita un campo numerico de la fuente.");
            }
        }

        if (string.IsNullOrWhiteSpace(d.Dimension))
        {
            if (PideDimension(d.Tipo))
            {
                errores.Add("Elige por que campo agrupar.");
            }
        }
        else if (fuente.FindField(d.Dimension) is not { CanGroup: true })
        {
            errores.Add($"No se puede agrupar por '{d.Dimension}'.");
        }

        foreach (var f in d.Filtros)
        {
            if (fuente.FindField(f.Campo) is not { CanFilter: true })
            {
                errores.Add($"No se puede filtrar por '{f.Campo}'.");
            }
            else if (string.IsNullOrWhiteSpace(f.Valor) || (f.Operador == ReportFilterOperator.Between && string.IsNullOrWhiteSpace(f.Valor2)))
            {
                errores.Add($"Completa el valor del filtro '{f.Campo}'.");
            }
        }

        return errores;
    }

    /// <summary>Widget listo para guardar a partir del diseno.</summary>
    public static AnalisisNuevoWidget ToNuevoWidget(WidgetDiseno d, string? contextBindingsJson = null) =>
        new(d.Tipo, d.Titulo, ToSpec(d), null, d.ColSpan, d.Config.ToJson(), contextBindingsJson);
}
