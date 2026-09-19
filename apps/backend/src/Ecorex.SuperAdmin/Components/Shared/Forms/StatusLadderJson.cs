using System.Text.Json.Nodes;

namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>
/// Lee / arma el JSON del escalon de estados calculados de una definicion (Ola 3 del FormBuilder). Modelo que
/// consume el motor (FormStatusLadder.Resolve): estados ORDENADOS de menor a mayor, se elige el de mayor indice
/// cuyas condiciones (AND) se cumplen; el primero suele ir con when:[] = piso. Solo avanza. Las condiciones
/// reusan la semantica de "Mostrar solo si" (FormVisibilityEvaluator: equals/notEquals/includes/empty/notEmpty)
/// y ademas hasChildren / childCount(min) sobre campos Subform (ADR-0085).
/// <code>
/// {"field":"estado_lead","states":[
///   {"label":"Inicial","when":[]},
///   {"label":"Perfilado","when":[{"field":"bant_1","op":"equals","value":"true"}]},
///   {"label":"Prospectado","when":[{"field":"oportunidad","op":"hasChildren"}]},
///   {"label":"Cerrado","when":[{"field":"oportunidad","op":"childCount","min":1},{"field":"cerrado","op":"equals","value":"true"}]}]}
/// </code>
/// </summary>
public static class StatusLadderJson
{
    /// <summary>Una condicion del estado. Para childCount se usa Min (umbral); los demas ops usan Value.</summary>
    public sealed record Cond(string Field, string Op, string? Value, int? Min);

    /// <summary>Un estado del escalon: etiqueta + condiciones en AND (vacio = siempre se alcanza = piso).</summary>
    public sealed record State(string Label, List<Cond> When);

    /// <summary>Escalon completo: campo destino (donde se escribe la etiqueta) + estados de menor a mayor.</summary>
    public sealed record Ladder(string Field, List<State> States);

    /// <summary>Lee el escalon actual o null si no hay JSON / no es valido / no tiene campo destino.</summary>
    public static Ladder? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        JsonObject? root;
        try { root = JsonNode.Parse(json) as JsonObject; } catch { return null; }
        if (root is null) { return null; }
        var field = Str(root, "field");
        if (string.IsNullOrWhiteSpace(field)) { return null; }

        var states = new List<State>();
        if (root["states"] is JsonArray sa)
        {
            foreach (var sn in sa)
            {
                if (sn is not JsonObject so) { continue; }
                var label = Str(so, "label");
                if (string.IsNullOrWhiteSpace(label)) { continue; }
                var conds = new List<Cond>();
                if (so["when"] is JsonArray wa)
                {
                    foreach (var cn in wa)
                    {
                        if (cn is not JsonObject co) { continue; }
                        var cf = Str(co, "field");
                        if (string.IsNullOrWhiteSpace(cf)) { continue; }
                        var op = Str(co, "op") ?? "equals";
                        var val = Str(co, "value");
                        int? min = null;
                        if (co.TryGetPropertyValue("min", out var mn) && mn is not null && mn.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                        {
                            try { min = mn.GetValue<int>(); } catch { min = null; }
                        }
                        conds.Add(new Cond(cf!.Trim(), string.IsNullOrWhiteSpace(op) ? "equals" : op.Trim(), val, min));
                    }
                }
                states.Add(new State(label!.Trim(), conds));
            }
        }
        return new Ladder(field!.Trim(), states);
    }

    /// <summary>Arma el JSON del escalon. Devuelve null (sin escalon) si no hay campo destino o no hay estados
    /// con etiqueta. Estados y condiciones se escriben en el orden recibido (menor a mayor).</summary>
    public static string? Build(string? field, IReadOnlyList<State> states)
    {
        if (string.IsNullOrWhiteSpace(field)) { return null; }
        var arr = new JsonArray();
        foreach (var st in states)
        {
            if (string.IsNullOrWhiteSpace(st.Label)) { continue; }   // un estado sin etiqueta no sirve
            var whenArr = new JsonArray();
            foreach (var c in st.When)
            {
                if (string.IsNullOrWhiteSpace(c.Field)) { continue; }
                var op = string.IsNullOrWhiteSpace(c.Op) ? "equals" : c.Op.Trim();
                var opNorm = op.ToLowerInvariant();
                var co = new JsonObject { ["field"] = c.Field.Trim(), ["op"] = op };
                if (opNorm == "childcount")
                {
                    co["min"] = c.Min is > 0 ? c.Min.Value : 1;
                }
                else if (opNorm is not ("haschildren" or "empty" or "notempty") && !string.IsNullOrEmpty(c.Value))
                {
                    co["value"] = c.Value;
                }
                whenArr.Add(co);
            }
            arr.Add(new JsonObject { ["label"] = st.Label.Trim(), ["when"] = whenArr });
        }
        if (arr.Count == 0) { return null; }
        return new JsonObject { ["field"] = field.Trim(), ["states"] = arr }.ToJsonString();
    }

    private static string? Str(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return null; }
        try { return v.GetValue<string?>(); } catch { return v.ToString(); }
    }
}
