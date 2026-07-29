using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Lee una fuente reportable de tipo CONTENEDOR (DataModel/DataContainer, modelo EAV). La clave de la
/// fuente es "container:{guid}". Como <c>DataContainerRows</c> lleva el filtro global tenant, un tenant
/// que pida el contenedor de otro obtiene VACIO (el contenedor mismo es invisible y sus filas quedan
/// filtradas): aislamiento por construccion.
///
/// El filtro de texto (Equals/Contains) se empuja a la BD via EXISTS sobre celdas (traducible en PG y
/// SQL Server). El resto (rangos numericos/fecha sobre celdas string y la agregacion) se resuelve en
/// memoria sobre el conjunto ya acotado al tenant + contenedor. La agregacion numerica (Sum/Avg/Min/Max)
/// se demuestra aqui, ya que las celdas guardan el valor como texto y se convierten al leer.
/// </summary>
public sealed class ContainerReportReader
{
    public const string KeyPrefix = "container:";
    private const int MaterializeCap = 50_000;

    private readonly IApplicationDbContext _db;

    public ContainerReportReader(IApplicationDbContext db) => _db = db;

    public static bool Handles(string sourceKey) => sourceKey.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase);

    public static string KeyFor(Guid containerId) => KeyPrefix + containerId;

    public static Guid? ParseId(string sourceKey) =>
        Guid.TryParse(sourceKey.AsSpan(KeyPrefix.Length), out var id) ? id : null;

    /// <summary>Mapea el tipo de columna del contenedor al tipo logico reportable. Null si no es escalar.</summary>
    public static ReportFieldType? MapType(DataContainerColumnType type) => type switch
    {
        DataContainerColumnType.Text => ReportFieldType.Text,
        DataContainerColumnType.Number => ReportFieldType.Number,
        DataContainerColumnType.Decimal => ReportFieldType.Decimal,
        DataContainerColumnType.Date => ReportFieldType.Date,
        DataContainerColumnType.Boolean => ReportFieldType.Boolean,
        _ => null // Submodel / Reference / RelationMany: no son escalares reportables en v1.
    };

    /// <summary>Construye el descriptor de catalogo de un contenedor a partir de sus columnas escalares.</summary>
    public async Task<ReportSourceDescriptor?> DescribeAsync(Guid containerId, CancellationToken ct = default)
    {
        var container = await _db.DataContainers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == containerId, ct);
        if (container is null)
        {
            return null;
        }

        var fields = await BuildFieldsAsync(containerId, ct);
        return new ReportSourceDescriptor(KeyFor(containerId), container.Name, ReportSourceKind.Container, fields);
    }

    private async Task<IReadOnlyList<ReportField>> BuildFieldsAsync(Guid containerId, CancellationToken ct)
    {
        var cols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == containerId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);

        var fields = new List<ReportField>();
        foreach (var c in cols)
        {
            var t = MapType(c.Type);
            if (t is null)
            {
                continue;
            }

            var isNumeric = t is ReportFieldType.Number or ReportFieldType.Decimal;
            fields.Add(new ReportField(c.Id.ToString(), c.Name, t.Value, CanFilter: true, CanGroup: true, CanAggregate: isNumeric));
        }

        return fields;
    }

    public async Task<ReportDataSet> QueryAsync(ReportSourceDescriptor descriptor, ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        var containerId = ParseId(descriptor.Key)
            ?? throw new ReportValidationException($"Clave de contenedor invalida: '{descriptor.Key}'.");

        var colTypes = descriptor.Fields.ToDictionary(f => f.Key, f => f.Type, StringComparer.OrdinalIgnoreCase);

        // Empuja los filtros de texto (Equals/Contains) a la BD via EXISTS sobre celdas.
        IQueryable<DataContainerRow> q = _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == containerId);

        var inMemoryFilters = new List<ReportFilter>();
        foreach (var f in spec.Filters)
        {
            var colId = Guid.Parse(f.FieldKey);
            var isText = colTypes.TryGetValue(f.FieldKey, out var ft) && ft == ReportFieldType.Text;
            if (isText && f.Operator is ReportFilterOperator.Equals or ReportFilterOperator.Contains)
            {
                var val = (f.Values.Count > 0 ? f.Values[0] : null)?.ToLowerInvariant() ?? string.Empty;
                if (f.Operator == ReportFilterOperator.Equals)
                {
                    q = q.Where(r => r.Cells.Any(c => c.ColumnId == colId && c.Value != null && c.Value.ToLower() == val));
                }
                else
                {
                    q = q.Where(r => r.Cells.Any(c => c.ColumnId == colId && c.Value != null && c.Value.ToLower().Contains(val)));
                }
            }
            else
            {
                inMemoryFilters.Add(f);
            }
        }

        // Materializa filas + celdas (ya acotadas a tenant + contenedor) y pivotea a diccionario.
        var rawRows = await q.Take(MaterializeCap)
            .Select(r => new { r.Id, Cells = r.Cells.Select(c => new { c.ColumnId, c.Value }).ToList() })
            .ToListAsync(ct);

        var pivoted = rawRows
            .Select(r => r.Cells.GroupBy(c => c.ColumnId).ToDictionary(g => g.Key, g => g.First().Value))
            .ToList();

        // Filtros restantes (rangos numericos/fecha) en memoria sobre el conjunto ya acotado.
        foreach (var f in inMemoryFilters)
        {
            var colId = Guid.Parse(f.FieldKey);
            var type = colTypes[f.FieldKey];
            pivoted = pivoted.Where(row => MatchesInMemory(row.GetValueOrDefault(colId), f, type)).ToList();
        }

        return spec.IsAggregated
            ? Aggregate(descriptor, spec, pivoted, colTypes)
            : Tabular(descriptor, spec, pivoted, colTypes);
    }

    private static ReportDataSet Tabular(
        ReportSourceDescriptor descriptor, ReportQuerySpec spec,
        List<Dictionary<Guid, string?>> pivoted, Dictionary<string, ReportFieldType> colTypes)
    {
        var fields = spec.Fields.Count > 0
            ? spec.Fields.Select(k => descriptor.FindField(k)!).ToList()
            : descriptor.Fields.ToList();

        var columns = fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();

        IEnumerable<Dictionary<Guid, string?>> rows = pivoted;
        foreach (var s in spec.Sort)
        {
            var colId = Guid.Parse(s.FieldKey);
            rows = s.Descending
                ? rows.OrderByDescending(r => r.GetValueOrDefault(colId), StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.GetValueOrDefault(colId), StringComparer.OrdinalIgnoreCase);
        }

        if (spec.Top is int top && top >= 0)
        {
            rows = rows.Take(top);
        }

        var data = rows.Select(row => (IReadOnlyList<object?>)fields
            .Select(f => ReportValueConverter.Convert(row.GetValueOrDefault(Guid.Parse(f.Key)), f.Type))
            .ToList()).ToList();

        return new ReportDataSet(columns, data);
    }

    private static ReportDataSet Aggregate(
        ReportSourceDescriptor descriptor, ReportQuerySpec spec,
        List<Dictionary<Guid, string?>> pivoted, Dictionary<string, ReportFieldType> colTypes)
    {
        if (spec.GroupBy.Count != 1)
        {
            throw new ReportValidationException("Un contenedor soporta exactamente un campo de agrupacion en v1.");
        }

        var groupField = descriptor.FindField(spec.GroupBy[0])!;
        var groupColId = Guid.Parse(groupField.Key);

        var groups = pivoted
            .GroupBy(row => row.GetValueOrDefault(groupColId))
            .ToList();

        var columns = new List<ReportColumn> { new(groupField.Key, groupField.DisplayName, groupField.Type) };
        var aggregates = spec.Aggregates.Count > 0
            ? spec.Aggregates.ToList()
            : new List<ReportAggregate> { new(groupField.Key, ReportAggregateFunction.Count) };

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

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var g in groups)
        {
            var cells = new List<object?> { g.Key };
            foreach (var agg in aggregates)
            {
                cells.Add(ComputeAggregate(agg, g, descriptor, colTypes));
            }

            rows.Add(cells);
        }

        // Orden opcional por la columna de grupo o por una de agregado.
        foreach (var s in spec.Sort)
        {
            var idx = columns.FindIndex(c => c.Key.Equals(s.FieldKey, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                rows = (s.Descending
                    ? rows.OrderByDescending(r => r[idx], TupleComparer.Instance)
                    : rows.OrderBy(r => r[idx], TupleComparer.Instance)).ToList();
            }
        }

        return new ReportDataSet(columns, rows);
    }

    private static object? ComputeAggregate(
        ReportAggregate agg, IGrouping<string?, Dictionary<Guid, string?>> group,
        ReportSourceDescriptor descriptor, Dictionary<string, ReportFieldType> colTypes)
    {
        if (agg.Function == ReportAggregateFunction.Count)
        {
            return (long)group.Count();
        }

        var field = descriptor.FindField(agg.FieldKey)
            ?? throw new ReportValidationException($"Campo de agregacion desconocido: '{agg.FieldKey}'.");
        if (!field.CanAggregate)
        {
            throw new ReportValidationException($"El campo '{field.DisplayName}' no es numerico: no admite {agg.Function}.");
        }

        var colId = Guid.Parse(field.Key);
        var values = group
            .Select(row => ReportValueConverter.AsDecimal(row.GetValueOrDefault(colId)))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        return agg.Function switch
        {
            ReportAggregateFunction.Sum => values.Sum(),
            ReportAggregateFunction.Avg => Math.Round(values.Average(), 4),
            ReportAggregateFunction.Min => values.Min(),
            ReportAggregateFunction.Max => values.Max(),
            _ => null
        };
    }

    private static bool MatchesInMemory(string? raw, ReportFilter f, ReportFieldType type)
    {
        if (type is ReportFieldType.Number or ReportFieldType.Decimal)
        {
            var v = ReportValueConverter.AsDecimal(raw);
            var a = f.Values.Count > 0 ? ReportValueConverter.AsDecimal(f.Values[0]) : null;
            var b = f.Values.Count > 1 ? ReportValueConverter.AsDecimal(f.Values[1]) : null;
            if (v is null)
            {
                return false;
            }

            return f.Operator switch
            {
                ReportFilterOperator.Equals => a.HasValue && v == a,
                ReportFilterOperator.NotEquals => !a.HasValue || v != a,
                ReportFilterOperator.GreaterThan => a.HasValue && v > a,
                ReportFilterOperator.GreaterThanOrEqual => a.HasValue && v >= a,
                ReportFilterOperator.LessThan => a.HasValue && v < a,
                ReportFilterOperator.LessThanOrEqual => a.HasValue && v <= a,
                ReportFilterOperator.Between => a.HasValue && b.HasValue && v >= a && v <= b,
                _ => true
            };
        }

        if (type == ReportFieldType.Date)
        {
            var v = ReportValueConverter.Convert(raw, ReportFieldType.Date) as DateTimeOffset?;
            var a = f.Values.Count > 0 ? ReportValueConverter.Convert(f.Values[0], ReportFieldType.Date) as DateTimeOffset? : null;
            var b = f.Values.Count > 1 ? ReportValueConverter.Convert(f.Values[1], ReportFieldType.Date) as DateTimeOffset? : null;
            if (v is null)
            {
                return false;
            }

            return f.Operator switch
            {
                ReportFilterOperator.GreaterThan => a.HasValue && v > a,
                ReportFilterOperator.GreaterThanOrEqual => a.HasValue && v >= a,
                ReportFilterOperator.LessThan => a.HasValue && v < a,
                ReportFilterOperator.LessThanOrEqual => a.HasValue && v <= a,
                ReportFilterOperator.Between => a.HasValue && b.HasValue && v >= a && v <= b,
                _ => true
            };
        }

        // Booleano u otro: comparacion textual simple.
        var s = raw?.ToLowerInvariant();
        var target = (f.Values.Count > 0 ? f.Values[0] : null)?.ToLowerInvariant();
        return f.Operator == ReportFilterOperator.NotEquals ? s != target : s == target;
    }

    private sealed class TupleComparer : IComparer<object?>
    {
        public static readonly TupleComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (x is null && y is null) { return 0; }
            if (x is null) { return -1; }
            if (y is null) { return 1; }
            if (x is IComparable cx && x.GetType() == y.GetType()) { return cx.CompareTo(y); }
            return string.CompareOrdinal(x.ToString(), y.ToString());
        }
    }
}
