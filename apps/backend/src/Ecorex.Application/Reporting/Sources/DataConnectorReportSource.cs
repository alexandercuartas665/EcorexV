using System.Linq.Expressions;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Fuente reportable NATIVA para <see cref="DataConnector"/> (conexiones/servidores del tenant, ADR-0084).
/// Declara sus campos logicos y sabe consultarse con LINQ tipado sobre <c>_db.DataConnectors</c>, que lleva
/// el filtro global tenant: la consulta es tenant-safe por construccion (imposible pedir cross-tenant).
///
/// SEGURIDAD: NUNCA expone <c>CredentialsEncrypted</c> ni <c>Username</c>. Solo metadatos de la conexion
/// (nombre, tipo, motor, host/puerto/base, endpoint/metodo, auth, contenedor, activa, fechas). El campo
/// Contenedor se resuelve por NOMBRE contra <c>DataContainers</c> (tenant-scoped), igual que los campos por
/// nombre de <see cref="TaskItemReportSource"/>.
///
/// Alcance v1: filtros parametrizados, modo tabular y modo agregado con UN campo de agrupacion + conteo
/// (Count). No hay campos numericos agregables de negocio (Puerto no se agrega), asi que Sum/Avg/Min/Max
/// se rechazan con error claro.
/// </summary>
public sealed class DataConnectorReportSource : IReportableSource
{
    public const string SourceKey = "native:dataconnector";

    private const int MaterializeCap = 50_000;

    private static readonly IReadOnlyList<ReportField> FieldSet = new[]
    {
        new ReportField("Name", "Nombre", ReportFieldType.Text, CanFilter: true, CanGroup: false),
        new ReportField("Kind", "Tipo", ReportFieldType.Text),
        new ReportField("DbEngine", "Motor", ReportFieldType.Text),
        new ReportField("Host", "Host", ReportFieldType.Text, CanFilter: true, CanGroup: false),
        new ReportField("Port", "Puerto", ReportFieldType.Number, CanFilter: true, CanGroup: false),
        new ReportField("DatabaseName", "BaseDatos", ReportFieldType.Text, CanFilter: true, CanGroup: false),
        new ReportField("EndpointUrl", "Endpoint", ReportFieldType.Text, CanFilter: true, CanGroup: false),
        new ReportField("HttpMethod", "Metodo", ReportFieldType.Text),
        new ReportField("AuthKind", "Auth", ReportFieldType.Text),
        // Contenedor por NOMBRE (join a DataContainers tenant-scoped): reportable, agrupable y filtrable.
        new ReportField("Container", "Contenedor", ReportFieldType.Text),
        new ReportField("IsActive", "Activa", ReportFieldType.Boolean),
        new ReportField("CreatedAt", "Creada", ReportFieldType.Date, CanFilter: true, CanGroup: false),
        new ReportField("UpdatedAt", "Actualizada", ReportFieldType.Date, CanFilter: true, CanGroup: false)
    };

    private readonly IApplicationDbContext _db;

    public DataConnectorReportSource(IApplicationDbContext db) => _db = db;

    public ReportSourceDescriptor Describe() =>
        new(SourceKey, "Conexiones", ReportSourceKind.Native, FieldSet);

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        var lk = await LoadLookupsAsync(ct);

        IQueryable<DataConnector> q = _db.DataConnectors.AsNoTracking();
        q = ApplyFilters(q, spec.Filters, lk);

        return spec.IsAggregated
            ? await QueryAggregatedAsync(q, spec, lk, ct)
            : await QueryTabularAsync(q, spec, lk, ct);
    }

    // ---- Catalogos (tenant-safe) ----

    private sealed record Lookups(IReadOnlyDictionary<Guid, string> Containers);

    private async Task<Lookups> LoadLookupsAsync(CancellationToken ct)
    {
        var containers = await _db.DataContainers.AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        return new Lookups(containers.ToDictionary(x => x.Id, x => x.Name));
    }

    // ---- Modo tabular ----

    private async Task<ReportDataSet> QueryTabularAsync(IQueryable<DataConnector> q, ReportQuerySpec spec, Lookups lk, CancellationToken ct)
    {
        var fields = spec.Fields.Count > 0
            ? spec.Fields.Select(ResolveField).ToList()
            : FieldSet.ToList();

        var entities = await q.Take(MaterializeCap).ToListAsync(ct);

        IEnumerable<DataConnector> rows = entities;
        rows = ApplySort(rows, spec.Sort, lk);
        if (spec.Top is int top && top >= 0)
        {
            rows = rows.Take(top);
        }

        var columns = fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();
        var getters = fields.Select(f => ValueGetter(f.Key, lk)).ToList();
        var data = rows
            .Select(c => (IReadOnlyList<object?>)getters.Select(g => g(c)).ToList())
            .ToList();

        return new ReportDataSet(columns, data);
    }

    // ---- Modo agregado (un campo de agrupacion + Count) ----

    private async Task<ReportDataSet> QueryAggregatedAsync(IQueryable<DataConnector> q, ReportQuerySpec spec, Lookups lk, CancellationToken ct)
    {
        if (spec.GroupBy.Count != 1)
        {
            throw new ReportValidationException("La fuente 'Conexiones' soporta exactamente un campo de agrupacion en v1.");
        }

        foreach (var agg in spec.Aggregates)
        {
            if (agg.Function != ReportAggregateFunction.Count)
            {
                throw new ReportValidationException(
                    "La fuente 'Conexiones' no tiene campos numericos agregables en v1: solo se admite el conteo (Count).");
            }
        }

        var groupField = ResolveField(spec.GroupBy[0]);
        var pairs = await GroupCountAsync(q, groupField.Key, lk, ct);

        var columns = new List<ReportColumn>
        {
            new(groupField.Key, groupField.DisplayName, groupField.Type),
            new("Count", "Conteo", ReportFieldType.Number)
        };

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

        var rows = ordered
            .Select(p => (IReadOnlyList<object?>)new object?[] { p.Key, (long)p.Count })
            .ToList();

        return new ReportDataSet(columns, rows);
    }

    private async Task<List<(string? Key, int Count)>> GroupCountAsync(IQueryable<DataConnector> q, string fieldKey, Lookups lk, CancellationToken ct)
    {
        switch (fieldKey.ToLowerInvariant())
        {
            case "kind":
                return (await q.GroupBy(c => c.Kind).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key.ToString(), x.C)).ToList();
            case "dbengine":
                return (await q.GroupBy(c => c.DbEngine).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)(x.Key?.ToString()), x.C)).ToList();
            case "authkind":
                return (await q.GroupBy(c => c.AuthKind).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key.ToString(), x.C)).ToList();
            case "httpmethod":
                return (await q.GroupBy(c => c.HttpMethod).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key, x.C)).ToList();
            case "isactive":
                return (await q.GroupBy(c => c.IsActive).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key.ToString(), x.C)).ToList();
            // Contenedor por NOMBRE: se agrupa por el id en BD y luego se mapea id->nombre y se RE-SUMA por nombre.
            case "container":
                return MapCountByName(await GroupByGuidAsync(q, c => c.ContainerId, ct),
                    id => lk.Containers.TryGetValue(id, out var n) ? n : null);
            default:
                throw new ReportValidationException($"El campo '{fieldKey}' no admite agrupacion en la fuente 'Conexiones'.");
        }
    }

    private static async Task<List<(Guid? Id, int Count)>> GroupByGuidAsync(
        IQueryable<DataConnector> q, Expression<Func<DataConnector, Guid?>> selector, CancellationToken ct)
        => (await q.GroupBy(selector).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
            .Select(x => (x.Key, x.C)).ToList();

    private static List<(string? Key, int Count)> MapCountByName(List<(Guid? Id, int Count)> src, Func<Guid, string?> nameOf)
    {
        var agg = new Dictionary<string?, int>();
        foreach (var (id, count) in src)
        {
            var name = id is Guid g ? nameOf(g) : null;
            agg[name] = agg.TryGetValue(name, out var c) ? c + count : count;
        }
        return agg.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    // ---- Filtros (parametrizados, traducidos a EF) ----

    private static IQueryable<DataConnector> ApplyFilters(IQueryable<DataConnector> q, IReadOnlyList<ReportFilter> filters, Lookups lk)
    {
        foreach (var f in filters)
        {
            var field = ResolveField(f.FieldKey);
            var first = f.Values.Count > 0 ? f.Values[0] : null;

            switch (field.Key.ToLowerInvariant())
            {
                case "kind":
                {
                    var set = ParseEnums<ConnectorKind>(f.Values);
                    q = set.Count == 0 ? q.Where(c => false)
                        : f.Operator == ReportFilterOperator.NotEquals ? q.Where(c => !set.Contains(c.Kind)) : q.Where(c => set.Contains(c.Kind));
                    break;
                }
                case "dbengine":
                {
                    var set = ParseEnums<DbEngine>(f.Values);
                    q = set.Count == 0 ? q.Where(c => false)
                        : f.Operator == ReportFilterOperator.NotEquals
                            ? q.Where(c => c.DbEngine == null || !set.Contains(c.DbEngine.Value))
                            : q.Where(c => c.DbEngine != null && set.Contains(c.DbEngine.Value));
                    break;
                }
                case "authkind":
                {
                    var set = ParseEnums<ConnectorAuthKind>(f.Values);
                    q = set.Count == 0 ? q.Where(c => false)
                        : f.Operator == ReportFilterOperator.NotEquals ? q.Where(c => !set.Contains(c.AuthKind)) : q.Where(c => set.Contains(c.AuthKind));
                    break;
                }
                case "isactive":
                    var b = bool.TryParse(first, out var bv) && bv;
                    q = f.Operator == ReportFilterOperator.NotEquals ? q.Where(c => c.IsActive != b) : q.Where(c => c.IsActive == b);
                    break;
                case "name":
                    q = ApplyTextFilter(q, f, c => c.Name);
                    break;
                case "host":
                    q = ApplyTextFilter(q, f, c => c.Host);
                    break;
                case "databasename":
                    q = ApplyTextFilter(q, f, c => c.DatabaseName);
                    break;
                case "endpointurl":
                    q = ApplyTextFilter(q, f, c => c.EndpointUrl);
                    break;
                case "httpmethod":
                    q = ApplyTextFilter(q, f, c => c.HttpMethod);
                    break;
                case "port":
                    q = ApplyNumberFilter(q, f, c => c.Port);
                    break;
                case "createdat":
                    q = ApplyDateFilter(q, f, c => (DateTimeOffset?)c.CreatedAt);
                    break;
                case "updatedat":
                    q = ApplyDateFilter(q, f, c => c.UpdatedAt);
                    break;
                // Filtro por NOMBRE del contenedor: se resuelve el conjunto de ids que casan y se filtra por id.
                case "container":
                {
                    var ids = MatchIds(lk.Containers.Select(kv => (kv.Key, (string?)kv.Value)), f);
                    q = f.Operator == ReportFilterOperator.NotEquals
                        ? q.Where(c => c.ContainerId == null || !ids.Contains(c.ContainerId.Value))
                        : q.Where(c => c.ContainerId != null && ids.Contains(c.ContainerId.Value));
                    break;
                }
                default:
                    throw new ReportValidationException($"El campo '{f.FieldKey}' no admite filtro en la fuente 'Conexiones'.");
            }
        }

        return q;
    }

    private static List<Guid> MatchIds(IEnumerable<(Guid Id, string? Name)> pairs, ReportFilter f)
    {
        var val = (f.Values.Count > 0 ? f.Values[0] : null)?.ToLowerInvariant() ?? string.Empty;
        var exact = f.Operator == ReportFilterOperator.Equals;
        var result = new List<Guid>();
        foreach (var (id, name) in pairs)
        {
            var n = (name ?? string.Empty).ToLowerInvariant();
            if (exact ? n == val : n.Contains(val)) { result.Add(id); }
        }
        return result;
    }

    private static List<TEnum> ParseEnums<TEnum>(IReadOnlyList<string?> values) where TEnum : struct, Enum
    {
        var result = new List<TEnum>();
        foreach (var v in values)
        {
            if (Enum.TryParse<TEnum>(v, ignoreCase: true, out var parsed))
            {
                result.Add(parsed);
            }
        }
        return result;
    }

    private static IQueryable<DataConnector> ApplyTextFilter(IQueryable<DataConnector> q, ReportFilter f, Expression<Func<DataConnector, string?>> selector)
    {
        var val = (f.Values.Count > 0 ? f.Values[0] : null)?.ToLowerInvariant() ?? string.Empty;
        var member = selector.Body;
        var param = selector.Parameters[0];

        Expression body = f.Operator switch
        {
            ReportFilterOperator.Equals => Expression.Equal(
                ToLower(member),
                Expression.Constant(val, typeof(string))),
            _ => Expression.Call(
                ToLower(member),
                typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
                Expression.Constant(val, typeof(string)))
        };

        var lambda = Expression.Lambda<Func<DataConnector, bool>>(body, param);
        return q.Where(lambda);
    }

    private static Expression ToLower(Expression member) =>
        Expression.Call(
            Expression.Coalesce(member, Expression.Constant(string.Empty)),
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);

    private static IQueryable<DataConnector> ApplyNumberFilter(IQueryable<DataConnector> q, ReportFilter f, Expression<Func<DataConnector, int?>> selector)
    {
        int? Parse(string? s) =>
            int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

        var param = selector.Parameters[0];
        var member = selector.Body; // int?
        var v0 = f.Values.Count > 0 ? Parse(f.Values[0]) : null;
        var v1 = f.Values.Count > 1 ? Parse(f.Values[1]) : null;
        if (v0 is null) { return q; }

        var c0 = Expression.Constant((int?)v0.Value, typeof(int?));
        Expression body = f.Operator switch
        {
            ReportFilterOperator.NotEquals => Expression.NotEqual(member, c0),
            ReportFilterOperator.GreaterThan => Expression.MakeBinary(ExpressionType.GreaterThan, member, c0),
            ReportFilterOperator.GreaterThanOrEqual => Expression.MakeBinary(ExpressionType.GreaterThanOrEqual, member, c0),
            ReportFilterOperator.LessThan => Expression.MakeBinary(ExpressionType.LessThan, member, c0),
            ReportFilterOperator.LessThanOrEqual => Expression.MakeBinary(ExpressionType.LessThanOrEqual, member, c0),
            ReportFilterOperator.Between when v1 is not null =>
                Expression.AndAlso(
                    Expression.MakeBinary(ExpressionType.GreaterThanOrEqual, member, c0),
                    Expression.MakeBinary(ExpressionType.LessThanOrEqual, member, Expression.Constant((int?)v1.Value, typeof(int?)))),
            _ => Expression.Equal(member, c0)
        };

        return q.Where(Expression.Lambda<Func<DataConnector, bool>>(body, param));
    }

    private static IQueryable<DataConnector> ApplyDateFilter(IQueryable<DataConnector> q, ReportFilter f, Expression<Func<DataConnector, DateTimeOffset?>> selector)
    {
        DateTimeOffset? Parse(string? s) =>
            DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

        var param = selector.Parameters[0];
        var member = selector.Body;
        var v0 = f.Values.Count > 0 ? Parse(f.Values[0]) : null;
        var v1 = f.Values.Count > 1 ? Parse(f.Values[1]) : null;

        Expression? body = f.Operator switch
        {
            ReportFilterOperator.GreaterThan when v0 is not null => Cmp(member, v0.Value, ExpressionType.GreaterThan),
            ReportFilterOperator.GreaterThanOrEqual when v0 is not null => Cmp(member, v0.Value, ExpressionType.GreaterThanOrEqual),
            ReportFilterOperator.LessThan when v0 is not null => Cmp(member, v0.Value, ExpressionType.LessThan),
            ReportFilterOperator.LessThanOrEqual when v0 is not null => Cmp(member, v0.Value, ExpressionType.LessThanOrEqual),
            ReportFilterOperator.Between when v0 is not null && v1 is not null =>
                Expression.AndAlso(
                    Cmp(member, v0.Value, ExpressionType.GreaterThanOrEqual),
                    Cmp(member, v1.Value, ExpressionType.LessThanOrEqual)),
            _ => null
        };

        if (body is null)
        {
            return q;
        }

        return q.Where(Expression.Lambda<Func<DataConnector, bool>>(body, param));
    }

    private static Expression Cmp(Expression member, DateTimeOffset value, ExpressionType op)
    {
        var constant = Expression.Constant((DateTimeOffset?)value, typeof(DateTimeOffset?));
        return Expression.MakeBinary(op, member, constant);
    }

    // ---- Mapeos de campo ----

    private static ReportField ResolveField(string key)
    {
        var f = FieldSet.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return f ?? throw new ReportValidationException($"Campo desconocido en 'Conexiones': '{key}'.");
    }

    private static Func<DataConnector, object?> ValueGetter(string key, Lookups lk) => key.ToLowerInvariant() switch
    {
        "name" => c => c.Name,
        "kind" => c => c.Kind.ToString(),
        "dbengine" => c => c.DbEngine?.ToString(),
        "host" => c => c.Host,
        "port" => c => c.Port is int p ? (long?)p : null,
        "databasename" => c => c.DatabaseName,
        "endpointurl" => c => c.EndpointUrl,
        "httpmethod" => c => c.HttpMethod,
        "authkind" => c => c.AuthKind.ToString(),
        "container" => c => c.ContainerId is Guid g && lk.Containers.TryGetValue(g, out var n) ? n : null,
        "isactive" => c => c.IsActive,
        "createdat" => c => c.CreatedAt,
        "updatedat" => c => c.UpdatedAt,
        _ => _ => null
    };

    private static IEnumerable<DataConnector> ApplySort(IEnumerable<DataConnector> rows, IReadOnlyList<ReportSort> sorts, Lookups lk)
    {
        if (sorts.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<DataConnector>? ordered = null;
        foreach (var s in sorts)
        {
            Func<DataConnector, object?> key = ValueGetter(s.FieldKey, lk);
            if (ordered is null)
            {
                ordered = s.Descending ? rows.OrderByDescending(key, NullSafeComparer.Instance) : rows.OrderBy(key, NullSafeComparer.Instance);
            }
            else
            {
                ordered = s.Descending ? ordered.ThenByDescending(key, NullSafeComparer.Instance) : ordered.ThenBy(key, NullSafeComparer.Instance);
            }
        }

        return ordered ?? rows;
    }

    private sealed class NullSafeComparer : IComparer<object?>
    {
        public static readonly NullSafeComparer Instance = new();

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
