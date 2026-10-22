using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramientas del ASISTENTE DE TABLAS DERIVADAS (ADR-0130, fase 2 de ADR-0127). Permite a un modelo de IA
/// describir una tabla del contenedor (columnas + muestra), PROPONER una derivacion (valida la spec contra
/// <see cref="IDerivedTableService"/> y devuelve la vista previa) y CREAR la tabla derivada (misma transaccion
/// y validaciones que el wizard, incluido "un solo nivel"). Tenant-scoped por el filtro global. Las
/// columnas se nombran por NOMBRE (no por id): el modelo no adivina GUIDs. El <c>contenedor_id</c> lo
/// inyecta el servicio de chat en cada llamada; el modelo no puede apuntar a otro contenedor.
/// </summary>
public interface IDerivedTableToolset : IAgentToolset
{
    /// <summary>Herramientas de SOLO LECTURA: corren sin gate humano.</summary>
    IReadOnlySet<string> ReadOnlyTools { get; }
}

public sealed class DerivedTableToolset : IDerivedTableToolset
{
    public const string DescribeTool = "describir_tabla";
    public const string ProposeTool = "proponer_derivacion";
    public const string CreateTool = "crear_tabla_derivada";
    public const string ModelIdArg = "contenedor_id";

    /// <summary>Filas de muestra que devuelve describir_tabla.</summary>
    public const int SampleRows = 10;
    /// <summary>Grupos que devuelve la vista previa de proponer_derivacion.</summary>
    public const int PreviewGroups = 10;

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDerivedTableService _derived;

    public DerivedTableToolset(IApplicationDbContext db, ITenantContext tenant, IDerivedTableService derived)
    {
        _db = db;
        _tenant = tenant;
        _derived = derived;
    }

    public string GroupKey => "tabla-derivada";
    public string GroupLabel => "Tabla derivada del contenedor";

    public IReadOnlySet<string> ReadOnlyTools { get; } = new HashSet<string>(StringComparer.Ordinal) { DescribeTool, ProposeTool };

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // Fragmento de schema compartido por proponer y crear (la receta de la derivacion).
    private const string RecipeProps =
        "\"tabla_origen\":{\"type\":\"string\",\"description\":\"Nombre EXACTO (o id) de la tabla origen del contenedor. No puede ser una tabla derivada.\"}," +
        "\"agrupar_por\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Nombres EXACTOS de las columnas por las que se agrupa (una fila por combinacion). Minimo una.\"}," +
        "\"agregados\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"funcion\":{\"type\":\"string\",\"enum\":[\"count\",\"sum\",\"avg\",\"min\",\"max\"]}," +
            "\"columna\":{\"type\":\"string\",\"description\":\"Columna origen (no aplica para count). sum/avg solo sobre numericas; min/max sobre numericas o fechas.\"}," +
            "\"nombre\":{\"type\":\"string\",\"description\":\"Nombre de la columna resultado (ej. 'Total precio'). Opcional: se deduce.\"}}," +
            "\"required\":[\"funcion\"]},\"description\":\"Agregados por grupo. 'count' (cantidad de filas) se incluye SIEMPRE aunque no lo mandes.\"}," +
        "\"filtro\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
            "\"columna\":{\"type\":\"string\"}," +
            "\"operador\":{\"type\":\"string\",\"enum\":[\"igual\",\"distinto\",\"contiene\",\"mayor\",\"menor\",\"vacio\",\"no_vacio\"]}," +
            "\"valor\":{\"type\":\"string\"}},\"required\":[\"columna\",\"operador\"]}," +
            "\"description\":\"Condiciones previas (AND): solo entran al calculo las filas del origen que las cumplan todas. Opcional.\"}";

    public IReadOnlyList<AiToolSpec> GetSpecs() => new[]
    {
        new AiToolSpec(
            DescribeTool,
            "Describe una tabla del contenedor: sus columnas con tipo (texto/num/dec/fecha/bool), cuantas filas tiene, " +
            "si ya es derivada (y entonces NO sirve como origen) y una MUESTRA de hasta 10 filas para entender el " +
            "contenido real de cada columna. Usala antes de proponer cuando no tengas claro que columna significa que.",
            "{\"type\":\"object\",\"properties\":{\"" + ModelIdArg + "\":{\"type\":\"string\",\"description\":\"Id del contenedor (lo da el sistema).\"}," +
            "\"tabla\":{\"type\":\"string\",\"description\":\"Nombre EXACTO (o id) de la tabla.\"}},\"required\":[\"tabla\"],\"additionalProperties\":false}"),
        new AiToolSpec(
            ProposeTool,
            "VALIDA una derivacion y devuelve su VISTA PREVIA (los primeros 10 grupos calculados con el mismo motor " +
            "que usara el refresco) sin crear nada. Devuelve las columnas que tendria la tabla derivada, el total de " +
            "grupos y las filas de muestra. Si algo no cuadra (columna inexistente, sum sobre texto, origen derivado) " +
            "devuelve el error para que corrijas. Llamala SIEMPRE antes de crear.",
            "{\"type\":\"object\",\"properties\":{\"" + ModelIdArg + "\":{\"type\":\"string\",\"description\":\"Id del contenedor (lo da el sistema).\"}," +
            RecipeProps + "},\"required\":[\"tabla_origen\",\"agrupar_por\"],\"additionalProperties\":false}"),
        new AiToolSpec(
            CreateTool,
            "CREA la tabla derivada en el contenedor (tabla hija + columnas + conector 'Desde otra tabla' + proceso de " +
            "refresco Upsert por 'Clave'), en una transaccion. Es una accion MUTANTE: el usuario debe confirmarla. " +
            "Usa la MISMA receta que validaste con proponer_derivacion. El primer refresco lo dispara el sistema al confirmar.",
            "{\"type\":\"object\",\"properties\":{\"" + ModelIdArg + "\":{\"type\":\"string\",\"description\":\"Id del contenedor (lo da el sistema).\"}," +
            "\"nombre\":{\"type\":\"string\",\"description\":\"Nombre de la tabla derivada (unico en el contenedor).\"}," +
            "\"descripcion\":{\"type\":\"string\"}," +
            RecipeProps + "," +
            "\"refresco\":{\"type\":\"object\",\"properties\":{\"tipo\":{\"type\":\"string\",\"enum\":[\"manual\",\"intervalo\",\"cron\"]}," +
            "\"cada_minutos\":{\"type\":\"integer\"},\"cron\":{\"type\":\"string\",\"description\":\"Expresion cron de 5 campos, hora del tenant.\"}}," +
            "\"description\":\"Cuando se refresca. Si no se indica: manual (boton 'Actualizar datos').\"}}," +
            "\"required\":[\"nombre\",\"tabla_origen\",\"agrupar_por\"],\"additionalProperties\":false}"),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch (JsonException) { return Err("Los argumentos no son un JSON valido."); }
        if (args.ValueKind != JsonValueKind.Object) { return Err("Los argumentos deben ser un objeto JSON."); }

        try
        {
            if (!Guid.TryParse(Str(args, ModelIdArg), out var modelId) || modelId == Guid.Empty)
            {
                return Err($"Falta '{ModelIdArg}' (id del contenedor).");
            }
            return toolName switch
            {
                DescribeTool => await DescribeAsync(modelId, Str(args, "tabla"), cancellationToken),
                ProposeTool => await ProposeAsync(modelId, args, cancellationToken),
                CreateTool => await CreateAsync(modelId, args, actorUserId, cancellationToken),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (InvalidOperationException ex)
        {
            return Err(ex.Message);
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    // ---- describir_tabla ----

    private async Task<AgentToolResult> DescribeAsync(Guid modelId, string? tabla, CancellationToken ct)
    {
        var table = await ResolveTableAsync(modelId, tabla, ct);
        var cols = await LoadColumnsAsync(table.Id, ct);
        var colName = cols.ToDictionary(c => c.Id, c => c.Name);

        var total = await _db.DataContainerRows.AsNoTracking()
            .CountAsync(r => r.ContainerId == table.Id && r.ParentRowId == null, ct);
        var rowIds = await _db.DataContainerRows.AsNoTracking()
            .Where(r => r.ContainerId == table.Id && r.ParentRowId == null)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Take(SampleRows)
            .Select(r => r.Id)
            .ToListAsync(ct);
        var cells = rowIds.Count == 0
            ? new List<(Guid RowId, Guid ColumnId, string? Value)>()
            : (await _db.DataContainerCells.AsNoTracking()
                .Where(c => rowIds.Contains(c.RowId))
                .Select(c => new { c.RowId, c.ColumnId, c.Value })
                .ToListAsync(ct)).Select(c => (c.RowId, c.ColumnId, c.Value)).ToList();
        var byRow = cells.GroupBy(c => c.RowId).ToDictionary(g => g.Key, g => g.ToList());

        var muestra = new List<Dictionary<string, string?>>();
        foreach (var id in rowIds)
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (byRow.TryGetValue(id, out var rc))
            {
                foreach (var (_, columnId, value) in rc)
                {
                    if (colName.TryGetValue(columnId, out var n) && !dict.ContainsKey(n)) { dict[n] = value; }
                }
            }
            muestra.Add(dict);
        }

        return Ok(new
        {
            ok = true,
            tabla = new
            {
                id = table.Id,
                nombre = table.Name,
                descripcion = table.Description,
                derivada = table.DerivedFromContainerId is not null,
                advertencia = table.DerivedFromContainerId is not null
                    ? "Esta tabla YA es derivada: no se puede usar como origen (un solo nivel)." : null
            },
            columnas = cols.Select(c => new
            {
                nombre = c.Name,
                tipo = TypeLabel(c.Type),
                agrupable = DerivedRowsBuilder.IsScalar(c.Type),
                agregados = AggregatesFor(c.Type)
            }),
            total_filas = total,
            muestra
        });
    }

    // ---- proponer_derivacion ----

    private async Task<AgentToolResult> ProposeAsync(Guid modelId, JsonElement args, CancellationToken ct)
    {
        var (table, cols, spec) = await BuildSpecAsync(modelId, args, ct);
        var preview = await _derived.PreviewAsync(spec, PreviewGroups, ct);
        return Ok(new
        {
            ok = true,
            origen = table.Name,
            resumen = Summarize(spec, cols),
            columnas_resultado = preview.Columns.Select(c => new { nombre = c.Name, tipo = TypeLabel(c.Type), rol = RoleLabel(c.Role) }),
            total_grupos = preview.TotalGroups,
            filas_origen = preview.SourceRowsRead,
            filas_tras_filtro = preview.SourceRowsAfterFilter,
            vista_previa = preview.Rows,
            nota = "Vista previa calculada con el mismo motor del refresco. Si el usuario esta de acuerdo, llama a " +
                   CreateTool + " con la MISMA receta y un nombre."
        });
    }

    // ---- crear_tabla_derivada ----

    private async Task<AgentToolResult> CreateAsync(Guid modelId, JsonElement args, Guid actorUserId, CancellationToken ct)
    {
        var name = (Str(args, "nombre") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { return Err("Falta 'nombre' de la tabla derivada."); }

        var (table, cols, spec) = await BuildSpecAsync(modelId, args, ct);

        var kind = ImportScheduleKind.Manual;
        int? interval = null;
        string? cron = null;
        if (args.TryGetProperty("refresco", out var ref0) && ref0.ValueKind == JsonValueKind.Object)
        {
            var tipo = (Str(ref0, "tipo") ?? "manual").Trim().ToLowerInvariant();
            switch (tipo)
            {
                case "intervalo":
                    var minutes = Int(ref0, "cada_minutos");
                    if (minutes is null || minutes <= 0) { return Err("El refresco por intervalo necesita 'cada_minutos' > 0."); }
                    kind = ImportScheduleKind.Interval;
                    interval = minutes;
                    break;
                case "cron":
                    cron = Str(ref0, "cron");
                    if (string.IsNullOrWhiteSpace(cron)) { return Err("El refresco cron necesita 'cron' (5 campos)."); }
                    kind = ImportScheduleKind.Cron;
                    break;
                case "manual":
                    break;
                default:
                    return Err($"Tipo de refresco desconocido: '{tipo}' (manual | intervalo | cron).");
            }
        }

        var req = new CreateDerivedTableRequest(modelId, name, Str(args, "descripcion"), spec, kind, interval, cron);
        var created = await _derived.CreateAsync(req, actorUserId, ct);
        return Ok(new
        {
            ok = true,
            tabla_id = created.TableId,
            conector_id = created.ConnectorId,
            proceso_id = created.ProcessId,
            nombre = name,
            origen = table.Name,
            resumen = Summarize(spec, cols),
            mensaje = $"Tabla derivada '{name}' creada a partir de '{table.Name}'. El sistema dispara el primer refresco."
        });
    }

    // ---- Receta -> DerivationSpec (valida nombres, tipos y un solo nivel) ----

    private sealed record TableInfo(Guid Id, string Name, string? Description, Guid? DerivedFromContainerId);

    private async Task<(TableInfo Table, IReadOnlyList<SourceColumnInfo> Columns, DerivationSpec Spec)> BuildSpecAsync(Guid modelId, JsonElement args, CancellationToken ct)
    {
        var table = await ResolveTableAsync(modelId, Str(args, "tabla_origen"), ct);
        if (table.DerivedFromContainerId is not null)
        {
            throw new InvalidOperationException($"'{table.Name}' ya es una tabla derivada: no se puede derivar de una derivada (un solo nivel). Elige una tabla normal como origen.");
        }
        var cols = await LoadColumnsAsync(table.Id, ct);
        var byName = new Dictionary<string, SourceColumnInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cols) { byName.TryAdd(c.Name.Trim(), c); }

        SourceColumnInfo Col(string? raw, string ctx)
        {
            var n = (raw ?? "").Trim();
            if (n.Length == 0) { throw new InvalidOperationException($"Falta la columna en {ctx}."); }
            if (byName.TryGetValue(n, out var c)) { return c; }
            var match = cols.FirstOrDefault(x => Guid.TryParse(n, out var g) && x.Id == g);
            if (match is not null) { return match; }
            throw new InvalidOperationException($"La columna '{n}' no existe en '{table.Name}'. Columnas: {string.Join(", ", cols.Select(x => x.Name))}.");
        }

        // Agrupar por
        var groupBy = new List<Guid>();
        if (args.TryGetProperty("agrupar_por", out var gb) && gb.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in gb.EnumerateArray())
            {
                var c = Col(g.ValueKind == JsonValueKind.String ? g.GetString() : g.GetRawText(), "agrupar_por");
                if (!DerivedRowsBuilder.IsScalar(c.Type)) { throw new InvalidOperationException($"La columna '{c.Name}' no sirve para agrupar (no es escalar)."); }
                if (!groupBy.Contains(c.Id)) { groupBy.Add(c.Id); }
            }
        }
        if (groupBy.Count == 0) { throw new InvalidOperationException("Indica al menos una columna en 'agrupar_por'."); }

        // Agregados (Count siempre, como en el wizard)
        var aggs = new List<DerivationAggregate>();
        string? countName = null;
        if (args.TryGetProperty("agregados", out var ag) && ag.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in ag.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) { continue; }
                var fn = ParseFn(Str(a, "funcion"));
                var target = (Str(a, "nombre") ?? "").Trim();
                if (fn == DerivationFn.Count)
                {
                    countName = string.IsNullOrWhiteSpace(target) ? countName : target;
                    continue;
                }
                var c = Col(Str(a, "columna"), $"el agregado {fn}");
                if (fn is DerivationFn.Sum or DerivationFn.Avg && !DerivedRowsBuilder.IsNumeric(c.Type))
                {
                    throw new InvalidOperationException($"{FnLabel(fn)} solo aplica a columnas numericas y '{c.Name}' es {TypeLabel(c.Type)}.");
                }
                if (fn is DerivationFn.Min or DerivationFn.Max && !(DerivedRowsBuilder.IsNumeric(c.Type) || c.Type == DataContainerColumnType.Date))
                {
                    throw new InvalidOperationException($"{FnLabel(fn)} solo aplica a columnas numericas o de fecha y '{c.Name}' es {TypeLabel(c.Type)}.");
                }
                if (string.IsNullOrWhiteSpace(target)) { target = $"{FnLabel(fn)} {c.Name}"; }
                aggs.Add(new DerivationAggregate(c.Id, fn, target));
            }
        }
        aggs.Insert(0, new DerivationAggregate(null, DerivationFn.Count, string.IsNullOrWhiteSpace(countName) ? "Cantidad" : countName!));

        // Filtro
        List<DerivationFilter>? filters = null;
        if (args.TryGetProperty("filtro", out var fl) && fl.ValueKind == JsonValueKind.Array)
        {
            filters = new List<DerivationFilter>();
            foreach (var f in fl.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) { continue; }
                var c = Col(Str(f, "columna"), "el filtro");
                var op = ParseOp(Str(f, "operador"));
                filters.Add(new DerivationFilter(c.Id, op, Str(f, "valor")));
            }
            if (filters.Count == 0) { filters = null; }
        }

        var spec = new DerivationSpec(table.Id, groupBy, aggs, filters);
        var plan = DerivedRowsBuilder.Plan(spec, cols);
        if (!plan.Ok) { throw new InvalidOperationException(plan.Error!); }
        return (table, cols, spec);
    }

    private async Task<TableInfo> ResolveTableAsync(Guid modelId, string? raw, CancellationToken ct)
    {
        var n = (raw ?? "").Trim();
        if (n.Length == 0) { throw new InvalidOperationException("Falta el nombre de la tabla."); }
        var tables = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId == modelId && c.ParentContainerId == null)
            .Select(c => new TableInfo(c.Id, c.Name, c.Description, c.DerivedFromContainerId))
            .ToListAsync(ct);
        if (tables.Count == 0) { throw new InvalidOperationException("El contenedor no existe o no tiene tablas."); }

        // Por id (lo mas seguro) -> nombre EXACTO -> nombre sin distinguir mayusculas SOLO si es unico.
        // Hallado en ALEGRA: 'Items' e 'ITEMS' conviven; la busqueda case-insensitive devolvia la equivocada.
        if (Guid.TryParse(n, out var g) && tables.FirstOrDefault(t => t.Id == g) is { } byId) { return byId; }
        var exact = tables.Where(t => string.Equals(t.Name.Trim(), n, StringComparison.Ordinal)).ToList();
        if (exact.Count == 1) { return exact[0]; }
        var loose = tables.Where(t => string.Equals(t.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)).ToList();
        if (loose.Count == 1) { return loose[0]; }
        if (loose.Count > 1)
        {
            throw new InvalidOperationException($"Hay varias tablas que se llaman '{n}' (solo cambian las mayusculas): {string.Join(", ", loose.Select(t => $"'{t.Name}' (id {t.Id:D})"))}. Usa el nombre EXACTO o el id.");
        }
        throw new InvalidOperationException($"No existe la tabla '{n}' en este contenedor. Tablas: {string.Join(", ", tables.Select(t => t.Name))}.");
    }

    private async Task<IReadOnlyList<SourceColumnInfo>> LoadColumnsAsync(Guid tableId, CancellationToken ct)
        => await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == tableId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new SourceColumnInfo(c.Id, c.Name, c.Type))
            .ToListAsync(ct);

    // ---- Vocabulario (tolerante a espanol/ingles) ----

    private static DerivationFn ParseFn(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "count" or "cantidad" or "conteo" or "contar" => DerivationFn.Count,
        "sum" or "suma" or "sumar" or "total" => DerivationFn.Sum,
        "avg" or "promedio" or "average" or "media" => DerivationFn.Avg,
        "min" or "minimo" or "minimum" => DerivationFn.Min,
        "max" or "maximo" or "maximum" => DerivationFn.Max,
        var x => throw new InvalidOperationException($"Funcion de agregado desconocida: '{x}' (count | sum | avg | min | max).")
    };

    private static DerivationFilterOp ParseOp(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "igual" or "equals" or "=" or "==" => DerivationFilterOp.Equals,
        "distinto" or "notequals" or "!=" or "<>" => DerivationFilterOp.NotEquals,
        "contiene" or "contains" => DerivationFilterOp.Contains,
        "mayor" or "greaterthan" or ">" => DerivationFilterOp.GreaterThan,
        "menor" or "lessthan" or "<" => DerivationFilterOp.LessThan,
        "vacio" or "isempty" => DerivationFilterOp.IsEmpty,
        "no_vacio" or "novacio" or "notempty" => DerivationFilterOp.NotEmpty,
        var x => throw new InvalidOperationException($"Operador de filtro desconocido: '{x}' (igual | distinto | contiene | mayor | menor | vacio | no_vacio).")
    };

    private static string FnLabel(DerivationFn f) => f switch
    {
        DerivationFn.Sum => "Suma",
        DerivationFn.Avg => "Promedio",
        DerivationFn.Min => "Minimo",
        DerivationFn.Max => "Maximo",
        _ => "Cantidad"
    };

    private static string TypeLabel(DataContainerColumnType t) => t switch
    {
        DataContainerColumnType.Text => "texto",
        DataContainerColumnType.Number => "num",
        DataContainerColumnType.Decimal => "dec",
        DataContainerColumnType.Date => "fecha",
        DataContainerColumnType.Boolean => "bool",
        DataContainerColumnType.Submodel => "subtabla",
        _ => t.ToString().ToLowerInvariant()
    };

    private static string RoleLabel(DerivedColumnRole r) => r switch
    {
        DerivedColumnRole.GroupBy => "agrupacion",
        DerivedColumnRole.Key => "clave",
        _ => "agregado"
    };

    private static string[] AggregatesFor(DataContainerColumnType t)
        => DerivedRowsBuilder.IsNumeric(t) ? new[] { "sum", "avg", "min", "max" }
            : t == DataContainerColumnType.Date ? new[] { "min", "max" }
            : Array.Empty<string>();

    /// <summary>Resumen legible de la receta (para el texto del asistente y la tarjeta de propuesta).</summary>
    public static string Summarize(DerivationSpec spec, IReadOnlyList<SourceColumnInfo> cols)
    {
        var byId = cols.ToDictionary(c => c.Id, c => c.Name);
        string N(Guid id) => byId.TryGetValue(id, out var n) ? n : id.ToString("N")[..8];
        var parts = new List<string>
        {
            "agrupa por " + string.Join(", ", spec.GroupBy.Select(N)),
            "agregados: " + string.Join(", ", spec.Aggregates.Select(a => a.SourceColumnId is Guid cid
                ? $"{a.TargetColumnName} = {FnLabel(a.Fn)}({N(cid)})" : $"{a.TargetColumnName} = Cantidad"))
        };
        if (spec.Filter is { Count: > 0 })
        {
            parts.Add("filtro: " + string.Join(" y ", spec.Filter.Select(f => $"{N(f.ColumnId)} {f.Op} {f.Value}".Trim())));
        }
        return string.Join("; ", parts);
    }

    // ---- Lectura tipada de los resultados (para la UI del panel) ----

    /// <summary>Extrae la vista previa del resultado JSON de proponer_derivacion; null si no es una.</summary>
    public static DerivationPreviewDto? TryParsePreview(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !IsOk(root) || !root.TryGetProperty("vista_previa", out var vp) || vp.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var cols = new List<DerivedColumnPlan>();
            if (root.TryGetProperty("columnas_resultado", out var cr) && cr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cr.EnumerateArray())
                {
                    var name = Str(c, "nombre") ?? "";
                    var type = Str(c, "tipo") switch
                    {
                        "num" => DataContainerColumnType.Number,
                        "dec" => DataContainerColumnType.Decimal,
                        "fecha" => DataContainerColumnType.Date,
                        "bool" => DataContainerColumnType.Boolean,
                        _ => DataContainerColumnType.Text
                    };
                    var role = Str(c, "rol") switch
                    {
                        "agrupacion" => DerivedColumnRole.GroupBy,
                        "clave" => DerivedColumnRole.Key,
                        _ => DerivedColumnRole.Aggregate
                    };
                    cols.Add(new DerivedColumnPlan(name, type, role));
                }
            }
            var rows = new List<IReadOnlyDictionary<string, string?>>();
            foreach (var r in vp.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) { continue; }
                var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in r.EnumerateObject())
                {
                    d[p.Name] = p.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => p.Value.GetString(),
                        _ => p.Value.GetRawText()
                    };
                }
                rows.Add(d);
            }
            return new DerivationPreviewDto(cols, rows,
                Int(root, "total_grupos") ?? rows.Count, Int(root, "filas_origen") ?? 0, Int(root, "filas_tras_filtro") ?? 0);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Extrae lo creado del resultado JSON de crear_tabla_derivada; null si no fue un alta exitosa.</summary>
    public static (DerivedTableCreatedDto Created, string Name)? TryParseCreated(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !IsOk(root)) { return null; }
            if (!Guid.TryParse(Str(root, "tabla_id"), out var t) || !Guid.TryParse(Str(root, "conector_id"), out var c) || !Guid.TryParse(Str(root, "proceso_id"), out var p))
            {
                return null;
            }
            return (new DerivedTableCreatedDto(t, c, p), Str(root, "nombre") ?? "");
        }
        catch (JsonException) { return null; }
    }

    private static bool IsOk(JsonElement root)
        => root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            }
            : null;

    private static int? Int(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) { return null; }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) { return n; }
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) { return s; }
        return null;
    }

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);
}
