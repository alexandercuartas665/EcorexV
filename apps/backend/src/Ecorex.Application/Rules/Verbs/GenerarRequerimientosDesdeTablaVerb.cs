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
/// (areaField) y el origen (que define el proceso de compra/cotizacion) de 'origenId'.
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
                    new RuleParamOption("origen", "Origen (sobrescribe)")
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

        // Mapeo avanzado destino -> "kind:ref" (col|field|fixed). Sobrescribe/añade datos del destino.
        var mapeo = ReadMapeo(context);
        // Resuelve los campos propios (Padre) por clave solo si el mapeo los usa.
        Dictionary<string, Guid>? dynDefs = null;
        if (mapeo.Keys.Any(k => k.StartsWith("dyn:", StringComparison.Ordinal)))
        {
            dynDefs = (await _fields.ListDefsAsync("Padre", cancellationToken))
                .ToDictionary(d => "dyn:" + d.FieldKey, d => d.Id, StringComparer.Ordinal);
        }

        // Trazabilidad: quien pidio (el que envio el formulario) y desde que actividad.
        // En on-submit el contexto trae ExecutedByTenantUserId (= submitter) y FormResponseId;
        // la actividad origen se resuelve por el Reference (= numero de tarea) de la respuesta.
        var solicitanteUserId = context.ExecutedByTenantUserId;
        Guid? sourceTaskId = context.TaskItemId;
        if (sourceTaskId is null && context.FormResponseId is Guid frId)
        {
            var reference = await _db.FormResponses.AsNoTracking()
                .Where(r => r.Id == frId).Select(r => r.Reference).FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(reference))
            {
                sourceTaskId = await _db.TaskItems.AsNoTracking()
                    .Where(t => t.Number == reference).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
            }
        }

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
                    case "origen": if (Guid.TryParse(val, out var og)) { origenId = og; } break;
                    default:
                        if (target.StartsWith("dyn:", StringComparison.Ordinal) && dynDefs is not null
                            && dynDefs.TryGetValue(target, out var defId))
                        {
                            dynValues[defId] = string.IsNullOrWhiteSpace(val) ? null : val!.Trim();
                        }
                        break;
                }
            }

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
                        SolicitanteUserId: solicitanteUserId, SourceTaskItemId: sourceTaskId,
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
            "col" => Cell(row, reference),
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
