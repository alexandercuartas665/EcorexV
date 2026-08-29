using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>
/// Evaluador PURO de la visibilidad condicional por VALOR de otra pregunta (config-driven, motor de
/// formularios). El JSON vive en <c>FormQuestion.VisibleWhenJson</c> / <c>FormContainer.VisibleWhenJson</c>
/// y tiene la forma { "field":"area", "op":"equals|notEquals|includes|empty|notEmpty", "value":"otro" }.
/// Lo comparte el renderer (evaluacion en vivo contra los valores del formulario) y cualquier consumidor
/// que necesite la misma semantica. Regla de seguridad: ante JSON ausente o invalido, VISIBLE (no ocultar
/// por error de configuracion). El campo "field" referencia el FieldCode de OTRA pregunta.
/// </summary>
public static class FormVisibilityEvaluator
{
    /// <summary>
    /// True si el campo/seccion debe MOSTRARSE. <paramref name="getValue"/> resuelve el valor actual de una
    /// pregunta por su FieldCode (null si no tiene valor). JSON vacio/invalido =&gt; visible.
    /// </summary>
    public static bool IsVisible(string? visibleWhenJson, Func<string, string?> getValue)
    {
        if (string.IsNullOrWhiteSpace(visibleWhenJson)) { return true; }
        try
        {
            using var doc = JsonDocument.Parse(visibleWhenJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return true; }
            var field = root.TryGetProperty("field", out var pf) ? pf.GetString() : null;
            if (string.IsNullOrWhiteSpace(field)) { return true; }
            var op = (root.TryGetProperty("op", out var po) ? po.GetString() : null) ?? "equals";
            var value = root.TryGetProperty("value", out var pv) ? pv.GetString() : null;
            var actual = getValue(field!);
            return Eval(op, actual, value);
        }
        catch (JsonException) { return true; }
    }

    private static bool Eval(string op, string? actual, string? value)
    {
        switch ((op ?? "equals").Trim().ToLowerInvariant())
        {
            case "notempty": return !string.IsNullOrWhiteSpace(actual);
            case "empty": return string.IsNullOrWhiteSpace(actual);
            case "includes": return Includes(actual, value);
            case "notequals": return !StrEquals(actual, value);
            case "equals":
            default: return StrEquals(actual, value);
        }
    }

    private static bool StrEquals(string? a, string? b)
        => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>Para MultiCheck: el valor guardado es un arreglo JSON (["leads","opp"]). Tambien admite lista
    /// separada por comas. True si alguno de los seleccionados coincide con <paramref name="value"/>.</summary>
    private static bool Includes(string? actual, string? value)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(value)) { return false; }
        foreach (var item in ParseMulti(actual))
        {
            if (StrEquals(item, value)) { return true; }
        }
        // Ultimo recurso: subcadena (texto libre que "incluye" el valor).
        return actual.Contains(value, StringComparison.OrdinalIgnoreCase) && ParseMulti(actual).Count == 0;
    }

    private static List<string> ParseMulti(string raw)
    {
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText();
                        if (!string.IsNullOrWhiteSpace(s)) { list.Add(s!); }
                    }
                    return list;
                }
            }
            catch (JsonException) { /* cae al split por comas */ }
        }
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
