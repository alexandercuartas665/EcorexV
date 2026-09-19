using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>
/// Cierre por evento de un registro transaccional (Ola 6/A3, decision del usuario: "si se agrega una firma eso
/// produce el cierre"). El JSON vive en <c>FormDefinition.CloseRuleJson</c> con la forma
/// { "field":"firma", "op":"notEmpty|equals|notEquals|includes|empty", "value":"..." } — la MISMA de la
/// visibilidad condicional. Cuando la condicion se cumple al guardar, el servicio PROMUEVE el guardado a envio
/// (confirma y cierra el registro), aunque el cliente lo mandara como borrador. Puro; reusa
/// <see cref="FormVisibilityEvaluator.Test"/>. Sin JSON / invalido / sin campo =&gt; NO cierra (fail-open al borrador).
/// </summary>
public static class FormCloseRule
{
    /// <summary>True si la condicion de cierre se cumple con los valores actuales. Null/vacio =&gt; false.</summary>
    public static bool IsMet(string? closeRuleJson, Func<string, string?> getValue)
    {
        if (string.IsNullOrWhiteSpace(closeRuleJson)) { return false; }
        try
        {
            using var doc = JsonDocument.Parse(closeRuleJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return false; }
            var field = root.TryGetProperty("field", out var pf) ? pf.GetString() : null;
            if (string.IsNullOrWhiteSpace(field)) { return false; }
            var op = (root.TryGetProperty("op", out var po) ? po.GetString() : null) ?? "notEmpty";
            var value = root.TryGetProperty("value", out var pv) ? pv.GetString() : null;
            return FormVisibilityEvaluator.Test(field, op, value, getValue);
        }
        catch (JsonException) { return false; }
    }
}
