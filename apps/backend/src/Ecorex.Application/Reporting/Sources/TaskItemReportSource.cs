using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Sources;

/// <summary>
/// Fuente reportable NATIVA para <see cref="TaskItem"/> (entidad curada por el dev). Declara sus
/// campos logicos de negocio y sabe consultarse con LINQ tipado sobre <c>_db.TaskItems</c>, que lleva
/// el filtro global tenant: la consulta es tenant-safe por construccion (imposible pedir cross-tenant).
///
/// Alcance v1 (Ola 1): filtros parametrizados, modo tabular y modo agregado con UN campo de
/// agrupacion + conteo (Count). TaskItem no tiene un campo numerico de negocio, asi que Sum/Avg/Min/Max
/// no aplican todavia (se rechazan con error claro); la agregacion numerica se demuestra en la fuente
/// de contenedor. Ordenacion y Top se aplican en memoria sobre el conjunto ya acotado al tenant.
/// </summary>
public sealed class TaskItemReportSource : IReportableSource
{
    public const string SourceKey = "native:taskitem";

    // Guarda dura contra resultados enormes al materializar (el conjunto ya viene acotado al tenant).
    private const int MaterializeCap = 50_000;

    private static readonly IReadOnlyList<ReportField> FieldSet = new[]
    {
        new ReportField("Number", "Numero", ReportFieldType.Text),
        new ReportField("Title", "Titulo", ReportFieldType.Text),
        new ReportField("Status", "Estado", ReportFieldType.Text),
        new ReportField("Priority", "Prioridad", ReportFieldType.Text),
        new ReportField("DueDate", "Vence", ReportFieldType.Date),
        new ReportField("StartDate", "Inicio", ReportFieldType.Date),
        new ReportField("ClosedAt", "Cerrada", ReportFieldType.Date),
        new ReportField("CreatedAt", "Creada", ReportFieldType.Date),
        new ReportField("ProjectId", "Proyecto", ReportFieldType.Text),
        new ReportField("AssigneeUserId", "Asignado", ReportFieldType.Text),
        new ReportField("IsArchived", "Archivada", ReportFieldType.Boolean)
    };

    private readonly IApplicationDbContext _db;

    public TaskItemReportSource(IApplicationDbContext db) => _db = db;

    public ReportSourceDescriptor Describe() =>
        new(SourceKey, "Actividades", ReportSourceKind.Native, FieldSet);

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        IQueryable<TaskItem> q = _db.TaskItems.AsNoTracking();
        q = ApplyFilters(q, spec.Filters);

        return spec.IsAggregated
            ? await QueryAggregatedAsync(q, spec, ct)
            : await QueryTabularAsync(q, spec, ct);
    }

    // ---- Modo tabular ----

    private async Task<ReportDataSet> QueryTabularAsync(IQueryable<TaskItem> q, ReportQuerySpec spec, CancellationToken ct)
    {
        var fields = spec.Fields.Count > 0
            ? spec.Fields.Select(ResolveField).ToList()
            : FieldSet.ToList();

        var entities = await q.Take(MaterializeCap).ToListAsync(ct);

        IEnumerable<TaskItem> rows = entities;
        rows = ApplySort(rows, spec.Sort);
        if (spec.Top is int top && top >= 0)
        {
            rows = rows.Take(top);
        }

        var columns = fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();
        var getters = fields.Select(f => ValueGetter(f.Key)).ToList();
        var data = rows
            .Select(t => (IReadOnlyList<object?>)getters.Select(g => g(t)).ToList())
            .ToList();

        return new ReportDataSet(columns, data);
    }

    // ---- Modo agregado (un campo de agrupacion + Count) ----

    private async Task<ReportDataSet> QueryAggregatedAsync(IQueryable<TaskItem> q, ReportQuerySpec spec, CancellationToken ct)
    {
        if (spec.GroupBy.Count != 1)
        {
            throw new ReportValidationException("La fuente 'Actividades' soporta exactamente un campo de agrupacion en v1.");
        }

        foreach (var agg in spec.Aggregates)
        {
            if (agg.Function != ReportAggregateFunction.Count)
            {
                throw new ReportValidationException(
                    "La fuente 'Actividades' no tiene campos numericos agregables en v1: solo se admite el conteo (Count).");
            }
        }

        var groupField = ResolveField(spec.GroupBy[0]);
        var pairs = await GroupCountAsync(q, groupField.Key, ct);

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

    private async Task<List<(string? Key, int Count)>> GroupCountAsync(IQueryable<TaskItem> q, string fieldKey, CancellationToken ct)
    {
        // Se agrupa EN LA BD (traducido a GROUP BY) y luego la clave se normaliza a texto en memoria.
        switch (fieldKey.ToLowerInvariant())
        {
            case "status":
                return (await q.GroupBy(t => t.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key.ToString(), x.C)).ToList();
            case "priority":
                return (await q.GroupBy(t => t.Priority).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key.ToString(), x.C)).ToList();
            case "projectid":
                return (await q.GroupBy(t => t.ProjectId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)(x.Key?.ToString()), x.C)).ToList();
            case "assigneeuserid":
                return (await q.GroupBy(t => t.AssigneeTenantUserId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)(x.Key?.ToString()), x.C)).ToList();
            case "number":
                return (await q.GroupBy(t => t.Number).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key, x.C)).ToList();
            case "isarchived":
                return (await q.GroupBy(t => t.IsArchived).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
                    .Select(x => (Key: (string?)x.Key.ToString(), x.C)).ToList();
            default:
                throw new ReportValidationException($"El campo '{fieldKey}' no admite agrupacion en la fuente 'Actividades'.");
        }
    }

    // ---- Filtros (parametrizados, traducidos a EF) ----

    private static IQueryable<TaskItem> ApplyFilters(IQueryable<TaskItem> q, IReadOnlyList<ReportFilter> filters)
    {
        foreach (var f in filters)
        {
            var field = ResolveField(f.FieldKey);
            var first = f.Values.Count > 0 ? f.Values[0] : null;

            switch (field.Key.ToLowerInvariant())
            {
                case "status":
                    q = ApplyStatusFilter(q, f);
                    break;
                case "priority":
                    q = ApplyPriorityFilter(q, f);
                    break;
                case "isarchived":
                    var b = bool.TryParse(first, out var bv) && bv;
                    q = f.Operator == ReportFilterOperator.NotEquals ? q.Where(t => t.IsArchived != b) : q.Where(t => t.IsArchived == b);
                    break;
                case "title":
                    q = ApplyTextFilter(q, f, t => t.Title);
                    break;
                case "number":
                    q = ApplyTextFilter(q, f, t => t.Number);
                    break;
                case "duedate":
                    q = ApplyDateFilter(q, f, t => t.DueDate);
                    break;
                case "startdate":
                    q = ApplyDateFilter(q, f, t => t.StartDate);
                    break;
                case "closedat":
                    q = ApplyDateFilter(q, f, t => t.ClosedAt);
                    break;
                case "createdat":
                    q = ApplyDateFilter(q, f, t => t.CreatedAt);
                    break;
                case "projectid":
                    if (Guid.TryParse(first, out var pid))
                    {
                        q = f.Operator == ReportFilterOperator.NotEquals ? q.Where(t => t.ProjectId != pid) : q.Where(t => t.ProjectId == pid);
                    }
                    break;
                case "assigneeuserid":
                    if (Guid.TryParse(first, out var aid))
                    {
                        q = f.Operator == ReportFilterOperator.NotEquals ? q.Where(t => t.AssigneeTenantUserId != aid) : q.Where(t => t.AssigneeTenantUserId == aid);
                    }
                    break;
                default:
                    throw new ReportValidationException($"El campo '{f.FieldKey}' no admite filtro en la fuente 'Actividades'.");
            }
        }

        return q;
    }

    private static IQueryable<TaskItem> ApplyStatusFilter(IQueryable<TaskItem> q, ReportFilter f)
    {
        var set = ParseEnums<TaskItemStatus>(f.Values);
        if (set.Count == 0)
        {
            return q.Where(t => false);
        }

        return f.Operator == ReportFilterOperator.NotEquals
            ? q.Where(t => !set.Contains(t.Status))
            : q.Where(t => set.Contains(t.Status));
    }

    private static IQueryable<TaskItem> ApplyPriorityFilter(IQueryable<TaskItem> q, ReportFilter f)
    {
        var set = ParseEnums<TaskPriority>(f.Values);
        if (set.Count == 0)
        {
            return q.Where(t => false);
        }

        return f.Operator == ReportFilterOperator.NotEquals
            ? q.Where(t => !set.Contains(t.Priority))
            : q.Where(t => set.Contains(t.Priority));
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

    private static IQueryable<TaskItem> ApplyTextFilter(IQueryable<TaskItem> q, ReportFilter f, System.Linq.Expressions.Expression<Func<TaskItem, string?>> selector)
    {
        var val = (f.Values.Count > 0 ? f.Values[0] : null)?.ToLowerInvariant() ?? string.Empty;
        var member = selector.Body;
        var param = selector.Parameters[0];

        // Contains -> LIKE %v%; Equals -> comparacion exacta case-insensitive; ambos traducibles en ambos motores.
        System.Linq.Expressions.Expression body = f.Operator switch
        {
            ReportFilterOperator.Equals => System.Linq.Expressions.Expression.Equal(
                ToLower(member),
                System.Linq.Expressions.Expression.Constant(val, typeof(string))),
            _ => System.Linq.Expressions.Expression.Call(
                ToLower(member),
                typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!,
                System.Linq.Expressions.Expression.Constant(val, typeof(string)))
        };

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<TaskItem, bool>>(body, param);
        return q.Where(lambda);
    }

    private static System.Linq.Expressions.Expression ToLower(System.Linq.Expressions.Expression member) =>
        System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Coalesce(member, System.Linq.Expressions.Expression.Constant(string.Empty)),
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);

    private static IQueryable<TaskItem> ApplyDateFilter(IQueryable<TaskItem> q, ReportFilter f, System.Linq.Expressions.Expression<Func<TaskItem, DateTimeOffset?>> selector)
    {
        DateTimeOffset? Parse(string? s) =>
            DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

        var param = selector.Parameters[0];
        var member = selector.Body;
        var v0 = f.Values.Count > 0 ? Parse(f.Values[0]) : null;
        var v1 = f.Values.Count > 1 ? Parse(f.Values[1]) : null;

        System.Linq.Expressions.Expression? body = f.Operator switch
        {
            ReportFilterOperator.GreaterThan when v0 is not null => Cmp(member, v0.Value, System.Linq.Expressions.ExpressionType.GreaterThan),
            ReportFilterOperator.GreaterThanOrEqual when v0 is not null => Cmp(member, v0.Value, System.Linq.Expressions.ExpressionType.GreaterThanOrEqual),
            ReportFilterOperator.LessThan when v0 is not null => Cmp(member, v0.Value, System.Linq.Expressions.ExpressionType.LessThan),
            ReportFilterOperator.LessThanOrEqual when v0 is not null => Cmp(member, v0.Value, System.Linq.Expressions.ExpressionType.LessThanOrEqual),
            ReportFilterOperator.Between when v0 is not null && v1 is not null =>
                System.Linq.Expressions.Expression.AndAlso(
                    Cmp(member, v0.Value, System.Linq.Expressions.ExpressionType.GreaterThanOrEqual),
                    Cmp(member, v1.Value, System.Linq.Expressions.ExpressionType.LessThanOrEqual)),
            _ => null
        };

        if (body is null)
        {
            return q;
        }

        return q.Where(System.Linq.Expressions.Expression.Lambda<Func<TaskItem, bool>>(body, param));
    }

    private static System.Linq.Expressions.Expression Cmp(System.Linq.Expressions.Expression member, DateTimeOffset value, System.Linq.Expressions.ExpressionType op)
    {
        // member es DateTimeOffset?; se compara contra un DateTimeOffset? constante para traducir en EF.
        var constant = System.Linq.Expressions.Expression.Constant((DateTimeOffset?)value, typeof(DateTimeOffset?));
        return System.Linq.Expressions.Expression.MakeBinary(op, member, constant);
    }

    // ---- Mapeos de campo ----

    private static ReportField ResolveField(string key)
    {
        var f = FieldSet.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return f ?? throw new ReportValidationException($"Campo desconocido en 'Actividades': '{key}'.");
    }

    private static Func<TaskItem, object?> ValueGetter(string key) => key.ToLowerInvariant() switch
    {
        "number" => t => t.Number,
        "title" => t => t.Title,
        "status" => t => t.Status.ToString(),
        "priority" => t => t.Priority.ToString(),
        "duedate" => t => t.DueDate,
        "startdate" => t => t.StartDate,
        "closedat" => t => t.ClosedAt,
        "createdat" => t => t.CreatedAt,
        "projectid" => t => t.ProjectId?.ToString(),
        "assigneeuserid" => t => t.AssigneeTenantUserId?.ToString(),
        "isarchived" => t => t.IsArchived,
        _ => _ => null
    };

    private static IEnumerable<TaskItem> ApplySort(IEnumerable<TaskItem> rows, IReadOnlyList<ReportSort> sorts)
    {
        if (sorts.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<TaskItem>? ordered = null;
        foreach (var s in sorts)
        {
            var getter = ValueGetter(s.FieldKey);
            Func<TaskItem, object?> key = getter;
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
