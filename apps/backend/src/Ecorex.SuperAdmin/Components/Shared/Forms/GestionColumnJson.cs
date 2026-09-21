using System.Text.Json.Nodes;

namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>
/// Manipula SOLO la columna de gestiones (type="gestion") dentro del options_json (arreglo de columnas) de un
/// GridDetail, PRESERVANDO las demas columnas y claves. Modelo que consume el motor (ADR-0085,
/// FormGridCalculator.ParseColumns): {"id":"gestion","type":"gestion","label":..,"pills":[{label,def,color}]}.
/// </summary>
public static class GestionColumnJson
{
    public sealed record Pill(string Label, string Def, string? Color, bool Hidden = false);

    /// <summary>Lee la columna de gestiones actual (label + pildoras) o null si no existe.</summary>
    public static (string Label, List<Pill> Pills)? Read(string? optionsJson)
    {
        foreach (var col in ParseArray(optionsJson))
        {
            if (col is not JsonObject o || !IsGestion(o)) { continue; }
            var pills = new List<Pill>();
            if (o["pills"] is JsonArray pa)
            {
                foreach (var pn in pa)
                {
                    if (pn is not JsonObject po) { continue; }
                    var def = Str(po, "def");
                    if (string.IsNullOrWhiteSpace(def)) { continue; }
                    var hidden = po.TryGetPropertyValue("hidden", out var hv) && hv is not null
                        && (hv.GetValueKind() == System.Text.Json.JsonValueKind.True
                            || string.Equals(hv.ToString(), "true", StringComparison.OrdinalIgnoreCase));
                    pills.Add(new Pill(Str(po, "label") ?? def!, def!.Trim(), Str(po, "color"), hidden));
                }
            }
            return (string.IsNullOrWhiteSpace(Str(o, "label")) ? "Gestiones" : Str(o, "label")!, pills);
        }
        return null;
    }

    /// <summary>Reescribe (creando si falta) la columna de gestiones con <paramref name="label"/> + pildoras,
    /// preservando las demas columnas; devuelve el options_json resultante. Las pildoras sin 'def' se omiten.</summary>
    public static string Upsert(string? optionsJson, string label, IReadOnlyList<Pill> pills)
    {
        var arr = ParseArray(optionsJson);
        JsonObject? gestion = null;
        foreach (var col in arr)
        {
            if (col is JsonObject o && IsGestion(o)) { gestion = o; break; }
        }
        if (gestion is null)
        {
            gestion = new JsonObject { ["id"] = "gestion", ["type"] = "gestion" };
            arr.Add(gestion);
        }
        gestion["label"] = string.IsNullOrWhiteSpace(label) ? "Gestiones" : label.Trim();

        var pillsArr = new JsonArray();
        foreach (var p in pills)
        {
            if (string.IsNullOrWhiteSpace(p.Def)) { continue; }   // una pildora sin formulario no sirve
            var po = new JsonObject
            {
                ["label"] = string.IsNullOrWhiteSpace(p.Label) ? p.Def.Trim() : p.Label.Trim(),
                ["def"] = p.Def.Trim()
            };
            if (!string.IsNullOrWhiteSpace(p.Color)) { po["color"] = p.Color; }
            if (p.Hidden) { po["hidden"] = true; }   // pildora oculta: se conserva en config pero no se renderiza
            pillsArr.Add(po);
        }
        gestion["pills"] = pillsArr;
        return arr.ToJsonString();
    }

    /// <summary>Elimina la columna de gestiones del options_json, preservando las demas.</summary>
    public static string Remove(string? optionsJson)
    {
        var arr = ParseArray(optionsJson);
        for (var i = arr.Count - 1; i >= 0; i--)
        {
            if (arr[i] is JsonObject o && IsGestion(o)) { arr.RemoveAt(i); }
        }
        return arr.ToJsonString();
    }

    private static bool IsGestion(JsonObject o)
        => string.Equals(Str(o, "type"), "gestion", StringComparison.OrdinalIgnoreCase);

    private static JsonArray ParseArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new JsonArray(); }
        try { return JsonNode.Parse(json) as JsonArray ?? new JsonArray(); }
        catch { return new JsonArray(); }
    }

    private static string? Str(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return null; }
        try { return v.GetValue<string?>(); } catch { return v.ToString(); }
    }
}
