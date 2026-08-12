using System.Globalization;

namespace Ecorex.Application.Reporting.Panels;

// Motor de pivoteo EN MEMORIA del renderizador generico (ADR-0066). Puro y sin dependencias de UI ni
// EF: recibe filas ya materializadas (una consulta tabular por fuente + join/derivados aplicados) y
// calcula KPIs, series de graficos, matriz y tablas. Es el nucleo NUMERICO que reproduce 1:1 lo que
// hoy hacen a mano OcsDashboardPanel / ActivitiesDashboardPanel / SiigoVentasDashboardPanel, por eso
// se aisla aqui: es testeable sin Docker ni Blazor. Cultura invariante para que PG y SQL Server
// coincidan.

/// <summary>Una fila logica del panel: valores por nombre de campo (fuente + alias de lookup + derivados).</summary>
public sealed class PanelRow : Dictionary<string, object?>
{
    public PanelRow() : base(StringComparer.OrdinalIgnoreCase) { }
}

/// <summary>Un corte (categoria + valor agregado) de un widget.</summary>
public sealed record PanelSlice(string Key, double Value);

/// <summary>Como ordenar los cortes de un widget.</summary>
public enum PanelSliceOrder
{
    ValueDesc,
    DimAsc,
    DimDesc
}

/// <summary>Resultado de una matriz cruzada (para tabla con heatmap).</summary>
public sealed class PanelMatrix
{
    public required IReadOnlyList<string> RowKeys { get; init; }
    public required IReadOnlyList<string> ColKeys { get; init; }
    public required IReadOnlyDictionary<(string Row, string Col), double> Cells { get; init; }
    public double Max { get; init; }

    public double Cell(string row, string col) => Cells.TryGetValue((row, col), out var v) ? v : 0d;
    public double RowTotal(string row) => ColKeys.Sum(c => Cell(row, c));
    public double ColTotal(string col) => RowKeys.Sum(r => Cell(r, col));
    public double Grand => Cells.Values.Sum();
}

public static class PanelDataEngine
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- Conversores robustos (los valores llegan tipados de la fuente, o como texto del EAV) ----

    public static string Norm(object? v) => (v?.ToString() ?? "").Trim();

    public static decimal? AsDecimal(object? v)
    {
        switch (v)
        {
            case null:
                return null;
            case decimal d:
                return d;
            case double db:
                return (decimal)db;
            case float f:
                return (decimal)f;
            case long l:
                return l;
            case int i:
                return i;
            case short s:
                return s;
        }

        var raw = v.ToString();
        return decimal.TryParse(raw, NumberStyles.Any, Inv, out var r) ? r : null;
    }

    public static DateTimeOffset? AsDate(object? v)
    {
        switch (v)
        {
            case null:
                return null;
            case DateTimeOffset dto:
                return dto;
            case DateTime dt:
                return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        return DateTimeOffset.TryParse(v.ToString(), Inv, DateTimeStyles.AssumeUniversal, out var r) ? r : null;
    }

    /// <summary>Deriva un bucket de fecha. Devuelve null si el valor no es una fecha.</summary>
    public static string? Derive(object? dateValue, string op)
    {
        var d = AsDate(dateValue);
        if (d is null)
        {
            return null;
        }

        var v = d.Value;
        return (op ?? "").Trim().ToLowerInvariant() switch
        {
            "year" => v.Year.ToString(Inv),
            "yyyymm" => v.ToString("yyyy-MM", Inv),
            "month" => v.ToString("MM", Inv),
            "date" => v.ToString("yyyy-MM-dd", Inv),
            _ => null
        };
    }

    // ---- Agregaciones ----

    /// <summary>Agrega un conjunto de filas: count / sum / countDistinct / avg. countDistinct y count
    /// ignoran el campo cuando no aplica. Devuelve 0 si no hay datos numericos.</summary>
    public static decimal Aggregate(IEnumerable<PanelRow> rows, string? agg, string? field)
    {
        var op = (agg ?? "count").Trim().ToLowerInvariant();
        switch (op)
        {
            case "count":
                return rows.Count();
            case "countdistinct":
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in rows)
                {
                    var s = Norm(field is null ? null : r.GetValueOrDefault(field));
                    if (s.Length > 0)
                    {
                        set.Add(s);
                    }
                }

                return set.Count;
            }
            case "sum":
            {
                if (field is null)
                {
                    return 0m;
                }

                decimal total = 0m;
                foreach (var r in rows)
                {
                    total += AsDecimal(r.GetValueOrDefault(field)) ?? 0m;
                }

                return total;
            }
            case "avg":
            {
                if (field is null)
                {
                    return 0m;
                }

                var vals = rows.Select(r => AsDecimal(r.GetValueOrDefault(field))).Where(x => x.HasValue)
                    .Select(x => x!.Value).ToList();
                return vals.Count == 0 ? 0m : Math.Round(vals.Sum() / vals.Count, 0);
            }
            default:
                return rows.Count();
        }
    }

    /// <summary>Agrupa por una dimension y agrega la medida; aplica escala (divisor), orden y top N.</summary>
    public static List<PanelSlice> GroupAggregate(
        IEnumerable<PanelRow> rows, string dim, string? agg, string? field,
        double scale = 1d, int? topN = null, PanelSliceOrder order = PanelSliceOrder.ValueDesc,
        bool includeBlank = false, string blankLabel = "(sin)")
    {
        var groups = new Dictionary<string, List<PanelRow>>(StringComparer.OrdinalIgnoreCase);
        var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var keyRaw = Norm(r.GetValueOrDefault(dim));
            if (keyRaw.Length == 0)
            {
                if (!includeBlank)
                {
                    continue;
                }

                keyRaw = blankLabel;
            }

            if (!groups.TryGetValue(keyRaw, out var list))
            {
                groups[keyRaw] = list = new List<PanelRow>();
                display[keyRaw] = keyRaw;
            }

            list.Add(r);
        }

        var slices = groups.Select(kv =>
        {
            var value = (double)Aggregate(kv.Value, agg, field);
            if (scale is not 0d and not 1d)
            {
                value /= scale;
            }

            return new PanelSlice(display[kv.Key], value);
        });

        slices = order switch
        {
            PanelSliceOrder.DimAsc => slices.OrderBy(s => s.Key, StringComparer.Ordinal),
            PanelSliceOrder.DimDesc => slices.OrderByDescending(s => s.Key, StringComparer.Ordinal),
            _ => slices.OrderByDescending(s => s.Value)
        };

        var list2 = slices.ToList();
        if (topN is int n && n >= 0 && order == PanelSliceOrder.ValueDesc)
        {
            list2 = list2.Take(n).ToList();
        }

        return list2;
    }

    /// <summary>Pareto: top N por medida (desc) + porcentaje acumulado sobre el gran total (todas las
    /// categorias, no solo el top). Devuelve categorias, barras (escaladas) y acumulado en %.</summary>
    public static (List<string> Cats, List<double> Bars, List<double> Cum) Pareto(
        IEnumerable<PanelRow> rows, string dim, string? agg, string? field, double scale = 1d, int? topN = 20)
    {
        var all = GroupAggregate(rows, dim, agg, field, scale: 1d, topN: null, order: PanelSliceOrder.ValueDesc,
            includeBlank: true, blankLabel: "(sin)");
        var grand = all.Sum(s => s.Value);
        var top = topN is int n && n >= 0 ? all.Take(n).ToList() : all;

        var cats = new List<string>(top.Count);
        var bars = new List<double>(top.Count);
        var cum = new List<double>(top.Count);
        var running = 0d;
        foreach (var s in top)
        {
            running += s.Value;
            cats.Add(s.Key);
            bars.Add(scale is not 0d and not 1d ? s.Value / scale : s.Value);
            cum.Add(grand > 0d ? running / grand * 100d : 0d);
        }

        return (cats, bars, cum);
    }

    /// <summary>Matriz cruzada rowDim x colDim con una agregacion por celda. Filas ordenadas por total
    /// desc (top), columnas por total desc.</summary>
    public static PanelMatrix Matrix(
        IEnumerable<PanelRow> rows, string rowDim, string colDim, string? agg, string? field,
        int rowTop = 10)
    {
        var cells = new Dictionary<(string, string), double>();
        var rowTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var colTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var buckets = new Dictionary<(string, string), List<PanelRow>>();

        foreach (var r in rows)
        {
            var rk = Norm(r.GetValueOrDefault(rowDim));
            var ck = Norm(r.GetValueOrDefault(colDim));
            if (rk.Length == 0 || ck.Length == 0)
            {
                continue;
            }

            if (!buckets.TryGetValue((rk, ck), out var list))
            {
                buckets[(rk, ck)] = list = new List<PanelRow>();
            }

            list.Add(r);
        }

        foreach (var kv in buckets)
        {
            var value = (double)Aggregate(kv.Value, agg, field);
            cells[kv.Key] = value;
            rowTotals[kv.Key.Item1] = rowTotals.GetValueOrDefault(kv.Key.Item1) + value;
            colTotals[kv.Key.Item2] = colTotals.GetValueOrDefault(kv.Key.Item2) + value;
        }

        var rowKeys = rowTotals.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(rowTop).ToList();
        var colKeys = colTotals.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        var max = cells.Count > 0 ? cells.Values.Max() : 0d;

        return new PanelMatrix { RowKeys = rowKeys, ColKeys = colKeys, Cells = cells, Max = max };
    }

    /// <summary>Filas de una tabla agrupada. Cada columna es un campo directo (primer valor del grupo)
    /// o una agregacion. Ordena por la primera columna agregada (desc) si existe. Devuelve, por fila,
    /// el valor CRUDO de cada columna (decimal para agregadas, string para directas) para que el
    /// formateo lo aplique la capa de presentacion.</summary>
    public static List<List<object?>> TableRows(
        IEnumerable<PanelRow> rows, string groupBy, IReadOnlyList<PanelColumn> columns, int? topN)
    {
        var groups = new Dictionary<string, List<PanelRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var key = Norm(r.GetValueOrDefault(groupBy));
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = new List<PanelRow>();
            }

            list.Add(r);
        }

        var firstAggIdx = -1;
        for (var i = 0; i < columns.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(columns[i].Agg))
            {
                firstAggIdx = i;
                break;
            }
        }

        var result = new List<List<object?>>();
        foreach (var g in groups)
        {
            var cells = new List<object?>(columns.Count);
            foreach (var col in columns)
            {
                if (!string.IsNullOrWhiteSpace(col.Agg))
                {
                    cells.Add(Aggregate(g.Value, col.Agg, col.AggField));
                }
                else
                {
                    var f = col.Field ?? groupBy;
                    cells.Add(g.Value.Count > 0 ? g.Value[0].GetValueOrDefault(f) : null);
                }
            }

            result.Add(cells);
        }

        if (firstAggIdx >= 0)
        {
            result = result
                .OrderByDescending(row => row[firstAggIdx] is decimal d ? d : 0m)
                .ToList();
        }

        if (topN is int n && n >= 0)
        {
            result = result.Take(n).ToList();
        }

        return result;
    }

    // ---- Formato de valores (dinero / millones / porcentaje / entero) ----

    public static string Format(decimal value, string? format)
    {
        switch ((format ?? "int").Trim().ToLowerInvariant())
        {
            case "money":
                return "$" + value.ToString("N0", Inv);
            case "moneym":
                return "$" + (value / 1_000_000m).ToString("N0", Inv) + " M";
            case "percent":
                return value.ToString("0.#", Inv) + "%";
            default:
                return value.ToString("N0", Inv);
        }
    }
}
