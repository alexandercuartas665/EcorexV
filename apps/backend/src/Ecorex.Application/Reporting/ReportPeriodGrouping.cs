using System.Globalization;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Reporting;

/// <summary>Periodo de calendario para agrupar un campo FECHA ("ventas por mes").</summary>
public enum ReportDatePeriod
{
    Day,
    Week,
    Month,
    Quarter,
    Year
}

/// <summary>
/// AGRUPAR POR PERIODO (ADR-0206). Cuando un spec agrupa por un campo fecha con <see cref="ReportQuerySpec.GroupPeriod"/>,
/// el despachador (<see cref="ReportDataSource"/>) pide a la fuente la consulta TABULAR de solo los campos necesarios
/// (fecha + medidos, con los mismos filtros) y aqui se agrupa en memoria por la clave del periodo. Asi funciona igual
/// para toda fuente (contenedor, nativa, formulario) y en ambos motores, sin SQL por proveedor. Claves ordenables en
/// orden cronologico: dia "2026-01-15", semana ISO "2026-W03", mes "2026-01", trimestre "2026-T1", anio "2026".
/// La fecha se lleva a la zona del tenant (regla 9), salvo las fechas SIN hora (medianoche UTC, ej. una columna fecha
/// de un contenedor), que se toman tal cual para que el dia 1 no se corra al mes anterior.
/// </summary>
public static class ReportPeriodGrouping
{
    public static string Label(ReportDatePeriod p) => p switch
    {
        ReportDatePeriod.Day => "Dia",
        ReportDatePeriod.Week => "Semana",
        ReportDatePeriod.Month => "Mes",
        ReportDatePeriod.Quarter => "Trimestre",
        _ => "Anio"
    };

    /// <summary>Clave del periodo de un valor fecha (DateTimeOffset, DateTime, DateOnly o texto). Null si no es fecha.</summary>
    public static string? BucketKey(object? value, ReportDatePeriod period, TimeZoneInfo? zone = null)
    {
        DateTime? local = value switch
        {
            DateTimeOffset dto => ToLocal(dto, zone),
            DateTime dt => dt.Kind == DateTimeKind.Utc ? ToLocal(new DateTimeOffset(dt), zone) : dt,
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p) => ToLocal(p, zone),
            _ => null
        };
        if (local is not DateTime d0)
        {
            return null;
        }

        return period switch
        {
            ReportDatePeriod.Day => d0.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReportDatePeriod.Week => $"{ISOWeek.GetYear(d0):D4}-W{ISOWeek.GetWeekOfYear(d0):D2}",
            ReportDatePeriod.Month => d0.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            ReportDatePeriod.Quarter => $"{d0.Year:D4}-T{(d0.Month - 1) / 3 + 1}",
            _ => d0.Year.ToString("D4", CultureInfo.InvariantCulture)
        };
    }

    private static DateTime ToLocal(DateTimeOffset v, TimeZoneInfo? zone)
    {
        // Fecha sin hora (medianoche con offset cero): es un DIA, no un instante; no se corre de zona.
        if (zone is null || (v.Offset == TimeSpan.Zero && v.TimeOfDay == TimeSpan.Zero))
        {
            return v.UtcDateTime;
        }
        return TimeZoneInfo.ConvertTime(v, zone).DateTime;
    }

    /// <summary>
    /// Spec TABULAR que se le pide a la fuente: el campo fecha + los campos de las agregaciones (no conteo), con los
    /// mismos filtros e Inputs; sin agrupar, ordenar ni limitar (eso se hace despues sobre los periodos).
    /// </summary>
    public static ReportQuerySpec TabularFor(ReportQuerySpec spec)
    {
        var fields = new List<string> { spec.GroupBy[0] };
        foreach (var a in spec.Aggregates)
        {
            if (a.Function != ReportAggregateFunction.Count && !string.IsNullOrWhiteSpace(a.FieldKey)
                && !fields.Contains(a.FieldKey, StringComparer.OrdinalIgnoreCase))
            {
                fields.Add(a.FieldKey);
            }
        }
        return spec with
        {
            Fields = fields,
            GroupBy = Array.Empty<string>(),
            Aggregates = Array.Empty<ReportAggregate>(),
            Sort = Array.Empty<ReportSort>(),
            Top = null,
            GroupPeriod = null
        };
    }

    /// <summary>Agrupa el resultado tabular por periodo y calcula las agregaciones (mismas columnas que un agrupado normal).</summary>
    public static ReportDataSet Aggregate(ReportDataSet tabular, ReportQuerySpec spec, ReportField groupField, ReportDatePeriod period, TimeZoneInfo? zone)
    {
        int Col(string key) => tabular.Columns.ToList().FindIndex(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        var dateIdx = Col(groupField.Key);
        var aggregates = spec.Aggregates.Count > 0
            ? spec.Aggregates.ToList()
            : new List<ReportAggregate> { new(groupField.Key, ReportAggregateFunction.Count) };

        var columns = new List<ReportColumn> { new(groupField.Key, $"{Label(period)} de {groupField.DisplayName}", ReportFieldType.Text) };
        foreach (var agg in aggregates)
        {
            var label = agg.Function switch
            {
                ReportAggregateFunction.Count => "Conteo",
                ReportAggregateFunction.Sum => "Suma",
                ReportAggregateFunction.Avg => "Promedio",
                ReportAggregateFunction.Min => "Minimo",
                ReportAggregateFunction.Max => "Maximo",
                _ => agg.Function.ToString()
            };
            var type = agg.Function == ReportAggregateFunction.Count ? ReportFieldType.Number : ReportFieldType.Decimal;
            columns.Add(new ReportColumn($"{agg.Function}_{agg.FieldKey}", label, type));
        }

        var groups = tabular.Rows
            .GroupBy(r => dateIdx >= 0 ? BucketKey(r[dateIdx], period, zone) : null)
            .ToList();

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var g in groups)
        {
            var cells = new List<object?> { g.Key };
            foreach (var agg in aggregates)
            {
                if (agg.Function == ReportAggregateFunction.Count)
                {
                    cells.Add((long)g.Count());
                    continue;
                }
                var idx = Col(agg.FieldKey);
                var values = idx < 0 ? new List<decimal>()
                    : g.Select(r => InMemoryReportEngine.ToDecimal(r[idx])).Where(v => v.HasValue).Select(v => v!.Value).ToList();
                cells.Add(values.Count == 0 ? null : agg.Function switch
                {
                    ReportAggregateFunction.Sum => values.Sum(),
                    ReportAggregateFunction.Avg => Math.Round(values.Average(), 4),
                    ReportAggregateFunction.Min => values.Min(),
                    ReportAggregateFunction.Max => values.Max(),
                    _ => (object?)null
                });
            }
            rows.Add(cells);
        }

        // Orden: el pedido (por el periodo o por una agregacion); por defecto CRONOLOGICO, sin fecha al final.
        IEnumerable<IReadOnlyList<object?>> ordered = rows.OrderBy(r => r[0] is null).ThenBy(r => (string?)r[0], StringComparer.Ordinal);
        foreach (var s in spec.Sort.Reverse())
        {
            var idx = columns.FindIndex(c => c.Key.Equals(s.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { continue; }
            ordered = s.Descending
                ? ordered.OrderByDescending(r => r[idx], InMemoryReportEngine.ValueComparer.Instance)
                : ordered.OrderBy(r => r[idx], InMemoryReportEngine.ValueComparer.Instance);
        }
        var list = ordered.ToList();
        if (spec.Top is int top && top > 0)
        {
            list = list.Take(top).ToList();
        }
        return new ReportDataSet(columns, list);
    }
}
