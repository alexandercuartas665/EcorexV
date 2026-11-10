using System.Globalization;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Motor COMUN en memoria para fuentes nativas nuevas (ADR-0158, Ola 1). La fuente materializa sus
/// filas ya acotadas al tenant (filtro global del DbContext) y con tope de filas, como arreglos de
/// valores TIPADOS alineados con su <see cref="ReportSourceDescriptor.Fields"/>; este motor aplica
/// filtros, agrupacion (N campos), agregaciones (Count/Sum/Avg/Min/Max), orden y Top.
///
/// Por que en memoria: los campos de estas fuentes mezclan columnas, nombres por join (catalogos
/// tenant-scoped) y derivados (mes, vencida, horas); resolverlos en memoria evita SQL por proveedor y
/// mantiene identico el resultado en PostgreSQL y SQL Server. El tope de filas acota el costo.
///
/// Convencion de columnas de salida (la misma que valida <see cref="ReportDataSource"/>): grupos por
/// su clave de campo; agregado Count -> "Count"; el resto -> "{Funcion}_{Campo}".
/// </summary>
public static class InMemoryReportEngine
{
    public static ReportDataSet Run(ReportSourceDescriptor descriptor, IReadOnlyList<object?[]> rows, ReportQuerySpec spec)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < descriptor.Fields.Count; i++)
        {
            index[descriptor.Fields[i].Key] = i;
        }

        int Idx(string key) => index.TryGetValue(key, out var i)
            ? i
            : throw new ReportValidationException($"Campo desconocido en '{descriptor.DisplayName}': '{key}'.");

        IEnumerable<object?[]> filtered = rows;
        foreach (var f in spec.Filters)
        {
            var i = Idx(f.FieldKey);
            var type = descriptor.Fields[i].Type;
            filtered = filtered.Where(r => Matches(r[i], f, type)).ToList();
        }

        return spec.IsAggregated
            ? Aggregate(descriptor, spec, filtered.ToList(), Idx)
            : Tabular(descriptor, spec, filtered, Idx);
    }

    private static ReportDataSet Tabular(ReportSourceDescriptor d, ReportQuerySpec spec, IEnumerable<object?[]> rows, Func<string, int> idx)
    {
        var fields = spec.Fields.Count > 0 ? spec.Fields.Select(k => d.Fields[idx(k)]).ToList() : d.Fields.ToList();
        var positions = fields.Select(f => idx(f.Key)).ToList();

        IOrderedEnumerable<object?[]>? ordered = null;
        foreach (var s in spec.Sort)
        {
            var i = idx(s.FieldKey);
            ordered = ordered is null
                ? (s.Descending ? rows.OrderByDescending(r => r[i], ValueComparer.Instance) : rows.OrderBy(r => r[i], ValueComparer.Instance))
                : (s.Descending ? ordered.ThenByDescending(r => r[i], ValueComparer.Instance) : ordered.ThenBy(r => r[i], ValueComparer.Instance));
        }

        IEnumerable<object?[]> result = ordered ?? rows;
        if (spec.Top is int top && top >= 0)
        {
            result = result.Take(top);
        }

        var columns = fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();
        var data = result.Select(r => (IReadOnlyList<object?>)positions.Select(p => r[p]).ToList()).ToList();
        return new ReportDataSet(columns, data);
    }

    private static ReportDataSet Aggregate(ReportSourceDescriptor d, ReportQuerySpec spec, List<object?[]> rows, Func<string, int> idx)
    {
        var groupFields = spec.GroupBy.Select(k => d.Fields[idx(k)]).ToList();
        var groupPos = groupFields.Select(f => idx(f.Key)).ToArray();
        var aggregates = spec.Aggregates.Count > 0
            ? spec.Aggregates.ToList()
            : new List<ReportAggregate> { new(string.Empty, ReportAggregateFunction.Count) };

        var columns = groupFields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();
        foreach (var a in aggregates)
        {
            if (a.Function == ReportAggregateFunction.Count)
            {
                columns.Add(new ReportColumn("Count", "Conteo", ReportFieldType.Number));
                continue;
            }

            var field = d.Fields[idx(a.FieldKey)];
            if (!field.CanAggregate)
            {
                throw new ReportValidationException($"El campo '{field.DisplayName}' no es numerico: no admite {a.Function}.");
            }

            columns.Add(new ReportColumn($"{a.Function}_{field.Key}", $"{Label(a.Function)} {field.DisplayName}", ReportFieldType.Decimal));
        }

        var groups = rows.GroupBy(r => new GroupKey(groupPos.Select(p => r[p]).ToArray()));
        var output = new List<IReadOnlyList<object?>>();
        foreach (var g in groups)
        {
            var cells = new List<object?>(g.Key.Values);
            foreach (var a in aggregates)
            {
                cells.Add(Compute(a, g, idx));
            }

            output.Add(cells);
        }

        foreach (var s in spec.Sort.Reverse())
        {
            var ci = columns.FindIndex(c => c.Key.Equals(s.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (ci < 0)
            {
                continue;
            }

            // OrderBy estable aplicado de la ultima clave a la primera = orden multi-clave correcto.
            output = (s.Descending
                ? output.OrderByDescending(r => r[ci], ValueComparer.Instance)
                : output.OrderBy(r => r[ci], ValueComparer.Instance)).ToList();
        }

        if (spec.Top is int top && top >= 0)
        {
            output = output.Take(top).ToList();
        }

        return new ReportDataSet(columns, output);
    }

    private static object? Compute(ReportAggregate a, IEnumerable<object?[]> group, Func<string, int> idx)
    {
        if (a.Function == ReportAggregateFunction.Count)
        {
            return (long)group.Count();
        }

        var i = idx(a.FieldKey);
        var values = group.Select(r => ToDecimal(r[i])).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        return a.Function switch
        {
            ReportAggregateFunction.Sum => values.Sum(),
            ReportAggregateFunction.Avg => Math.Round(values.Average(), 4),
            ReportAggregateFunction.Min => values.Min(),
            ReportAggregateFunction.Max => values.Max(),
            _ => null
        };
    }

    private static string Label(ReportAggregateFunction f) => f switch
    {
        ReportAggregateFunction.Sum => "Suma",
        ReportAggregateFunction.Avg => "Promedio",
        ReportAggregateFunction.Min => "Minimo",
        ReportAggregateFunction.Max => "Maximo",
        _ => "Conteo"
    };

    // ---- Filtros tipados ----

    public static bool Matches(object? value, ReportFilter f, ReportFieldType type)
    {
        var vals = f.Values;
        switch (type)
        {
            case ReportFieldType.Number:
            case ReportFieldType.Decimal:
                {
                    var v = ToDecimal(value);
                    var a = vals.Count > 0 ? ReportValueConverter.AsDecimal(vals[0]) : null;
                    var b = vals.Count > 1 ? ReportValueConverter.AsDecimal(vals[1]) : null;
                    if (f.Operator == ReportFilterOperator.In)
                    {
                        return v.HasValue && vals.Select(ReportValueConverter.AsDecimal).Any(x => x == v);
                    }

                    return CompareOp(v, a, b, f.Operator);
                }
            case ReportFieldType.Date:
                {
                    var v = value as DateTimeOffset?;
                    DateTimeOffset? P(int k) => vals.Count > k ? ReportValueConverter.Convert(vals[k], ReportFieldType.Date) as DateTimeOffset? : null;
                    var a = P(0);
                    var b = P(1);
                    return CompareOp(v, a, b, f.Operator);
                }
            case ReportFieldType.Boolean:
                {
                    var v = value as bool?;
                    var target = vals.Count > 0 ? ReportValueConverter.Convert(vals[0], ReportFieldType.Boolean) as bool? : null;
                    return f.Operator == ReportFilterOperator.NotEquals ? v != target : v == target;
                }
            default:
                {
                    var s = (value?.ToString() ?? string.Empty).ToLowerInvariant();
                    var t = (vals.Count > 0 ? vals[0] : null)?.ToLowerInvariant() ?? string.Empty;
                    return f.Operator switch
                    {
                        ReportFilterOperator.Equals => s == t,
                        ReportFilterOperator.NotEquals => s != t,
                        ReportFilterOperator.Contains => s.Contains(t, StringComparison.Ordinal),
                        ReportFilterOperator.In => vals.Any(x => (x ?? string.Empty).ToLowerInvariant() == s),
                        ReportFilterOperator.GreaterThan => string.CompareOrdinal(s, t) > 0,
                        ReportFilterOperator.GreaterThanOrEqual => string.CompareOrdinal(s, t) >= 0,
                        ReportFilterOperator.LessThan => string.CompareOrdinal(s, t) < 0,
                        ReportFilterOperator.LessThanOrEqual => string.CompareOrdinal(s, t) <= 0,
                        ReportFilterOperator.Between => vals.Count > 1
                            && string.CompareOrdinal(s, t) >= 0
                            && string.CompareOrdinal(s, (vals[1] ?? string.Empty).ToLowerInvariant()) <= 0,
                        _ => true
                    };
                }
        }
    }

    private static bool CompareOp<T>(T? v, T? a, T? b, ReportFilterOperator op) where T : struct, IComparable<T>
    {
        if (op == ReportFilterOperator.NotEquals)
        {
            return !v.HasValue || !a.HasValue || v.Value.CompareTo(a.Value) != 0;
        }

        if (!v.HasValue)
        {
            return false;
        }

        var x = v.Value;
        return op switch
        {
            ReportFilterOperator.Equals or ReportFilterOperator.In => a.HasValue && x.CompareTo(a.Value) == 0,
            ReportFilterOperator.GreaterThan => a.HasValue && x.CompareTo(a.Value) > 0,
            ReportFilterOperator.GreaterThanOrEqual => a.HasValue && x.CompareTo(a.Value) >= 0,
            ReportFilterOperator.LessThan => a.HasValue && x.CompareTo(a.Value) < 0,
            ReportFilterOperator.LessThanOrEqual => a.HasValue && x.CompareTo(a.Value) <= 0,
            ReportFilterOperator.Between => a.HasValue && b.HasValue && x.CompareTo(a.Value) >= 0 && x.CompareTo(b.Value) <= 0,
            _ => true
        };
    }

    public static decimal? ToDecimal(object? v) => v switch
    {
        null => null,
        decimal d => d,
        long l => l,
        int i => i,
        double db => (decimal)db,
        string s => ReportValueConverter.AsDecimal(s),
        _ => null
    };

    /// <summary>
    /// Clave "AAAA-MM" de un instante en la zona del tenant (regla 9). Si <paramref name="zona"/> es null
    /// se usa UTC (comportamiento historico): asi, un reporte generico sin zona sigue agrupando igual.
    /// Con zona, un instante cercano a la medianoche cae en el mes correcto para el usuario.
    /// </summary>
    public static string? MonthKey(DateTimeOffset? d, TimeZoneInfo? zona = null)
    {
        if (d is not DateTimeOffset v)
        {
            return null;
        }

        var local = zona is null ? v.UtcDateTime : TimeZoneInfo.ConvertTime(v, zona).DateTime;
        return local.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    private sealed class GroupKey : IEquatable<GroupKey>
    {
        public GroupKey(object?[] values) => Values = values;

        public object?[] Values { get; }

        public bool Equals(GroupKey? other) =>
            other is not null && Values.Length == other.Values.Length
            && Values.Zip(other.Values).All(p => Equals(p.First, p.Second));

        public override bool Equals(object? obj) => Equals(obj as GroupKey);

        public override int GetHashCode()
        {
            var h = new HashCode();
            foreach (var v in Values)
            {
                h.Add(v);
            }

            return h.ToHashCode();
        }
    }

    /// <summary>Comparador null-first tolerante a tipos mixtos.</summary>
    public sealed class ValueComparer : IComparer<object?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) { return 0; }
            if (x is null) { return -1; }
            if (y is null) { return 1; }
            if (ToDecimal(x) is decimal dx && ToDecimal(y) is decimal dy && x is not string && y is not string) { return dx.CompareTo(dy); }
            if (x is IComparable cx && x.GetType() == y.GetType()) { return cx.CompareTo(y); }
            return string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
