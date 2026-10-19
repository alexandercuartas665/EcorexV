using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Requerimientos;

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

    public GenerarRequerimientosDesdeTablaVerb(IRequerimientoService req)
    {
        _req = req;
    }

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Generar requerimientos desde tabla",
        "Crea una necesidad de compra por cada fila de un campo tabla del formulario (modulo Requerimientos y Compras).",
        [
            new RuleVerbParamDescriptor("tableField", "Campo tabla", RuleParamType.FieldCode, Required: true,
                "FieldCode del grid cuyo valor es un arreglo JSON de filas (cada fila = una necesidad)."),
            new RuleVerbParamDescriptor("origenId", "Origen (Id)", RuleParamType.Text, Required: false,
                "Guid del RequerimientoOrigen al que quedan asociadas las necesidades (define el proceso de compra/cotizacion). Vacio = sin origen."),
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

        Guid? origenId = Guid.TryParse(context.GetStringParam("origenId"), out var oid) ? oid : null;
        var areaField = context.GetStringParam("areaField");
        var area = !string.IsNullOrWhiteSpace(areaField)
            ? (context.FormData.GetValueOrDefault(areaField!) ?? "")
            : "";

        var productoCol = context.GetStringParam("productoCol") ?? "producto";
        var cantidadCol = context.GetStringParam("cantidadCol") ?? "cantidad";
        var unidadCol = context.GetStringParam("unidadCol") ?? "unidad";
        var fechaCol = context.GetStringParam("fechaCol") ?? "fecha_requerida";
        var obsCol = context.GetStringParam("obsCol") ?? "observaciones";

        var created = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object) { skipped++; continue; }
            var producto = Cell(row, productoCol);
            if (string.IsNullOrWhiteSpace(producto)) { skipped++; continue; }

            if (!TryNum(Cell(row, cantidadCol), out var cantidad) || cantidad <= 0) { skipped++; continue; }

            var unidad = Cell(row, unidadCol) ?? "";
            var obs = Cell(row, obsCol);
            var descripcion = string.IsNullOrWhiteSpace(obs) ? producto!.Trim() : $"{producto!.Trim()} ({obs!.Trim()})";
            DateTimeOffset? fecha = TryDate(Cell(row, fechaCol), out var d) ? new DateTimeOffset(d.Date, TimeSpan.Zero) : null;

            try
            {
                var saved = await _req.CrearAsync(
                    new CrearRequerimientoRequest(origenId, area, null, descripcion, cantidad, unidad, fecha),
                    cancellationToken);
                if (saved is null) { return new RuleVerbResult(false, $"No se pudo crear la necesidad '{descripcion}'.", created); }
                created++;
            }
            catch (Exception ex)
            {
                return new RuleVerbResult(false, $"Error al crear la necesidad '{descripcion}': {ex.Message}", created);
            }
        }

        var msg = $"{created} necesidad(es) de compra creada(s)." + (skipped > 0 ? $" {skipped} fila(s) omitida(s) (sin producto o cantidad)." : "");
        return RuleVerbResult.Ok(msg, created, autoCompleteStep: context.GetBoolParam("autoComplete"));
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
