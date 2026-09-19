using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ecorex.Application.Rules;

/// <summary>
/// Contrato PURO del params_json de una regla AL ENVIAR "crear actividad" (Ola 4 del FormBuilder). Las claves
/// son EXACTAMENTE las que lee <c>GenerarTareasDesdeTablaVerb</c> (camelCase): activityTypeId, assigneeUserId,
/// titlePrefix, autoComplete, y para el origen tableField+titleKey (una tarea por fila) o rows (una sola tarea,
/// primer elemento = titulo). Build arma el JSON; Parse lo vuelve a los campos del editor. Separado del servicio
/// para poder testear el contrato sin BD (mismo patron que StatusLadderJson / GestionColumnJson).
/// </summary>
public static class FormSubmitRuleParams
{
    /// <summary>Resumen parseado del params_json para repoblar el editor. TableFieldCode y FixedTitle son
    /// mutuamente excluyentes segun el origen.</summary>
    public sealed record Parsed(
        Guid? ActivityTypeId, Guid? AssigneeTenantUserId,
        string? TableFieldCode, string? TitleKey, string? FixedTitle, string? TitlePrefix, bool AutoComplete);

    /// <summary>Arma el params_json. Origen: <paramref name="tableFieldCode"/> (una tarea por fila) o, si va
    /// vacio, <paramref name="fixedTitle"/> (una sola tarea). No valida existencia (eso es del servicio, con BD).</summary>
    public static string Build(
        Guid activityTypeId, Guid? assigneeTenantUserId,
        string? tableFieldCode, string? titleKey, string? fixedTitle, string? titlePrefix, bool autoComplete)
    {
        var p = new JsonObject { ["activityTypeId"] = activityTypeId.ToString() };
        if (assigneeTenantUserId is Guid asg && asg != Guid.Empty)
        {
            p["assigneeUserId"] = asg.ToString();
        }
        if (!string.IsNullOrWhiteSpace(titlePrefix))
        {
            p["titlePrefix"] = titlePrefix!.Trim();
        }
        if (autoComplete)
        {
            p["autoComplete"] = true;
        }
        if (!string.IsNullOrWhiteSpace(tableFieldCode))
        {
            p["tableField"] = tableFieldCode!.Trim();
            if (!string.IsNullOrWhiteSpace(titleKey))
            {
                p["titleKey"] = titleKey!.Trim();
            }
        }
        else
        {
            // Una sola tarea: fila unica como string -> el verbo la toma como titulo.
            p["rows"] = new JsonArray((fixedTitle ?? string.Empty).Trim());
        }
        return p.ToJsonString();
    }

    /// <summary>Vuelve el params_json a los campos del editor. JSON ausente/ilegible => todo null/false.</summary>
    public static Parsed Parse(string? paramsJson)
    {
        Guid? actType = null, assignee = null;
        string? tableField = null, titleKey = null, fixedTitle = null, titlePrefix = null;
        var autoComplete = false;
        if (!string.IsNullOrWhiteSpace(paramsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(paramsJson);
                var r = doc.RootElement;
                if (r.ValueKind == JsonValueKind.Object)
                {
                    if (r.TryGetProperty("activityTypeId", out var at) && Guid.TryParse(at.GetString(), out var atg)) { actType = atg; }
                    if (r.TryGetProperty("assigneeUserId", out var au) && Guid.TryParse(au.GetString(), out var aug)) { assignee = aug; }
                    if (r.TryGetProperty("tableField", out var tf) && tf.ValueKind == JsonValueKind.String) { tableField = tf.GetString(); }
                    if (r.TryGetProperty("titleKey", out var tk) && tk.ValueKind == JsonValueKind.String) { titleKey = tk.GetString(); }
                    if (r.TryGetProperty("titlePrefix", out var tp) && tp.ValueKind == JsonValueKind.String) { titlePrefix = tp.GetString(); }
                    if (r.TryGetProperty("autoComplete", out var ac))
                    {
                        autoComplete = ac.ValueKind == JsonValueKind.True
                            || (ac.ValueKind == JsonValueKind.String && bool.TryParse(ac.GetString(), out var b) && b);
                    }
                    if (string.IsNullOrWhiteSpace(tableField)
                        && r.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in rowsEl.EnumerateArray())
                        {
                            if (row.ValueKind == JsonValueKind.String) { fixedTitle = row.GetString(); break; }
                            if (row.ValueKind == JsonValueKind.Object
                                && row.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                            {
                                fixedTitle = t.GetString();
                                break;
                            }
                        }
                    }
                }
            }
            catch (JsonException) { /* ilegible: se devuelve todo vacio */ }
        }
        return new Parsed(actType, assignee, tableField, titleKey, fixedTitle, titlePrefix, autoComplete);
    }
}
