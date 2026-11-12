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

    /// <summary>Segunda agrupacion: series (linea/area/tabla) o columnas (mapa de calor). Solo fuentes nativas.</summary>
    public string? Dimension2 { get; init; }

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

    /// <summary>Velocimetro (Ola 7b): valor objetivo = tope de la escala. Sin meta se usa el umbral alto.</summary>
    public decimal? Meta { get; init; }

    /// <summary>
    /// KPI/velocimetro (Ola 7b): periodo a medir (<see cref="KpiPeriodos"/>: mes, trimestre, anio, 30d, 7d).
    /// Con periodo, el KPI muestra la variacion vs el anterior y su tendencia. Null = total sin comparar.
    /// </summary>
    public string? Periodo { get; init; }

    /// <summary>Definicion del widget COMBINADO (Ola 5); null en los widgets de una sola fuente.</summary>
    public CombinadoDiseno? Combinado { get; init; }

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
            Tono = Tonos.Contains(Tono) ? Tono : null,
            Meta = Meta is > 0 ? Meta : null,
            Periodo = KpiPeriodos.EsValido(Periodo) ? Periodo : null
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
    /// <summary>Tipos que ofrece el modal (en este orden) y si exigen dimension. Echarts (8) es reservado.</summary>
    public static readonly IReadOnlyList<(AnalisisTipoWidget Tipo, string Nombre, bool PideDimension)> Tipos = new[]
    {
        (AnalisisTipoWidget.Kpi, "KPI", false),
        (AnalisisTipoWidget.Velocimetro, "Velocimetro", false),
        (AnalisisTipoWidget.Barras, "Barras", true),
        (AnalisisTipoWidget.BarrasHorizontales, "Barras horizontales", true),
        (AnalisisTipoWidget.BarrasApiladas, "Barras apiladas", true),
        (AnalisisTipoWidget.BarrasLinea, "Barras + linea", true),
        (AnalisisTipoWidget.Dona, "Dona", true),
        (AnalisisTipoWidget.Embudo, "Embudo", true),
        (AnalisisTipoWidget.Treemap, "Mapa de arbol", true),
        (AnalisisTipoWidget.Linea, "Linea", true),
        (AnalisisTipoWidget.Area, "Area", true),
        (AnalisisTipoWidget.Heatmap, "Mapa de calor", true),
        (AnalisisTipoWidget.Calendario, "Calendario", false),
        (AnalisisTipoWidget.Semaforo, "Semaforo", true),
        (AnalisisTipoWidget.Tabla, "Tabla", true)
    };

    /// <summary>Tipos validos para guardar/importar (todos los ofrecidos; no el reservado ni valores fuera del enum).</summary>
    public static bool EsTipoValido(AnalisisTipoWidget tipo) => Tipos.Any(t => t.Tipo == tipo);

    public static bool PideDimension(AnalisisTipoWidget tipo) =>
        Tipos.FirstOrDefault(t => t.Tipo == tipo).PideDimension;

    /// <summary>Un solo numero (KPI clasico o velocimetro): admite umbrales y periodo.</summary>
    public static bool EsEscalar(AnalisisTipoWidget tipo) =>
        tipo is AnalisisTipoWidget.Kpi or AnalisisTipoWidget.Velocimetro;

    /// <summary>Tipos que agregan sobre el CAMPO DE FECHA de la fuente (no agrupan por una dimension).</summary>
    public static bool UsaCampoFecha(AnalisisTipoWidget tipo) => tipo == AnalisisTipoWidget.Calendario;

    /// <summary>Tipos que aceptan una segunda agrupacion (series, columnas, pila o segundo nivel).</summary>
    public static bool AdmiteDimension2(AnalisisTipoWidget tipo) =>
        tipo is AnalisisTipoWidget.Linea or AnalisisTipoWidget.Area or AnalisisTipoWidget.Heatmap or AnalisisTipoWidget.Tabla
            or AnalisisTipoWidget.BarrasApiladas or AnalisisTipoWidget.Treemap;

    /// <summary>El mapa de calor necesita filas Y columnas.</summary>
    public static bool PideDimension2(AnalisisTipoWidget tipo) => tipo == AnalisisTipoWidget.Heatmap;

    /// <summary>Series, mapa de calor y pilas se leen en orden del eje (cronologico), no por valor.</summary>
    public static bool OrdenPorEjePorDefecto(AnalisisTipoWidget tipo) =>
        tipo is AnalisisTipoWidget.Linea or AnalisisTipoWidget.Area or AnalisisTipoWidget.Heatmap or AnalisisTipoWidget.BarrasApiladas;

    /// <summary>Ancho inicial sugerido al elegir el tipo (el usuario lo puede cambiar).</summary>
    public static int AnchoSugerido(AnalisisTipoWidget tipo) => tipo switch
    {
        AnalisisTipoWidget.Calendario => 4,
        AnalisisTipoWidget.Linea or AnalisisTipoWidget.Area or AnalisisTipoWidget.Heatmap or AnalisisTipoWidget.Tabla
            or AnalisisTipoWidget.BarrasApiladas or AnalisisTipoWidget.BarrasLinea or AnalisisTipoWidget.Treemap => 2,
        _ => 1
    };

    public static string AggregateKey(ReportAggregateFunction fn, string? campo) =>
        fn == ReportAggregateFunction.Count ? "Count" : $"{fn}_{campo}";

    public static ReportQuerySpec ToSpec(WidgetDiseno d)
    {
        var agg = new ReportAggregate(d.Funcion == ReportAggregateFunction.Count ? string.Empty : d.CampoMedida ?? string.Empty, d.Funcion);
        var dim = string.IsNullOrWhiteSpace(d.Dimension) ? null : d.Dimension;
        var dim2 = dim is not null && AdmiteDimension2(d.Tipo) && !string.IsNullOrWhiteSpace(d.Dimension2) ? d.Dimension2 : null;
        var grupos = dim is null ? Array.Empty<string>() : dim2 is null ? new[] { dim } : new[] { dim, dim2 };
        var orden = dim is null
            ? Array.Empty<ReportSort>()
            : d.OrdenarPorMedida
                ? new[] { new ReportSort(AggregateKey(d.Funcion, d.CampoMedida), Descending: true) }
                : grupos.Select(g => new ReportSort(g)).ToArray();
        return new ReportQuerySpec
        {
            SourceKey = d.SourceKey,
            Filters = d.Filtros.Select(ToFilter).ToList(),
            GroupBy = grupos,
            Aggregates = new[] { agg },
            Sort = orden
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
            Dimension2 = spec?.GroupBy.ElementAtOrDefault(1),
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
    /// <param name="bindingsJson">ContextBindingsJson del widget (campo de fecha elegido), para los tipos
    /// que agregan sobre la fecha (calendario, KPI con periodo).</param>
    public static IReadOnlyList<string> Validar(WidgetDiseno d, ReportSourceDescriptor? fuente, string? bindingsJson = null)
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Titulo))
        {
            errores.Add("Ponle un titulo al widget.");
        }

        if (!EsTipoValido(d.Tipo))
        {
            errores.Add("Elige un tipo de widget.");
        }

        if (fuente is null)
        {
            errores.Add("Elige una fuente de datos.");
            return errores;
        }

        var necesitaFecha = UsaCampoFecha(d.Tipo) || (EsEscalar(d.Tipo) && KpiPeriodos.EsValido(d.Config.Periodo));
        if (necesitaFecha && HojaFiltroBinder.Resolver(HojaFiltroBinder.Fecha, fuente, bindingsJson) is null)
        {
            errores.Add(UsaCampoFecha(d.Tipo)
                ? "El calendario necesita un campo de fecha: elige uno en Presentacion."
                : "Para comparar periodos se necesita un campo de fecha: elige uno en Presentacion.");
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

        var dim2 = AdmiteDimension2(d.Tipo) && !string.IsNullOrWhiteSpace(d.Dimension) ? d.Dimension2 : null;
        if (string.IsNullOrWhiteSpace(dim2))
        {
            if (PideDimension2(d.Tipo))
            {
                errores.Add("El mapa de calor necesita un segundo campo para las columnas.");
            }
        }
        else if (fuente.Kind != ReportSourceKind.Native)
        {
            errores.Add("Esta fuente solo admite una agrupacion.");
        }
        else if (fuente.FindField(dim2) is not { CanGroup: true })
        {
            errores.Add($"No se puede agrupar por '{dim2}'.");
        }
        else if (string.Equals(dim2, d.Dimension, StringComparison.OrdinalIgnoreCase))
        {
            errores.Add("Las dos agrupaciones deben ser campos distintos.");
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
        new(d.Tipo, d.Titulo, ToSpec(d), null, d.ColSpan, LimpiarConfig(d.Tipo, d.Config).ToJson(), contextBindingsJson);

    /// <summary>Quita de la presentacion lo que el tipo no usa (periodo/meta fuera de KPI y velocimetro).</summary>
    public static WidgetConfig LimpiarConfig(AnalisisTipoWidget tipo, WidgetConfig c) => c with
    {
        Periodo = EsEscalar(tipo) ? c.Periodo : null,
        Meta = tipo == AnalisisTipoWidget.Velocimetro ? c.Meta : null
    };
}
