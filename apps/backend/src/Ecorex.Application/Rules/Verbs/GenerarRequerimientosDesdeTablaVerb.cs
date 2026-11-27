using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Requerimientos;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// Verbo GENERAR_REQUERIMIENTOS_DESDE_TABLA (Capa 10): crea una necesidad de compra
/// (RequerimientoCompra) via IRequerimientoService por cada fila de un campo tabla del
/// formulario. Calcado de GENERAR_TAREAS_DESDE_TABLA. El mapeo columna->campo es configurable
/// (productoCol/cantidadCol/unidadCol/fechaCol/obsCol); el area sale de un campo de cabecera
/// (areaField) y el origen (que define el proceso de compra/cotizacion) de 'origenId' o, por fila, del
/// mapeo "origen" (id o NOMBRE del origen; una columna con la fuente "Origenes de compra"). El solicitante
/// sale del mapeo "solicitante" (id, correo o nombre de un usuario del tenant), si no de quien envio el
/// formulario y, si el formulario quedo en borrador, del creador de la actividad origen.
/// RecordsAffected = necesidades creadas.
/// </summary>
public sealed class GenerarRequerimientosDesdeTablaVerb : IRuleVerb
{
    public const string VerbName = "GENERAR_REQUERIMIENTOS_DESDE_TABLA";

    private readonly IRequerimientoService _req;
    private readonly IRequerimientoFieldService _fields;
    private readonly IApplicationDbContext _db;

    public GenerarRequerimientosDesdeTablaVerb(IRequerimientoService req, IRequerimientoFieldService fields, IApplicationDbContext db)
    {
        _req = req; _fields = fields; _db = db;
    }

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Generar requerimientos desde tabla",
        "Crea una necesidad de compra por cada fila de un campo tabla del formulario (modulo Requerimientos y Compras).",
        [
            new RuleVerbParamDescriptor("tableField", "Campo tabla", RuleParamType.FieldCode, Required: true,
                "FieldCode del grid cuyo valor es un arreglo JSON de filas (cada fila = una necesidad)."),
            new RuleVerbParamDescriptor("origenId", "Origen", RuleParamType.Select, Required: false,
                "Origen de Requerimientos al que quedan asociadas las necesidades (define el proceso de compra/cotizacion). Vacio = sin origen.",
                OptionsSource: "requerimiento-origenes"),
            new RuleVerbParamDescriptor("areaField", "Campo area (cabecera)", RuleParamType.FieldCode, Required: false,
                "FieldCode de cabecera cuyo valor es el area solicitante para todas las filas."),
            new RuleVerbParamDescriptor("productoCol", "Columna producto", RuleParamType.Text, Required: false,
                "Id de la columna con el nombre del producto (default: producto)."),
            new RuleVerbParamDescriptor("cantidadCol", "Columna cantidad", RuleParamType.Text, Required: false,
                "Id de la columna con la cantidad (default: cantidad)."),
            new RuleVerbParamDescriptor("unidadCol", "Columna unidad", RuleParamType.Text, Required: false,
                "Id de la columna con la unidad de medida (default: unidad)."),
            new RuleVerbParamDescriptor("unidadFija", "Unidad por defecto", RuleParamType.Select, Required: false,
                "Unidad de medida que se aplica cuando la fila no trae ninguna (catalogo de unidades mas comunes).",
                Options: RequerimientoUnidades.Comunes.Select(u => new RuleParamOption(u, u)).ToArray()),
            new RuleVerbParamDescriptor("fechaCol", "Columna fecha", RuleParamType.Text, Required: false,
                "Id de la columna con la fecha requerida (default: fecha_requerida)."),
            new RuleVerbParamDescriptor("obsCol", "Columna observaciones", RuleParamType.Text, Required: false,
                "Id de la columna con observaciones; se anexa a la descripcion (default: observaciones)."),
            new RuleVerbParamDescriptor("mapeo", "Mapeo de destino (avanzado)", RuleParamType.Mapping, Required: false,
                "Llena cada dato de la necesidad desde una columna del grid, un campo del formulario o un valor fijo. Sobrescribe las columnas de arriba. Incluye los campos propios de la necesidad.",
                OptionsSource: "requerimiento-campos-padre",
                Options:
                [
                    new RuleParamOption("descripcion", "Descripcion (producto)"),
                    new RuleParamOption("cantidad", "Cantidad"),
                    new RuleParamOption("unidad", "Unidad"),
                    new RuleParamOption("fecha", "Fecha requerida"),
                    new RuleParamOption("area", "Area solicitante"),
                    new RuleParamOption("observaciones", "Observaciones generales"),
                    new RuleParamOption("observaciones_despacho", "Observaciones de despacho"),
                    new RuleParamOption("origen", "Origen (id o nombre; sobrescribe)"),
                    new RuleParamOption("solicitante", "Solicitante (usuario: id, correo o nombre)")
                ]),
            new RuleVerbParamDescriptor("autoComplete", "Completar paso", RuleParamType.Boolean, Required: false,
                "Si es true y la regla es autonoma de un nodo, el paso del flujo se completa solo.")
        ]);

    public async Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var tableField = context.GetStringParam("tableField");
        if (string.IsNullOrWhiteSpace(tableField))
        {
            return RuleVerbResult.Fail("Parametro 'tableField' obligatorio (FieldCode del grid).");
        }

        var rows = ResolveRows(context, tableField!, out var rowsError);
        if (rowsError is not null) { return RuleVerbResult.Fail(rowsError); }
        if (rows.Count == 0)
        {
            return RuleVerbResult.Ok("Sin filas: no se crearon necesidades.", 0,
                autoCompleteStep: context.GetBoolParam("autoComplete"));
        }

        Guid? origenParam = Guid.TryParse(context.GetStringParam("origenId"), out var oid) ? oid : null;
        var areaField = context.GetStringParam("areaField");
        var areaHeader = !string.IsNullOrWhiteSpace(areaField)
            ? (context.FormData.GetValueOrDefault(areaField!) ?? "")
            : "";

        var productoCol = context.GetStringParam("productoCol") ?? "producto";
        var cantidadCol = context.GetStringParam("cantidadCol") ?? "cantidad";
        var unidadCol = context.GetStringParam("unidadCol") ?? "unidad";
        var fechaCol = context.GetStringParam("fechaCol") ?? "fecha_requerida";
        var obsCol = context.GetStringParam("obsCol") ?? "observaciones";
        var unidadFija = context.GetStringParam("unidadFija");

        // Mapeo avanzado destino -> "kind:ref" (col|field|fixed). Sobrescribe/añade datos del destino.
        var mapeo = ReadMapeo(context);
        // Resuelve los campos propios (Padre) por clave solo si el mapeo los usa.
        Dictionary<string, Guid>? dynDefs = null;
        if (mapeo.Keys.Any(k => k.StartsWith("dyn:", StringComparison.Ordinal)))
        {
            dynDefs = (await _fields.ListDefsAsync("Padre", cancellationToken))
                .ToDictionary(d => "dyn:" + d.FieldKey, d => d.Id, StringComparer.Ordinal);
        }

        // Trazabilidad: desde que actividad. En on-submit el contexto trae FormResponseId; la actividad
        // origen se resuelve por el Reference (= numero de tarea) de la respuesta.
        Guid? sourceTaskId = context.TaskItemId;
        if (sourceTaskId is null && context.FormResponseId is Guid frId)
        {
            var reference = await _db.FormResponses.AsNoTracking()
                .Where(r => r.Id == frId).Select(r => r.Reference).FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(reference))
            {
                sourceTaskId = await TaskReference.ResolveTaskIdAsync(_db, reference, cancellationToken);
            }
        }

        // Quien pidio: 1) mapeo "solicitante" (campo de cabecera, p.ej. "Nombre del solicitante" de
        // Usuarios del sistema); 2) quien envio el formulario (ExecutedByTenantUserId); 3) el creador de la
        // actividad origen (formularios que se guardan en borrador no tienen submitter). Si el mapeo trae un
        // texto que no es un usuario, se guarda como nombre (snapshot) sin usuario.
        var (solicitanteUserId, solicitanteNombre) = await ResolveSolicitanteAsync(
            mapeo, context, sourceTaskId, cancellationToken);

        // Origenes del tenant (solo si el mapeo pide origen por fila): se aceptan id o nombre.
        List<OrigenRef>? origenes = null;
        if (mapeo.ContainsKey("origen"))
        {
            origenes = await _db.RequerimientoOrigenes.AsNoTracking()
                .Select(o => new OrigenRef(o.Id, o.Nombre, o.Area, o.Activo))
                .ToListAsync(cancellationToken);
        }
        Dictionary<Guid, string>? areaPorOrigen = null;

        // Idempotencia (evita duplicar al re-ejecutar la regla / doble clic del boton): se omite una fila si
        // ya existe una necesidad de la MISMA actividad origen con el mismo producto+unidad. Tambien se
        // deduplican filas identicas dentro de la misma corrida. La clave es insensible a mayusculas/espacios.
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sourceTaskId is Guid stk)
        {
            var prev = await _db.RequerimientosCompra.AsNoTracking()
                .Where(x => x.SourceTaskItemId == stk)
                .Select(x => new { x.Descripcion, x.UnidadMedida })
                .ToListAsync(cancellationToken);
            foreach (var p in prev) { existingKeys.Add(DedupKey(p.Descripcion, p.UnidadMedida)); }
        }
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var skipped = 0;
        var duplicadas = 0;
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object) { skipped++; continue; }

            // Valores base desde columnas (compatibilidad).
            var producto = Cell(row, productoCol);
            var cantidad = TryNum(Cell(row, cantidadCol), out var cBase) ? cBase : 0m;
            var unidad = Cell(row, unidadCol) ?? "";
            var obs = Cell(row, obsCol);
            DateTimeOffset? fecha = TryDate(Cell(row, fechaCol), out var dBase) ? new DateTimeOffset(dBase.Date, TimeSpan.Zero) : null;
            var area = areaHeader;
            var origenId = origenParam;
            string? descripcionOverride = null;
            string? observaciones = null;
            string? obsDespacho = null;
            var dynValues = new Dictionary<Guid, string?>();

            // Overrides por mapeo.
            foreach (var (target, expr) in mapeo)
            {
                var val = ResolveSource(expr, row, context);
                switch (target)
                {
                    case "descripcion": descripcionOverride = val; break;
                    case "cantidad": if (TryNum(val, out var cm)) { cantidad = cm; } break;
                    case "unidad": unidad = val ?? ""; break;
                    case "fecha": if (TryDate(val, out var dm)) { fecha = new DateTimeOffset(dm.Date, TimeSpan.Zero); } break;
                    case "area": area = val ?? ""; break;
                    case "observaciones": observaciones = val; break;
                    case "observaciones_despacho": obsDespacho = val; break;
                    case "origen":
                        if (MatchOrigen(val, origenes) is OrigenRef om) { origenId = om.Id; }
                        break;
                    case "solicitante": break; // ya resuelto una vez para todas las filas
                    default:
                        if (target.StartsWith("dyn:", StringComparison.Ordinal) && dynDefs is not null
                            && dynDefs.TryGetValue(target, out var defId))
                        {
                            dynValues[defId] = string.IsNullOrWhiteSpace(val) ? null : val!.Trim();
                        }
                        break;
                }
            }

            // Area: si nadie la dio (cabecera/mapeo), la del origen elegido.
            if (string.IsNullOrWhiteSpace(area) && origenId is Guid oa)
            {
                areaPorOrigen ??= origenes?.ToDictionary(o => o.Id, o => o.Area)
                    ?? await _db.RequerimientoOrigenes.AsNoTracking()
                        .ToDictionaryAsync(o => o.Id, o => o.Area, cancellationToken);
                area = areaPorOrigen.GetValueOrDefault(oa) ?? "";
            }

            // Unidad por defecto (del catalogo comun) cuando la fila no trajo ninguna.
            if (string.IsNullOrWhiteSpace(unidad) && !string.IsNullOrWhiteSpace(unidadFija)) { unidad = unidadFija!; }

            // Descripcion final (override o producto), con las observaciones entre parentesis.
            var baseDesc = descripcionOverride ?? producto;
            if (string.IsNullOrWhiteSpace(baseDesc)) { skipped++; continue; }
            var descripcion = string.IsNullOrWhiteSpace(obs) ? baseDesc!.Trim() : $"{baseDesc!.Trim()} ({obs!.Trim()})";
            if (cantidad <= 0) { skipped++; continue; }

            // Omite si ya existe (ejecucion previa) o si se repite dentro de esta misma corrida.
            var key = DedupKey(descripcion, unidad);
            if (existingKeys.Contains(key) || !seenKeys.Add(key)) { duplicadas++; continue; }

            try
            {
                var saved = await _req.CrearAsync(
                    new CrearRequerimientoRequest(origenId, area, null, descripcion, cantidad, unidad, fecha,
                        SolicitanteUserId: solicitanteUserId, SolicitanteNombre: solicitanteNombre,
                        SourceTaskItemId: sourceTaskId,
                        Observaciones: observaciones, ObservacionesDespacho: obsDespacho),
                    cancellationToken);
                if (saved is null) { return new RuleVerbResult(false, $"No se pudo crear la necesidad '{descripcion}'.", created); }
                // Campos propios de la necesidad creada.
                foreach (var (defId, v) in dynValues)
                {
                    try { await _fields.SetValueAsync(saved.Id, defId, v, cancellationToken); } catch { }
                }
                created++;
            }
            catch (Exception ex)
            {
                return new RuleVerbResult(false, $"Error al crear la necesidad '{descripcion}': {ex.Message}", created);
            }
        }

        var msg = $"{created} necesidad(es) de compra creada(s)."
            + (duplicadas > 0 ? $" {duplicadas} ya existia(n) (no se duplicaron)." : "")
            + (skipped > 0 ? $" {skipped} fila(s) omitida(s) (sin producto o cantidad)." : "");
        return RuleVerbResult.Ok(msg, created, autoCompleteStep: context.GetBoolParam("autoComplete"));
    }

    internal sealed record OrigenRef(Guid Id, string Nombre, string Area, bool Activo);

    internal sealed record UserRef(Guid Id, string? DisplayName, string Email);

    /// <summary>Busca el origen por id o por nombre (sin mayusculas ni espacios de mas; prefiere activos).
    /// Si ningun nombre coincide, prueba por area. Null si no hay valor o no coincide.</summary>
    internal static OrigenRef? MatchOrigen(string? value, IReadOnlyList<OrigenRef>? origenes)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || origenes is null || origenes.Count == 0) { return null; }
        if (Guid.TryParse(v, out var id)) { return origenes.FirstOrDefault(o => o.Id == id); }
        var n = Norm(v);
        return origenes.Where(o => Norm(o.Nombre) == n).OrderByDescending(o => o.Activo).FirstOrDefault()
            ?? origenes.Where(o => Norm(o.Area) == n).OrderByDescending(o => o.Activo).FirstOrDefault();
    }

    /// <summary>Busca el usuario del tenant por id, correo o nombre visible (mismo nombre que muestra el
    /// campo "Usuarios del sistema"). Null si no coincide ninguno o el nombre es ambiguo.</summary>
    internal static Guid? MatchUser(string? value, IReadOnlyList<UserRef> users)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || users.Count == 0) { return null; }
        if (Guid.TryParse(v, out var id)) { return users.Any(u => u.Id == id) ? id : null; }
        var byEmail = users.FirstOrDefault(u => string.Equals(u.Email?.Trim(), v, StringComparison.OrdinalIgnoreCase));
        if (byEmail is not null) { return byEmail.Id; }
        var n = Norm(v);
        var byName = users.Where(u => Norm(Forms.Lookups.SystemUserLookupSource.UserLabel(u.DisplayName, u.Email)) == n).ToList();
        return byName.Count == 1 ? byName[0].Id : null;
    }

    private static string Norm(string? s) => string.Join(' ', (s ?? "").Trim().ToLowerInvariant()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private async Task<(Guid? UserId, string? Nombre)> ResolveSolicitanteAsync(
        Dictionary<string, string> mapeo, RuleContext context, Guid? sourceTaskId, CancellationToken ct)
    {
        if (mapeo.TryGetValue("solicitante", out var expr))
        {
            var raw = ResolveSource(expr, default, context)?.Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                var users = await _db.TenantUsers.AsNoTracking()
                    .Join(_db.PlatformUsers.AsNoTracking().IgnoreQueryFilters(), tu => tu.PlatformUserId, pu => pu.Id,
                        (tu, pu) => new UserRef(tu.Id, pu.DisplayName, tu.Email))
                    .ToListAsync(ct);
                if (MatchUser(raw, users) is Guid uid) { return (uid, null); }
                // Texto libre que no es usuario: se conserva como nombre (si no parece un id).
                if (!Guid.TryParse(raw, out _)) { return (null, raw); }
            }
        }
        if (context.ExecutedByTenantUserId is Guid submitter) { return (submitter, null); }
        if (sourceTaskId is Guid tid)
        {
            // CreatedBy de la actividad es el PlatformUser; se traduce al TenantUser de este tenant.
            var creator = await _db.TaskItems.AsNoTracking().Where(t => t.Id == tid)
                .Select(t => t.CreatedBy).FirstOrDefaultAsync(ct);
            if (creator is Guid pu)
            {
                var tu = await _db.TenantUsers.AsNoTracking().Where(u => u.PlatformUserId == pu)
                    .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
                if (tu is not null) { return (tu, null); }
            }
        }
        return (null, null);
    }

    /// <summary>Clave de deduplicacion: producto + unidad, insensible a mayusculas y espacios colapsados.</summary>
    private static string DedupKey(string? descripcion, string? unidad)
    {
        static string N(string? s) => string.Join(' ', (s ?? "").Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return N(descripcion) + "|" + N(unidad);
    }

    /// <summary>Lee el parametro 'mapeo' (objeto JSON destino -> "kind:ref"); ignora valores vacios.</summary>
    private static Dictionary<string, string> ReadMapeo(RuleContext context)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (context.Params.TryGetValue("mapeo", out var el) && el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                var v = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
                if (!string.IsNullOrWhiteSpace(v)) { map[prop.Name] = v!; }
            }
        }
        return map;
    }

    /// <summary>Resuelve una fuente "kind:ref": col=&gt;columna del grid (por fila), field=&gt;campo de cabecera,
    /// fixed=&gt;literal. Sin prefijo reconocido se toma como literal.</summary>
    private static string? ResolveSource(string expr, JsonElement row, RuleContext context)
    {
        var i = expr.IndexOf(':');
        if (i < 0) { return expr; }
        var kind = expr[..i];
        var reference = expr[(i + 1)..];
        return kind switch
        {
            "col" => row.ValueKind == JsonValueKind.Object ? Cell(row, reference) : null,
            "field" => context.FormData.GetValueOrDefault(reference),
            "fixed" => reference,
            _ => expr,
        };
    }

    /// <summary>Filas desde el campo tabla del FormData (arreglo JSON) o, en su defecto, params.rows.</summary>
    private static List<JsonElement> ResolveRows(RuleContext context, string tableField, out string? error)
    {
        error = null;
        var raw = context.FormData.GetValueOrDefault(tableField);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Fallback opcional a filas fijas en params (util para pruebas).
            if (context.Params.TryGetValue("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
            {
                return rowsElement.EnumerateArray().Select(e => e.Clone()).ToList();
            }
            return [];
        }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = $"El campo tabla '{tableField}' no contiene un arreglo JSON.";
                return [];
            }
            return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        catch (JsonException)
        {
            error = $"El campo tabla '{tableField}' no contiene JSON valido.";
            return [];
        }
    }

    /// <summary>Lee una celda de la fila (los valores del grid se guardan como string).</summary>
    private static string? Cell(JsonElement row, string col)
    {
        if (!row.TryGetProperty(col, out var e)) { return null; }
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static bool TryNum(string? s, out decimal n)
    {
        s = (s ?? "").Trim().Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out n);
    }

    private static bool TryDate(string? s, out DateTime d)
    {
        d = default;
        if (string.IsNullOrWhiteSpace(s)) { return false; }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
            || DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out d);
    }
}
