using Ecorex.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Fuente reportable NATIVA para los SERVIDORES SQL externos del tenant (<c>external_data_sources</c>, la
/// pagina "Conexiones de datos", ADR-0084). A diferencia de las entidades tenant-scoped, esta es
/// <c>BaseEntity</c> con <c>OwnerTenantId</c> nullable: NO lleva filtro global. El aislamiento es EXPLICITO:
/// solo se listan las fuentes cuyo <c>OwnerTenantId</c> es el tenant activo (<c>ctx.TenantId</c>); las de
/// owner nulo (plataforma) NUNCA se muestran al tenant.
///
/// SEGURIDAD: NUNCA expone <c>ConnectionStringEncrypted</c>. Solo metadatos (nombre, motor, acceso, estado,
/// datasets, validacion, descripcion, fechas). Como el conjunto por tenant es pequeno, se materializa y se
/// filtra/agrupa EN MEMORIA (tabular + agregado con 1 group + Count).
/// </summary>
public sealed class ExternalDataSourceReportSource : IReportableSource
{
    public const string SourceKey = "native:externalsource";

    private static readonly IReadOnlyList<ReportField> FieldSet = new[]
    {
        new ReportField("Name", "Nombre", ReportFieldType.Text, CanFilter: true, CanGroup: false),
        new ReportField("Provider", "Motor", ReportFieldType.Text),
        new ReportField("Acceso", "Acceso", ReportFieldType.Text),
        new ReportField("Estado", "Estado", ReportFieldType.Text),
        new ReportField("Datasets", "Datasets", ReportFieldType.Number, CanFilter: true, CanGroup: false),
        new ReportField("LastValidatedAt", "UltimaValidacion", ReportFieldType.Date, CanFilter: true, CanGroup: false),
        new ReportField("Description", "Descripcion", ReportFieldType.Text, CanFilter: true, CanGroup: false),
        new ReportField("CreatedAt", "Creada", ReportFieldType.Date, CanFilter: true, CanGroup: false),
        new ReportField("UpdatedAt", "Actualizada", ReportFieldType.Date, CanFilter: true, CanGroup: false)
    };

    private readonly IApplicationDbContext _db;

    public ExternalDataSourceReportSource(IApplicationDbContext db) => _db = db;

    public ReportSourceDescriptor Describe() =>
        new(SourceKey, "Servidores de datos", ReportSourceKind.Native, FieldSet);

    private sealed record Row(
        string Name, string Provider, string Acceso, string Estado, long Datasets,
        DateTimeOffset? LastValidatedAt, string? Description, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        // Aislamiento EXPLICITO por owner: solo las fuentes del tenant activo (owner nulo = plataforma NO se ve).
        var sources = await _db.ExternalDataSources.AsNoTracking()
            .Where(s => s.OwnerTenantId == ctx.TenantId)
            .Select(s => new
            {
                s.Id, s.Name, s.Provider, s.AllowWrite, s.IsEnabled, s.Description,
                s.LastValidatedAt, s.CreatedAt, s.UpdatedAt
            })
            .ToListAsync(ct);

        // Conteo de datasets por fuente (acotado a las fuentes del tenant).
        var counts = new Dictionary<Guid, int>();
        if (sources.Count > 0)
        {
            var ids = sources.Select(s => s.Id).ToList();
            var grouped = await _db.ExternalDataSets.AsNoTracking()
                .Where(d => ids.Contains(d.ExternalDataSourceId))
                .GroupBy(d => d.ExternalDataSourceId)
                .Select(g => new { g.Key, C = g.Count() })
                .ToListAsync(ct);
            counts = grouped.ToDictionary(x => x.Key, x => x.C);
        }

        var rows = sources.Select(s => new Row(
            Name: s.Name,
            Provider: s.Provider.ToString(),
            Acceso: s.AllowWrite ? "Lectura/Escritura" : "Lectura",
            Estado: s.IsEnabled ? "Habilitada" : "Deshabilitada",
            Datasets: counts.TryGetValue(s.Id, out var c) ? c : 0,
            LastValidatedAt: s.LastValidatedAt,
            Description: s.Description,
            CreatedAt: s.CreatedAt,
            UpdatedAt: s.UpdatedAt)).ToList();

        rows = ApplyFilters(rows, spec.Filters);

        return spec.IsAggregated ? Aggregate(rows, spec) : Tabular(rows, spec);
    }

    // ---- Tabular ----

    private static ReportDataSet Tabular(List<Row> rows, ReportQuerySpec spec)
    {
        var fields = spec.Fields.Count > 0 ? spec.Fields.Select(ResolveField).ToList() : FieldSet.ToList();

        IEnumerable<Row> ordered = rows;
        foreach (var s in spec.Sort)
        {
            var key = FieldGetter(s.FieldKey);
            ordered = ordered is IOrderedEnumerable<Row> oe
                ? (s.Descending ? oe.ThenByDescending(key, NullSafe.Instance) : oe.ThenBy(key, NullSafe.Instance))
                : (s.Descending ? rows.OrderByDescending(key, NullSafe.Instance) : rows.OrderBy(key, NullSafe.Instance));
        }
        if (spec.Top is int top && top >= 0) { ordered = ordered.Take(top); }

        var columns = fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();
        var getters = fields.Select(f => FieldGetter(f.Key)).ToList();
        var data = ordered
            .Select(r => (IReadOnlyList<object?>)getters.Select(g => g(r)).ToList())
            .ToList();
        return new ReportDataSet(columns, data);
    }

    // ---- Agregado (1 group + Count) ----

    private static ReportDataSet Aggregate(List<Row> rows, ReportQuerySpec spec)
    {
        if (spec.GroupBy.Count != 1)
        {
            throw new ReportValidationException("La fuente 'Servidores de datos' soporta exactamente un campo de agrupacion en v1.");
        }
        foreach (var agg in spec.Aggregates)
        {
            if (agg.Function != ReportAggregateFunction.Count)
            {
                throw new ReportValidationException("La fuente 'Servidores de datos' solo admite el conteo (Count) en v1.");
            }
        }

        var groupField = ResolveField(spec.GroupBy[0]);
        if (!groupField.CanGroup)
        {
            throw new ReportValidationException($"El campo '{groupField.Key}' no admite agrupacion en la fuente 'Servidores de datos'.");
        }
        var keyOf = FieldGetter(groupField.Key);

        var pairs = rows
            .GroupBy(r => keyOf(r)?.ToString())
            .Select(g => (Key: g.Key, Count: g.Count()))
            .ToList();

        IEnumerable<(string? Key, int Count)> ordered = pairs;
        foreach (var s in spec.Sort)
        {
            if (s.FieldKey.Equals("Count", StringComparison.OrdinalIgnoreCase))
            {
                ordered = s.Descending ? ordered.OrderByDescending(p => p.Count) : ordered.OrderBy(p => p.Count);
            }
            else if (s.FieldKey.Equals(groupField.Key, StringComparison.OrdinalIgnoreCase))
            {
                ordered = s.Descending ? ordered.OrderByDescending(p => p.Key) : ordered.OrderBy(p => p.Key);
            }
        }

        var columns = new List<ReportColumn>
        {
            new(groupField.Key, groupField.DisplayName, groupField.Type),
            new("Count", "Conteo", ReportFieldType.Number)
        };
        var data = ordered
            .Select(p => (IReadOnlyList<object?>)new object?[] { p.Key, (long)p.Count })
            .ToList();
        return new ReportDataSet(columns, data);
    }

    // ---- Filtros (en memoria) ----

    private static List<Row> ApplyFilters(List<Row> rows, IReadOnlyList<ReportFilter> filters)
    {
        IEnumerable<Row> q = rows;
        foreach (var f in filters)
        {
            var field = ResolveField(f.FieldKey);
            var get = FieldGetter(field.Key);
            var first = f.Values.Count > 0 ? f.Values[0] : null;

            switch (field.Type)
            {
                case ReportFieldType.Date:
                    q = q.Where(r => DateMatches(get(r) as DateTimeOffset?, f));
                    break;
                case ReportFieldType.Number:
                    q = q.Where(r => NumberMatches(get(r), f));
                    break;
                default:
                    q = q.Where(r => TextMatches(get(r)?.ToString(), first, f.Operator));
                    break;
            }
        }
        return q.ToList();
    }

    private static bool TextMatches(string? value, string? target, ReportFilterOperator op)
    {
        var v = (value ?? string.Empty).ToLowerInvariant();
        var t = (target ?? string.Empty).ToLowerInvariant();
        return op switch
        {
            ReportFilterOperator.Equals => v == t,
            ReportFilterOperator.NotEquals => v != t,
            _ => v.Contains(t)
        };
    }

    private static bool NumberMatches(object? value, ReportFilter f)
    {
        var v = value is long l ? l : (value is int i ? i : (long?)null);
        long? Parse(string? s) => long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
        var a = f.Values.Count > 0 ? Parse(f.Values[0]) : null;
        var b = f.Values.Count > 1 ? Parse(f.Values[1]) : null;
        if (v is null || a is null) { return f.Operator == ReportFilterOperator.NotEquals; }
        return f.Operator switch
        {
            ReportFilterOperator.NotEquals => v != a,
            ReportFilterOperator.GreaterThan => v > a,
            ReportFilterOperator.GreaterThanOrEqual => v >= a,
            ReportFilterOperator.LessThan => v < a,
            ReportFilterOperator.LessThanOrEqual => v <= a,
            ReportFilterOperator.Between => b is not null && v >= a && v <= b,
            _ => v == a
        };
    }

    private static bool DateMatches(DateTimeOffset? value, ReportFilter f)
    {
        DateTimeOffset? Parse(string? s) => DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        var a = f.Values.Count > 0 ? Parse(f.Values[0]) : null;
        var b = f.Values.Count > 1 ? Parse(f.Values[1]) : null;
        if (value is null || a is null) { return false; }
        return f.Operator switch
        {
            ReportFilterOperator.GreaterThan => value > a,
            ReportFilterOperator.GreaterThanOrEqual => value >= a,
            ReportFilterOperator.LessThan => value < a,
            ReportFilterOperator.LessThanOrEqual => value <= a,
            ReportFilterOperator.Between => b is not null && value >= a && value <= b,
            _ => false
        };
    }

    // ---- Mapeos ----

    private static ReportField ResolveField(string key)
    {
        var f = FieldSet.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return f ?? throw new ReportValidationException($"Campo desconocido en 'Servidores de datos': '{key}'.");
    }

    private static Func<Row, object?> FieldGetter(string key) => key.ToLowerInvariant() switch
    {
        "name" => r => r.Name,
        "provider" => r => r.Provider,
        "acceso" => r => r.Acceso,
        "estado" => r => r.Estado,
        "datasets" => r => r.Datasets,
        "lastvalidatedat" => r => r.LastValidatedAt,
        "description" => r => r.Description,
        "createdat" => r => r.CreatedAt,
        "updatedat" => r => r.UpdatedAt,
        _ => _ => null
    };

    private sealed class NullSafe : IComparer<object?>
    {
        public static readonly NullSafe Instance = new();
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
