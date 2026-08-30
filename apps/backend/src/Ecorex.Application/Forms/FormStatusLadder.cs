using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>
/// Escalon de ESTADOS calculados de un registro (P1#5, config-driven). El JSON vive en
/// <c>FormDefinition.StatusLadderJson</c>:
/// <code>
/// { "field": "estado_lead",
///   "states": [
///     { "label": "Inicial", "when": [] },
///     { "label": "Perfilado", "when": [ {"field":"bant_1","op":"equals","value":"true"}, ... ] },
///     { "label": "Prospectado", "when": [ ... ] },
///     { "label": "Cerrado", "when": [ ... ] } ] }
/// </code>
/// Estados ORDENADOS de menor a mayor. Se elige el estado de MAYOR indice cuyas condiciones (AND) se
/// cumplen; el indice 0 (con when vacio) es el piso. SOLO AVANZA: nunca baja del estado actual. Puro,
/// reutiliza <see cref="FormVisibilityEvaluator.Test"/> por condicion. JSON invalido = sin cambio.
/// </summary>
public static class FormStatusLadder
{
    public sealed record LadderResult(string TargetField, string Label);

    /// <summary>Devuelve el campo destino y la etiqueta de estado calculada. El estado ACTUAL se lee del
    /// propio campo destino via getValue (avance-only). Null si no hay escalon valido / sin campo destino.</summary>
    public static LadderResult? Resolve(string? ladderJson, Func<string, string?> getValue)
    {
        if (string.IsNullOrWhiteSpace(ladderJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(ladderJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }
            var field = root.TryGetProperty("field", out var pf) ? pf.GetString() : null;
            if (string.IsNullOrWhiteSpace(field)) { return null; }
            if (!root.TryGetProperty("states", out var statesEl) || statesEl.ValueKind != JsonValueKind.Array) { return null; }

            var labels = new List<string>();
            var reached = -1;
            var idx = 0;
            foreach (var st in statesEl.EnumerateArray())
            {
                if (st.ValueKind != JsonValueKind.Object) { idx++; continue; }
                var label = st.TryGetProperty("label", out var pl) ? pl.GetString() : null;
                if (string.IsNullOrWhiteSpace(label)) { idx++; continue; }
                labels.Add(label!);
                var whenOk = true;
                if (st.TryGetProperty("when", out var whenEl) && whenEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cond in whenEl.EnumerateArray())
                    {
                        if (cond.ValueKind != JsonValueKind.Object) { continue; }
                        var cf = cond.TryGetProperty("field", out var cfp) ? cfp.GetString() : null;
                        var co = cond.TryGetProperty("op", out var cop) ? cop.GetString() : "equals";
                        var cv = cond.TryGetProperty("value", out var cvp) ? cvp.GetString() : null;
                        if (!FormVisibilityEvaluator.Test(cf, co, cv, getValue)) { whenOk = false; break; }
                    }
                }
                if (whenOk) { reached = labels.Count - 1; }
                idx++;
            }
            if (labels.Count == 0) { return null; }
            if (reached < 0) { reached = 0; } // piso: primer estado

            // Avance-only: no bajar del estado actual (leido del propio campo destino) si estaba mas arriba.
            var currentLabel = getValue(field!);
            var currentIdx = string.IsNullOrWhiteSpace(currentLabel) ? -1
                : labels.FindIndex(l => string.Equals(l, currentLabel, StringComparison.OrdinalIgnoreCase));
            var finalIdx = Math.Max(reached, currentIdx);
            if (finalIdx < 0 || finalIdx >= labels.Count) { finalIdx = Math.Max(0, reached); }
            return new LadderResult(field!.Trim(), labels[finalIdx]);
        }
        catch (JsonException) { return null; }
    }
}
