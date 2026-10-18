using System.Text.Json;

namespace Ecorex.Application.Rules.Verbs;

/// <summary>
/// FEATURE C - Verbo CARGAR_FILAS_DESDE_GRILLA: inserta de un golpe, en una grilla DESTINO, una fila por cada fila
/// de otra grilla ORIGEN del MISMO formulario, copiando las columnas segun un mapeo. Caso de uso: boton "Cargar
/// todos los productos" en 'entregas' que agrega una fila por cada producto de 'items' con pendiente &gt; 0,
/// poniendo cantidad_entregada = pendiente (el usuario luego ajusta/borra). Es CONFIGURABLE (no hardcodeado al
/// tenant): grilla origen/destino, mapeo de columnas, filtro "solo &gt; 0" y dedup se declaran en los params de la
/// regla y se editan en el disenador. Devuelve una accion SetFieldValue con las filas nuevas (el renderer la aplica
/// y recalcula). Respeta FEATURE B: al poner cantidad_entregada = pendiente nunca supera el tope.
/// </summary>
public sealed class CargarFilasDesdeGrillaVerb : IRuleVerb
{
    public const string VerbName = "CARGAR_FILAS_DESDE_GRILLA";

    public string Name => VerbName;

    public RuleVerbDescriptor Descriptor { get; } = new(
        VerbName,
        "Cargar filas desde otra grilla",
        "Inserta en una grilla una fila por cada fila de otra grilla del formulario, copiando columnas segun un mapeo.",
        [
            new RuleVerbParamDescriptor("sourceGrid", "Grilla origen", RuleParamType.FieldCode, Required: true,
                "FieldCode de la grilla de la que se leen las filas (ej. items)."),
            new RuleVerbParamDescriptor("targetGrid", "Grilla destino", RuleParamType.FieldCode, Required: true,
                "FieldCode de la grilla a la que se agregan las filas (ej. entregas)."),
            new RuleVerbParamDescriptor("columnMap", "Mapeo de columnas (JSON)", RuleParamType.Json, Required: true,
                "Objeto {columnaDestino: columnaOrigen}. Ej: {\"item\":\"item\",\"cantidad_entregada\":\"pendiente\"}"),
            new RuleVerbParamDescriptor("onlyWherePositive", "Solo si esta columna > 0", RuleParamType.Text, Required: false,
                "Columna de la grilla ORIGEN que debe ser numerica y mayor que 0 para incluir la fila (ej. pendiente)."),
            new RuleVerbParamDescriptor("dedupeBy", "No duplicar por (columna destino)", RuleParamType.Text, Required: false,
                "Columna de la grilla DESTINO: si ya existe una fila con ese valor, no se vuelve a agregar (ej. item)."),
            new RuleVerbParamDescriptor("autoComplete", "Completar paso", RuleParamType.Boolean, Required: false,
                "Si es true y la regla es autonoma de un nodo, el paso del flujo se completa solo.")
        ]);

    public Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken)
    {
        var sourceGrid = context.GetStringParam("sourceGrid");
        var targetGrid = context.GetStringParam("targetGrid");
        if (string.IsNullOrWhiteSpace(sourceGrid) || string.IsNullOrWhiteSpace(targetGrid))
        {
            return Task.FromResult(RuleVerbResult.Fail("Parametros 'sourceGrid' y 'targetGrid' obligatorios (FieldCode de las grillas)."));
        }
        if (!context.Params.TryGetValue("columnMap", out var mapEl) || mapEl.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(RuleVerbResult.Fail("Parametro 'columnMap' obligatorio: objeto JSON {columnaDestino: columnaOrigen}."));
        }
        var columnMap = new List<(string Target, string Source)>();
        foreach (var p in mapEl.EnumerateObject())
        {
            var src = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(src)) { columnMap.Add((p.Name, src!)); }
        }
        if (columnMap.Count == 0)
        {
            return Task.FromResult(RuleVerbResult.Fail("'columnMap' no tiene mapeos {columnaDestino: columnaOrigen}."));
        }

        var onlyPos = context.GetStringParam("onlyWherePositive");
        var dedupeBy = context.GetStringParam("dedupeBy");
        var autoComplete = context.GetBoolParam("autoComplete");

        var sourceRows = ParseRows(context.FormData.GetValueOrDefault(sourceGrid));
        var targetRows = ParseRows(context.FormData.GetValueOrDefault(targetGrid));

        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(dedupeBy))
        {
            foreach (var tr in targetRows)
            {
                var k = tr.GetValueOrDefault(dedupeBy!);
                if (!string.IsNullOrWhiteSpace(k)) { existingKeys.Add(k!); }
            }
        }

        var added = 0;
        foreach (var sr in sourceRows)
        {
            if (!string.IsNullOrWhiteSpace(onlyPos))
            {
                if (!decimal.TryParse(sr.GetValueOrDefault(onlyPos!),
                        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pos)
                    || pos <= 0)
                {
                    continue;
                }
            }
            var row = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (target, source) in columnMap) { row[target] = sr.GetValueOrDefault(source); }

            if (!string.IsNullOrWhiteSpace(dedupeBy))
            {
                var k = row.GetValueOrDefault(dedupeBy!);
                if (string.IsNullOrWhiteSpace(k) || !existingKeys.Add(k!)) { continue; }
            }
            targetRows.Add(row);
            added++;
        }

        if (added == 0)
        {
            return Task.FromResult(RuleVerbResult.Ok("No hay filas para cargar (nada pendiente o ya estaban cargadas).", 0,
                new List<RuleAction>(), autoComplete));
        }

        var json = JsonSerializer.Serialize(targetRows);
        context.FormData[targetGrid] = json;   // los verbos encadenados ven las filas nuevas
        return Task.FromResult(RuleVerbResult.Ok(
            $"{added} fila(s) cargada(s) en '{targetGrid}'.", added,
            new List<RuleAction> { RuleAction.SetValue(targetGrid, json) }, autoComplete));
    }

    /// <summary>Parsea un valor de grilla (arreglo JSON de objetos) a filas. Vacio/invalido = lista vacia.</summary>
    private static List<Dictionary<string, string?>> ParseRows(string? gridValue)
    {
        var result = new List<Dictionary<string, string?>>();
        if (string.IsNullOrWhiteSpace(gridValue)) { return result; }
        try
        {
            using var doc = JsonDocument.Parse(gridValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return result; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var p in el.EnumerateObject())
                {
                    row[p.Name] = p.Value.ValueKind switch
                    {
                        JsonValueKind.String => p.Value.GetString(),
                        JsonValueKind.Number => p.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => null,
                        _ => p.Value.GetRawText()
                    };
                }
                result.Add(row);
            }
        }
        catch (JsonException) { /* grilla invalida: se trata como vacia */ }
        return result;
    }
}
