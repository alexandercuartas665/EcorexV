using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Analisis;

// Formas NORMALIZADAS que pinta un widget del modulo Analisis (ADR-0158 D1). Son independientes de la
// fuente: un spec del motor de reportes o un indicador curado terminan en una de estas formas.

/// <summary>Base comun de las formas de widget.</summary>
public abstract record WidgetShape;

/// <summary>Un numero (KPI). <see cref="Delta"/> opcional = variacion vs periodo anterior.</summary>
public sealed record EscalarShape(decimal? Valor, string Etiqueta, decimal? Delta = null, string? Tono = null) : WidgetShape;

/// <summary>Un par etiqueta-valor de <see cref="CategoriasShape"/>. <see cref="Tono"/> opcional para semaforos.</summary>
public sealed record CategoriaItem(string Etiqueta, decimal Valor, string? Tono = null);

/// <summary>Barras o dona: etiquetas con un valor cada una.</summary>
public sealed record CategoriasShape(IReadOnlyList<CategoriaItem> Items) : WidgetShape
{
    public decimal Total => Items.Sum(i => i.Valor);
}

/// <summary>Una serie con un valor por categoria del eje X (null = sin dato).</summary>
public sealed record SerieDatos(string Nombre, IReadOnlyList<decimal?> Valores);

/// <summary>Linea/area/barras agrupadas: eje X comun + una o varias series alineadas.</summary>
public sealed record SerieShape(IReadOnlyList<string> Ejes, IReadOnlyList<SerieDatos> Series) : WidgetShape;

/// <summary>Mapa de calor: filas x columnas con un valor por celda (null = sin dato).</summary>
public sealed record HeatmapShape(
    IReadOnlyList<string> Filas,
    IReadOnlyList<string> Columnas,
    IReadOnlyList<IReadOnlyList<decimal?>> Valores) : WidgetShape;

/// <summary>Tabla tal cual el dataset.</summary>
public sealed record TablaShape(IReadOnlyList<ReportColumn> Columnas, IReadOnlyList<IReadOnlyList<object?>> Filas) : WidgetShape;

/// <summary>
/// Conversion ReportDataSet -> forma. Convencion: las columnas de agrupacion van primero y la medida
/// es la ULTIMA columna numerica (asi salen los datasets agregados del motor). Etiquetas nulas o vacias
/// se muestran como "(sin dato)". Puro: sin BD, testeable.
/// </summary>
public static class WidgetShapes
{
    public const string SinDato = "(sin dato)";

    /// <summary>KPI: suma de la medida de todas las filas (un dataset agregado sin grupos tiene 1 fila).</summary>
    public static EscalarShape ToEscalar(ReportDataSet ds, string etiqueta)
    {
        // Sin filas = no hubo nada que contar ni sumar: el KPI vale 0 (no "sin dato").
        if (ds.RowCount == 0)
        {
            return new EscalarShape(0m, etiqueta);
        }

        var m = MeasureIndex(ds);
        if (m < 0)
        {
            return new EscalarShape(ds.RowCount, etiqueta);
        }

        var values = ds.Rows.Select(r => InMemoryReportEngine.ToDecimal(r[m])).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return new EscalarShape(values.Count == 0 ? null : values.Sum(), etiqueta);
    }

    /// <summary>Barras/dona: primera columna = etiqueta, medida = valor. Etiquetas repetidas se suman.</summary>
    public static CategoriasShape ToCategorias(ReportDataSet ds, int? top = null)
    {
        var m = MeasureIndex(ds);
        if (m < 0 || ds.Columns.Count < 2)
        {
            return new CategoriasShape(Array.Empty<CategoriaItem>());
        }

        var items = ds.Rows
            .GroupBy(r => Label(r[0]))
            .Select(g => new CategoriaItem(g.Key, g.Sum(r => InMemoryReportEngine.ToDecimal(r[m]) ?? 0m)))
            .OrderByDescending(i => i.Valor)
            .ToList();

        if (top is int t && t > 0 && items.Count > t)
        {
            // Lo que no cabe en el top se agrupa en "Otros" para no perder el total.
            var otros = items.Skip(t).Sum(i => i.Valor);
            items = items.Take(t).Append(new CategoriaItem("Otros", otros)).ToList();
        }

        return new CategoriasShape(items);
    }

    /// <summary>
    /// Serie: columna 0 = eje X (orden ascendente, p.ej. "2026-01"); si hay columna 1 de agrupacion,
    /// define una serie por valor; si no, una sola serie con el nombre de la medida.
    /// </summary>
    public static SerieShape ToSerie(ReportDataSet ds)
    {
        var m = MeasureIndex(ds);
        if (m < 0 || ds.Columns.Count < 2)
        {
            return new SerieShape(Array.Empty<string>(), Array.Empty<SerieDatos>());
        }

        var ejes = ds.Rows.Select(r => Label(r[0])).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var pos = ejes.Select((e, i) => (e, i)).ToDictionary(p => p.e, p => p.i);

        var hasSeriesCol = m >= 2;
        var bySerie = ds.Rows.GroupBy(r => hasSeriesCol ? Label(r[1]) : ds.Columns[m].DisplayName);

        var series = new List<SerieDatos>();
        foreach (var g in bySerie.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var vals = new decimal?[ejes.Count];
            foreach (var r in g)
            {
                var i = pos[Label(r[0])];
                var v = InMemoryReportEngine.ToDecimal(r[m]);
                vals[i] = v is null ? vals[i] : (vals[i] ?? 0m) + v;
            }

            series.Add(new SerieDatos(g.Key, vals));
        }

        return new SerieShape(ejes, series);
    }

    /// <summary>Heatmap: columna 0 = fila, columna 1 = columna, medida = celda.</summary>
    public static HeatmapShape ToHeatmap(ReportDataSet ds)
    {
        var m = MeasureIndex(ds);
        if (m < 2)
        {
            return new HeatmapShape(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<IReadOnlyList<decimal?>>());
        }

        var filas = ds.Rows.Select(r => Label(r[0])).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var cols = ds.Rows.Select(r => Label(r[1])).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var fi = filas.Select((f, i) => (f, i)).ToDictionary(p => p.f, p => p.i);
        var ci = cols.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i);

        var grid = filas.Select(_ => new decimal?[cols.Count]).ToArray();
        foreach (var r in ds.Rows)
        {
            var v = InMemoryReportEngine.ToDecimal(r[m]);
            if (v is null)
            {
                continue;
            }

            ref var cell = ref grid[fi[Label(r[0])]][ci[Label(r[1])]];
            cell = (cell ?? 0m) + v;
        }

        return new HeatmapShape(filas, cols, grid.Select(a => (IReadOnlyList<decimal?>)a).ToList());
    }

    public static TablaShape ToTabla(ReportDataSet ds) => new(ds.Columns, ds.Rows);

    private static int MeasureIndex(ReportDataSet ds)
    {
        for (var i = ds.Columns.Count - 1; i >= 0; i--)
        {
            if (ds.Columns[i].Type is ReportFieldType.Number or ReportFieldType.Decimal)
            {
                return i;
            }
        }

        return -1;
    }

    private static string Label(object? v) => v switch
    {
        null => SinDato,
        string s when string.IsNullOrWhiteSpace(s) => SinDato,
        DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "Si" : "No",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? SinDato
    };
}
