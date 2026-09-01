using System.Linq.Expressions;
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
///
/// Campos por NOMBRE via join (Concepto, Categoria, Asignado, Tablero, Etapa): se resuelven contra
/// tablas de catalogo TENANT-SCOPED (mismo filtro global) cargadas como diccionarios id->nombre; el
/// nombre se mapea EN MEMORIA (agrupar/ordenar/pintar) y los filtros por nombre se traducen a un
/// <c>WHERE id IN (...)</c> sobre el conjunto de ids que casan. Nunca hay SQL crudo ni cross-tenant.
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
        new ReportField("AssigneeUserId", "Asignado (ID)", ReportFieldType.Text),
        // Campos por NOMBRE (join a catalogos tenant-scoped): reportables, agrupables y filtrables.
        new ReportField("Concepto", "Concepto", ReportFieldType.Text),
        new ReportField("Categoria", "Categoria", ReportFieldType.Text),
        new ReportField("AssigneeName", "Asignado", ReportFieldType.Text),
        new ReportField("Board", "Tablero", ReportFieldType.Text),
        new ReportField("Stage", "Etapa", ReportFieldType.Text),
        new ReportField("IsArchived", "Archivada", ReportFieldType.Boolean)
    };

    private readonly IApplicationDbContext _db;

    public TaskItemReportSource(IApplicationDbContext db) => _db = db;

    public ReportSourceDescriptor Describe() =>
        new(SourceKey, "Actividades", ReportSourceKind.Native, FieldSet);

    public async Task<ReportDataSet> QueryAsync(ReportQuerySpec spec, ReportContext ctx, CancellationToken ct = default)
    {
        // Catalogos tenant-scoped (id -> nombre) para los campos por nombre. Se cargan ANTES de filtrar
        // porque los filtros por nombre resuelven el conjunto de ids que casan.
        var lk = await LoadLookupsAsync(ct);

        IQueryable<TaskItem> q = _db.TaskItems.AsNoTracking();
        q = ApplyFilters(q, spec.Filters, lk);

        return spec.IsAggregated
            ? await QueryAggregatedAsync(q, spec, lk, ct)
            : await QueryTabularAsync(q, spec, lk, ct);
    }

    // ---- Catalogos (tenant-safe: todas las tablas llevan el filtro global) ----

    private sealed record Lookups(
        IReadOnlyDictionary<Guid, (string Sub, string? Cat)> Subcats,
        IReadOnlyDictionary<Guid, string> Boards,
        IReadOnlyDictionary<Guid, string> Columns,
        IReadOnlyDictionary<Guid, string> Users);

    private async Task<Lookups> LoadLookupsAsync(CancellationToken ct)
    {
        // Concepto (subcategoria) + Categoria (un nivel arriba). LEFT JOIN por si la categoria no existe.
        var subs = await (
            from s in _db.ActividadSubcategorias.AsNoTracking()
            join c in _db.ActividadCategorias.AsNoTracking() on s.CategoriaId equals c.Id into cj
            from c in cj.DefaultIfEmpty()
            select new { s.Id, Sub = s.Nombre, Cat = c != null ? c.Nombre : null })
            .ToListAsync(ct);
        var subMap = subs.ToDictionary(x => x.Id, x => (x.Sub, (string?)x.Cat));

        var boards = await _db.TaskBoards.AsNoTracking().Select(b => new { b.Id, b.Name }).ToListAsync(ct);
        var boardMap = boards.ToDictionary(x => x.Id, x => x.Name);

        var cols = await _db.TaskBoardColumns.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var colMap = cols.ToDictionary(x => x.Id, x => x.Name);

        // Asignado: nombre para mostrar = PlatformUser.DisplayName; si no, el correo del TenantUser.
        var users = await (
            from u in _db.TenantUsers.AsNoTracking()
            join p in _db.PlatformUsers.AsNoTracking() on u.PlatformUserId equals p.Id into pj
            from p in pj.DefaultIfEmpty()
            select new { u.Id, Name = (p != null ? p.DisplayName : null) ?? u.Email })
            .ToListAsync(ct);
        var userMap = users.ToDictionary(x => x.Id, x => x.Name);

        return new Lookups(subMap, boardMap, colMap, userMap);
    }

    // ---- Modo tabular ----

    private async Task<ReportDataSet> QueryTabularAsync(IQueryable<TaskItem> q, ReportQuerySpec spec, Lookups lk, CancellationToken ct)
    {
        var fields = spec.Fields.Count > 0
            ? spec.Fields.Select(ResolveField).ToList()
            : FieldSet.ToList();

        var entities = await q.Take(MaterializeCap).ToListAsync(ct);

        IEnumerable<TaskItem> rows = entities;
        rows = ApplySort(rows, spec.Sort, lk);
        if (spec.Top is int top && top >= 0)
        {
            rows = rows.Take(top);
        }

        var columns = fields.Select(f => new ReportColumn(f.Key, f.DisplayName, f.Type)).ToList();
        var getters = fields.Select(f => ValueGetter(f.Key, lk)).ToList();
        var data = rows
            .Select(t => (IReadOnlyList<object?>)getters.Select(g => g(t)).ToList())
            .ToList();

        return new ReportDataSet(columns, data);
    }

    // ---- Modo agregado (un campo de agrupacion + Count) ----

    private async Task<ReportDataSet> QueryAggregatedAsync(IQueryable<TaskItem> q, ReportQuerySpec spec, Lookups lk, CancellationToken ct)
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

    private async Task<List<(string? Key, int Count)>> GroupCountAsync(IQueryable<TaskItem> q, string fieldKey, Lookups lk, CancellationToken ct)
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
            // Campos por NOMBRE: se agrupa por el id en BD y luego se mapea id->nombre y se RE-SUMA por nombre.
            case "concepto":
                return MapCountByName(await GroupByGuidAsync(q, t => t.SubcategoriaId, ct),
                    id => lk.Subcats.TryGetValue(id, out var v) ? v.Sub : null);
            case "categoria":
                return MapCountByName(await GroupByGuidAsync(q, t => t.SubcategoriaId, ct),
                    id => lk.Subcats.TryGetValue(id, out var v) && !string.IsNullOrEmpty(v.Cat) ? v.Cat : null);
            case "assigneename":
                return MapCountByName(await GroupByGuidAsync(q, t => t.AssigneeTenantUserId, ct),
                    id => lk.Users.TryGetValue(id, out var n) ? n : null);
            case "board":
                return MapCountByName(await GroupByGuidAsync(q, t => t.BoardId, ct),
                    id => lk.Boards.TryGetValue(id, out var n) ? n : null);
            case "stage":
                return MapCountByName(await GroupByGuidAsync(q, t => t.ColumnId, ct),
                    id => lk.Columns.TryGetValue(id, out var n) ? n : null);
            default:
                throw new ReportValidationException($"El campo '{fieldKey}' no admite agrupacion en la fuente 'Actividades'.");
        }
    }

    private static async Task<List<(Guid? Id, int Count)>> GroupByGuidAsync(
        IQueryable<TaskItem> q, Expression<Func<TaskItem, Guid?>> selector, CancellationToken ct)
        => (await q.GroupBy(selector).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
            .Select(x => (x.Key, x.C)).ToList();

    private static List<(string? Key, int Count)> MapCountByName(List<(Guid? Id, int Count)> src, Func<Guid, string?> nameOf)
    {
        // id -> nombre (null = "sin asignar/sin dato") y se acumula por nombre.
        var agg = new Dictionary<string?, int>();
        foreach (var (id, count) in src)
        {
            var name = id is Guid g ? nameOf(g) : null;
            agg[name] = agg.TryGetValue(name, out var c) ? c + count : count;
        }
        return agg.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    // ---- Filtros (parametrizados, traducidos a EF) ----

    private static IQueryable<TaskItem> ApplyFilters(IQueryable<TaskItem> q, IReadOnlyList<ReportFilter> filters, Lookups lk)
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
                // Filtros por NOMBRE: se resuelve el conjunto de ids que casan y se filtra por id (WHERE IN).
                // List.Contains lo traduce EF a IN en ambos motores; el NotEquals incluye los sin dato (null).
                case "concepto":
                {
                    var ids = MatchIds(lk.Subcats.Select(kv => (kv.Key, kv.Value.Sub)), f);
                    q = f.Operator == ReportFilterOperator.NotEquals
                        ? q.Where(t => t.SubcategoriaId == null || !ids.Contains(t.SubcategoriaId.Value))
                        : q.Where(t => t.SubcategoriaId != null && ids.Contains(t.SubcategoriaId.Value));
                    break;
                }
                case "categoria":
                {
                    var ids = MatchIds(lk.Subcats.Select(kv => (kv.Key, kv.Value.Cat)), f);
                    q = f.Operator == ReportFilterOperator.NotEquals
                        ? q.Where(t => t.SubcategoriaId == null || !ids.Contains(t.SubcategoriaId.Value))
                        : q.Where(t => t.SubcategoriaId != null && ids.Contains(t.SubcategoriaId.Value));
                    break;
                }
                case "assigneename":
                {
                    var ids = MatchIds(lk.Users.Select(kv => (kv.Key, (string?)kv.Value)), f);
                    q = f.Operator == ReportFilterOperator.NotEquals
                        ? q.Where(t => t.AssigneeTenantUserId == null || !ids.Contains(t.AssigneeTenantUserId.Value))
                        : q.Where(t => t.AssigneeTenantUserId != null && ids.Contains(t.AssigneeTenantUserId.Value));
                    break;
                }
                case "board":
                {
                    var ids = MatchIds(lk.Boards.Select(kv => (kv.Key, (string?)kv.Value)), f);
                    q = f.Operator == ReportFilterOperator.NotEquals
                        ? q.Where(t => t.BoardId == null || !ids.Contains(t.BoardId.Value))
                        : q.Where(t => t.BoardId != null && ids.Contains(t.BoardId.Value));
                    break;
                }
                case "stage":
                {
                    var ids = MatchIds(lk.Columns.Select(kv => (kv.Key, (string?)kv.Value)), f);
                    q = f.Operator == ReportFilterOperator.NotEquals
                        ? q.Where(t => t.ColumnId == null || !ids.Contains(t.ColumnId.Value))
                        : q.Where(t => t.ColumnId != null && ids.Contains(t.ColumnId.Value));
                    break;
                }
                default:
                    throw new ReportValidationException($"El campo '{f.FieldKey}' no admite filtro en la fuente 'Actividades'.");
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

    private static IQueryable<TaskItem> ApplyTextFilter(IQueryable<TaskItem> q, ReportFilter f, Expression<Func<TaskItem, string?>> selector)
    {
        var val = (f.Values.Count > 0 ? f.Values[0] : null)?.ToLowerInvariant() ?? string.Empty;
        var member = selector.Body;
        var param = selector.Parameters[0];

        // Contains -> LIKE %v%; Equals -> comparacion exacta case-insensitive; ambos traducibles en ambos motores.
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

        var lambda = Expression.Lambda<Func<TaskItem, bool>>(body, param);
        return q.Where(lambda);
    }

    private static Expression ToLower(Expression member) =>
        Expression.Call(
            Expression.Coalesce(member, Expression.Constant(string.Empty)),
            typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);

    private static IQueryable<TaskItem> ApplyDateFilter(IQueryable<TaskItem> q, ReportFilter f, Expression<Func<TaskItem, DateTimeOffset?>> selector)
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

        return q.Where(Expression.Lambda<Func<TaskItem, bool>>(body, param));
    }

    private static Expression Cmp(Expression member, DateTimeOffset value, ExpressionType op)
    {
        // member es DateTimeOffset?; se compara contra un DateTimeOffset? constante para traducir en EF.
        var constant = Expression.Constant((DateTimeOffset?)value, typeof(DateTimeOffset?));
        return Expression.MakeBinary(op, member, constant);
    }

    // ---- Mapeos de campo ----

    private static ReportField ResolveField(string key)
    {
        var f = FieldSet.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return f ?? throw new ReportValidationException($"Campo desconocido en 'Actividades': '{key}'.");
    }

    private static Func<TaskItem, object?> ValueGetter(string key, Lookups lk) => key.ToLowerInvariant() switch
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
        "concepto" => t => t.SubcategoriaId is Guid g && lk.Subcats.TryGetValue(g, out var v) ? v.Sub : null,
        "categoria" => t => t.SubcategoriaId is Guid g && lk.Subcats.TryGetValue(g, out var v) && !string.IsNullOrEmpty(v.Cat) ? v.Cat : null,
        "assigneename" => t => t.AssigneeTenantUserId is Guid g && lk.Users.TryGetValue(g, out var n) ? n : null,
        "board" => t => t.BoardId is Guid g && lk.Boards.TryGetValue(g, out var n) ? n : null,
        "stage" => t => t.ColumnId is Guid g && lk.Columns.TryGetValue(g, out var n) ? n : null,
        "isarchived" => t => t.IsArchived,
        _ => _ => null
    };

    private static IEnumerable<TaskItem> ApplySort(IEnumerable<TaskItem> rows, IReadOnlyList<ReportSort> sorts, Lookups lk)
    {
        if (sorts.Count == 0)
        {
            return rows;
        }

        IOrderedEnumerable<TaskItem>? ordered = null;
        foreach (var s in sorts)
        {
            Func<TaskItem, object?> key = ValueGetter(s.FieldKey, lk);
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
