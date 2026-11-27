using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Cascada en columnas de GRILLA: el filtro del lookup de una columna puede referirse a otras columnas de
/// la MISMA FILA con tokens <c>{columna}</c>. Ej.: la columna OT con
/// <c>"filter":{"cliente":"{proyecto_cliente}"}</c> lista solo las actividades del cliente elegido en esa
/// fila. El renderer sustituye los tokens con los valores de la fila justo antes de buscar.
/// Un token cuya columna esta vacia o no existe queda como texto vacio (la fuente decide: p.ej. Actividades
/// devuelve la lista vacia hasta que se elija el cliente). Solo se tocan valores string; puro y sin BD.
/// </summary>
public static partial class FormGridFilterTokens
{
    [GeneratedRegex(@"\{([A-Za-z0-9_\-\.]+)\}")]
    private static partial Regex TokenRx();

    /// <summary>Devuelve el filtro con los tokens sustituidos. Sin tokens (o JSON invalido) devuelve el original.</summary>
    public static string? Resolve(string? filterJson, IReadOnlyDictionary<string, string?> row)
    {
        if (string.IsNullOrWhiteSpace(filterJson) || !filterJson.Contains('{', StringComparison.Ordinal)) { return filterJson; }
        JsonNode? root;
        try { root = JsonNode.Parse(filterJson); }
        catch (JsonException) { return filterJson; }
        if (root is not JsonObject obj) { return filterJson; }

        var changed = Replace(obj, row);
        return changed ? obj.ToJsonString() : filterJson;
    }

    private static bool Replace(JsonObject obj, IReadOnlyDictionary<string, string?> row)
    {
        var changed = false;
        foreach (var key in obj.Select(p => p.Key).ToList())
        {
            switch (obj[key])
            {
                case JsonObject child:
                    changed |= Replace(child, row);
                    break;
                case JsonValue val when val.TryGetValue<string>(out var s) && TokenRx().IsMatch(s):
                    obj[key] = TokenRx().Replace(s, m =>
                        row.TryGetValue(m.Groups[1].Value, out var v) ? (v ?? string.Empty).Trim() : string.Empty);
                    changed = true;
                    break;
            }
        }
        return changed;
    }
}
