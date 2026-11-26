using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ecorex.Application.Forms;

/// <summary>
/// Reescritura PURA de referencias a un FieldCode cuando se renombra el "nombre interno" de un campo.
/// Cada funcion conoce el formato exacto de una columna (inventario 2026-11-26) y reescribe SOLO las
/// posiciones que son codigo de campo; las que podrian coincidir por casualidad (ids de columna de grilla,
/// claves de la fuente en un autollenado, columnas de un filtro fijo) se dejan intactas. Devuelve el mismo
/// texto (misma instancia) cuando no hay cambios, para no reformatear JSON que no se toca.
/// </summary>
public static class FormFieldCodeRename
{
    // Sin escapar acentos ni HTML: el JSON reescrito queda legible igual que el original.
    private static readonly JsonSerializerOptions Out = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Parametros de verbos cuyo valor (string) es un codigo de campo.</summary>
    public static readonly IReadOnlySet<string> RuleCodeParams = new HashSet<string>(StringComparer.Ordinal)
    {
        "targetField", "sourceField", "sourceGrid", "targetGrid", "titleField", "requesterNameField",
        "requesterEmailField", "requesterPhoneField", "marcaCreadaField", "tableField", "areaField",
        "emptyFieldCode", "assigneeFieldCode", "gridField",
    };

    /// <summary>Parametros plantilla con tokens {codigo}.</summary>
    private static readonly HashSet<string> RuleTemplateParams = new(StringComparer.Ordinal)
    {
        "titleTemplate", "descriptionTemplate",
    };

    // ---- Expresiones / texto ----

    /// <summary>CalcExpression de un campo y plantillas de reglas: {old} y {#old}.</summary>
    public static string? FieldTokens(string? text, string oldCode, string newCode)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(oldCode, StringComparison.Ordinal)) { return text; }
        var re = new Regex(@"\{(#?)\s*" + Regex.Escape(oldCode) + @"\s*\}", RegexOptions.CultureInvariant);
        return re.Replace(text, m => "{" + m.Groups[1].Value + newCode + "}");
    }

    /// <summary>Texto dentro de la config de grilla/canvas: SOLO {#old} (encabezado) y campo.old
    /// ({{campo.old}}, barcode:campo.old, qr:campo.old). {old} sin # es id de columna: no se toca.</summary>
    public static string HeaderAndTemplateTokens(string text, string oldCode, string newCode)
    {
        if (!text.Contains(oldCode, StringComparison.Ordinal)) { return text; }
        var esc = Regex.Escape(oldCode);
        var s = Regex.Replace(text, @"\{#\s*" + esc + @"\s*\}", "{#" + newCode + "}", RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"(?<=campo\.)" + esc + @"(?![A-Za-z0-9_-])", newCode, RegexOptions.CultureInvariant);
        return s;
    }

    /// <summary>CSS personalizado del formulario: selector .field-old (con limite: no toca .field-old_iva).</summary>
    public static string? Css(string? css, string oldCode, string newCode)
    {
        if (string.IsNullOrEmpty(css) || !css.Contains(oldCode, StringComparison.Ordinal)) { return css; }
        return Regex.Replace(css, @"\.field-" + Regex.Escape(oldCode) + @"(?![A-Za-z0-9_-])", ".field-" + newCode,
            RegexOptions.CultureInvariant);
    }

    // ---- JSON por columna ----

    /// <summary>VisibleWhenJson, StatusLadderJson, KpisJson, CloseRuleJson: toda propiedad "field" == old.</summary>
    public static string? FieldProps(string? json, string oldCode, string newCode)
        => Rewrite(json, oldCode, root => Walk(root, (key, _) => key == "field", oldCode, newCode, null));

    /// <summary>Arreglo plano de codigos (ListColumnsJson, FilterFieldsJson, UniqueKeyFieldsJson).</summary>
    public static string? CodeArray(string? json, string oldCode, string newCode)
        => Rewrite(json, oldCode, root =>
        {
            if (root is not JsonArray arr) { return false; }
            var changed = false;
            for (var i = 0; i < arr.Count; i++)
            {
                if (IsStr(arr[i], oldCode)) { arr[i] = newCode; changed = true; }
            }
            return changed;
        });

    /// <summary>AutofillMapJson {campoFuente: codigoDestino}: solo los VALORES (la clave es de la fuente).</summary>
    public static string? AutofillValues(string? json, string oldCode, string newCode)
        => Rewrite(json, oldCode, root =>
        {
            if (root is not JsonObject obj) { return false; }
            var changed = false;
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                if (IsStr(obj[key], oldCode)) { obj[key] = newCode; changed = true; }
            }
            return changed;
        });

    /// <summary>FilterJson: solo cascade[].fromField (fixed y la forma plana son columnas del contenedor).</summary>
    public static string? FilterCascade(string? json, string oldCode, string newCode)
        => Rewrite(json, oldCode, root =>
            root is JsonObject o && o["cascade"] is JsonArray cascade
            && Walk(cascade, (key, _) => key == "fromField", oldCode, newCode, null));

    /// <summary>
    /// OptionsJson (grilla, lienzo, IA, boton de accion, canvas): rollup/grid/targetField/target_grid/lienzo,
    /// items de source/sources, ai.targets[].field; y en cualquier texto {#old} y campo.old.
    /// </summary>
    public static string? Options(string? json, string oldCode, string newCode)
        => Rewrite(json, oldCode, root => Walk(root,
            (key, parentArrayKey) => key is "rollup" or "grid" or "targetField" or "target_grid" or "lienzo"
                || (key == "field" && parentArrayKey == "targets"),
            oldCode, newCode,
            arrayKey => arrayKey is "source" or "sources",
            s => HeaderAndTemplateTokens(s, oldCode, newCode)));

    /// <summary>Params de una regla (Rule.ParamsJson u override del vinculo). Solo los parametros que son codigo
    /// en el formulario DUENO de la regla; los valores de CONVERTIR_A_FORMULARIO.mapping son del formulario
    /// DESTINO y no se tocan (sus CLAVES si, son del origen).</summary>
    public static string? RuleParams(string? json, string oldCode, string newCode)
        => Rewrite(json, oldCode, root =>
        {
            if (root is not JsonObject obj) { return false; }
            var changed = false;
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                var node = obj[key];
                if (RuleCodeParams.Contains(key) && IsStr(node, oldCode))
                {
                    obj[key] = newCode; changed = true;
                }
                else if (RuleTemplateParams.Contains(key) && node is JsonValue tv && tv.TryGetValue<string>(out var tpl))
                {
                    var r = FieldTokens(tpl, oldCode, newCode);
                    if (!ReferenceEquals(r, tpl) && r != tpl) { obj[key] = r; changed = true; }
                }
                else if (key == "requireFields")
                {
                    if (node is JsonArray rf)
                    {
                        for (var i = 0; i < rf.Count; i++)
                        {
                            if (IsStr(rf[i], oldCode)) { rf[i] = newCode; changed = true; }
                        }
                    }
                    else if (node is JsonValue rv && rv.TryGetValue<string>(out var list)
                        && list.Split(',').Any(p => p.Trim() == oldCode))
                    {
                        // Tambien se acepta "a,b,c" (string suelto o separado por comas).
                        obj[key] = string.Join(",", list.Split(',').Select(p => p.Trim() == oldCode ? newCode : p.Trim()));
                        changed = true;
                    }
                }
                else if (key is "mappings" or "mapping" or "mapeo")
                {
                    changed |= NestedJson(obj, key, inner => key switch
                    {
                        "mappings" => Walk(inner, (k, _) => k is "source" or "target", oldCode, newCode, null),
                        "mapping" => RenameKey(inner as JsonObject, oldCode, newCode),
                        _ => MapeoFieldValues(inner as JsonObject, oldCode, newCode),
                    });
                }
            }
            return changed;
        });

    /// <summary>FormResponse.Data: renombra la clave de primer nivel conservando el orden.</summary>
    public static string? DataKey(string? json, string oldCode, string newCode)
        => Rewrite(json, oldCode, root => RenameKey(root as JsonObject, oldCode, newCode));

    // ---- Helpers ----

    private static string? Rewrite(string? json, string oldCode, Func<JsonNode, bool> mutate)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.Contains(oldCode, StringComparison.Ordinal)) { return json; }
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return json; }
        if (root is null) { return json; }
        return mutate(root) ? root.ToJsonString(Out) : json;
    }

    private static bool IsStr(JsonNode? node, string value)
        => node is JsonValue v && v.TryGetValue<string>(out var s) && s == value;

    /// <summary>Recorre el arbol: reescribe strings bajo claves-codigo, items de arreglos-codigo y (opcional)
    /// cualquier string con <paramref name="anyString"/>.</summary>
    private static bool Walk(JsonNode node, Func<string, string?, bool> isCodeKey, string oldCode, string newCode,
        Func<string, bool>? isCodeArray, Func<string, string>? anyString = null, string? parentArrayKey = null)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    if (child is null) { continue; }
                    if (child is JsonValue && isCodeKey(key, parentArrayKey) && IsStr(child, oldCode))
                    {
                        obj[key] = newCode; changed = true;
                    }
                    else if (child is JsonArray arr && isCodeArray?.Invoke(key) == true)
                    {
                        for (var i = 0; i < arr.Count; i++)
                        {
                            if (IsStr(arr[i], oldCode)) { arr[i] = newCode; changed = true; }
                            else if (arr[i] is { } item and not JsonValue)
                            {
                                changed |= Walk(item, isCodeKey, oldCode, newCode, isCodeArray, anyString, key);
                            }
                        }
                    }
                    else if (child is JsonValue sv && anyString is not null && sv.TryGetValue<string>(out var s))
                    {
                        var r = anyString(s);
                        if (r != s) { obj[key] = r; changed = true; }
                    }
                    else if (child is JsonArray childArr)
                    {
                        changed |= WalkArray(childArr, key, isCodeKey, oldCode, newCode, isCodeArray, anyString);
                    }
                    else if (child is JsonObject)
                    {
                        changed |= Walk(child, isCodeKey, oldCode, newCode, isCodeArray, anyString, null);
                    }
                }
                break;
            case JsonArray rootArr:
                changed |= WalkArray(rootArr, parentArrayKey, isCodeKey, oldCode, newCode, isCodeArray, anyString);
                break;
        }
        return changed;
    }

    private static bool WalkArray(JsonArray arr, string? arrayKey, Func<string, string?, bool> isCodeKey, string oldCode,
        string newCode, Func<string, bool>? isCodeArray, Func<string, string>? anyString)
    {
        var changed = false;
        for (var i = 0; i < arr.Count; i++)
        {
            var item = arr[i];
            if (item is JsonValue sv)
            {
                if (anyString is not null && sv.TryGetValue<string>(out var s))
                {
                    var r = anyString(s);
                    if (r != s) { arr[i] = r; changed = true; }
                }
            }
            else if (item is not null)
            {
                changed |= Walk(item, isCodeKey, oldCode, newCode, isCodeArray, anyString, arrayKey);
            }
        }
        return changed;
    }

    private static bool RenameKey(JsonObject? obj, string oldCode, string newCode)
    {
        if (obj is null || !obj.ContainsKey(oldCode) || obj.ContainsKey(newCode)) { return false; }
        // Reconstruye en orden para que la clave renombrada quede en su posicion original.
        var entries = obj.Select(p => (p.Key, p.Value)).ToList();
        obj.Clear();
        foreach (var (k, v) in entries)
        {
            obj[k == oldCode ? newCode : k] = v;
        }
        return true;
    }

    /// <summary>GENERAR_REQUERIMIENTOS mapeo {destino:"field:codigo"|"col:x"|"fixed:x"}: solo "field:".</summary>
    private static bool MapeoFieldValues(JsonObject? obj, string oldCode, string newCode)
    {
        if (obj is null) { return false; }
        var changed = false;
        foreach (var key in obj.Select(p => p.Key).ToList())
        {
            if (IsStr(obj[key], "field:" + oldCode)) { obj[key] = "field:" + newCode; changed = true; }
        }
        return changed;
    }

    /// <summary>Un parametro que es JSON puede venir como valor JSON o como string con JSON dentro.</summary>
    private static bool NestedJson(JsonObject owner, string key, Func<JsonNode, bool> mutate)
    {
        var node = owner[key];
        if (node is JsonObject or JsonArray) { return mutate(node); }
        if (node is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            JsonNode? inner;
            try { inner = JsonNode.Parse(text); }
            catch (JsonException) { return false; }
            if (inner is not null && mutate(inner)) { owner[key] = inner.ToJsonString(Out); return true; }
        }
        return false;
    }
}
