using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ecorex.Application.Workflows;

/// <summary>Como se cuentan los DIAS de un plazo de paso (Fase 1 - plazos de flujo).</summary>
public enum StepSlaDayMode
{
    /// <summary>Dias de calendario (24h reales cada uno), incluyendo fines de semana y festivos.</summary>
    Calendar = 0,
    /// <summary>Dias habiles: saltan fines de semana y los dias no operativos del tenant.</summary>
    Business = 1
}

/// <summary>
/// Plazo (SLA) de UN paso del flujo: dias + horas + minutos, con el modo de los DIAS (calendario o habil).
/// Es un ESTIMADO; el reloj real de cada paso arranca cuando el paso anterior termina de verdad. Se persiste
/// como <c>WorkflowNode.SlaJson</c> = { "days","hours","minutes","dayMode":"calendar|business" }. Solo las
/// horas/minutos son tiempo real; los dias respetan el modo. Helper PURO (parse/build), testeable sin BD.
/// </summary>
public sealed record StepSla(int Days, int Hours, int Minutes, StepSlaDayMode DayMode)
{
    public static readonly StepSla None = new(0, 0, 0, StepSlaDayMode.Calendar);

    /// <summary>Sin plazo = todos los componentes en cero (no fija vencimiento).</summary>
    public bool IsEmpty => Days <= 0 && Hours <= 0 && Minutes <= 0;

    /// <summary>Lee el plazo del JSON del nodo. Ausente/invalido = None (sin plazo).</summary>
    public static StepSla Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return None; }
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) { return None; }
            var mode = (Str(o, "dayMode") ?? "calendar").Trim().ToLowerInvariant() == "business"
                ? StepSlaDayMode.Business : StepSlaDayMode.Calendar;
            return new StepSla(Clamp(Int(o, "days")), Clamp(Int(o, "hours")), Clamp(Int(o, "minutes")), mode);
        }
        catch (JsonException) { return None; }
    }

    /// <summary>Arma el JSON del plazo. Devuelve null cuando esta vacio (=> el nodo no fija vencimiento).</summary>
    public static string? Build(int days, int hours, int minutes, StepSlaDayMode dayMode)
    {
        var d = Math.Max(0, days);
        var h = Math.Max(0, hours);
        var m = Math.Max(0, minutes);
        if (d == 0 && h == 0 && m == 0) { return null; }
        return new JsonObject
        {
            ["days"] = d,
            ["hours"] = h,
            ["minutes"] = m,
            ["dayMode"] = dayMode == StepSlaDayMode.Business ? "business" : "calendar"
        }.ToJsonString();
    }

    private static int Clamp(int v) => v < 0 ? 0 : v;

    private static int Int(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return 0; }
        try { return v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : (int.TryParse(v.ToString(), out var n) ? n : 0); }
        catch { return 0; }
    }

    private static string? Str(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return null; }
        try { return v.GetValue<string?>(); } catch { return v.ToString(); }
    }
}

/// <summary>
/// Lista ORDENADA de plazos = la CADENCIA de seguimiento de un nodo con agente (WorkflowNode.AgentFollowUpJson).
/// Se serializa como JSON array de objetos {days,hours,minutes}. El 1er elemento es el PRIMER contacto (p.ej.
/// 0 = inmediato) y los siguientes son la espera ANTES de cada recordatorio. Al agotarse la lista, el agente
/// deja de insistir. Helper PURO (parse/build), testeable sin BD. (Se mantienen los ceros: el 1er elemento
/// suele ser 0; los demas en 0 se interpretan como "fin de la cadencia" en el runner.)
/// </summary>
public static class StepSlaList
{
    /// <summary>Total en MINUTOS de cada entrada (days*1440 + hours*60 + minutes). Ausente/invalido = vacio.</summary>
    public static IReadOnlyList<int> ReadMinutes(string? json)
    {
        var rows = ReadRows(json);
        var list = new List<int>(rows.Count);
        foreach (var r in rows) { list.Add((r.Days * 1440) + (r.Hours * 60) + r.Minutes); }
        return list;
    }

    /// <summary>Lee las filas (dias,horas,minutos) de la lista. Ausente/invalido = vacio.</summary>
    public static IReadOnlyList<(int Days, int Hours, int Minutes)> ReadRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<(int, int, int)>(); }
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) { return Array.Empty<(int, int, int)>(); }
            var list = new List<(int, int, int)>(arr.Count);
            foreach (var el in arr)
            {
                if (el is not JsonObject o) { continue; }
                list.Add((Math.Max(0, IntOf(o, "days")), Math.Max(0, IntOf(o, "hours")), Math.Max(0, IntOf(o, "minutes"))));
            }
            return list;
        }
        catch (JsonException) { return Array.Empty<(int, int, int)>(); }
    }

    /// <summary>Arma el JSON array desde filas (dias,horas,minutos). Lista vacia -> null (sin cadencia).</summary>
    public static string? Build(IEnumerable<(int Days, int Hours, int Minutes)> rows)
    {
        var arr = new JsonArray();
        foreach (var r in rows)
        {
            arr.Add(new JsonObject
            {
                ["days"] = Math.Max(0, r.Days),
                ["hours"] = Math.Max(0, r.Hours),
                ["minutes"] = Math.Max(0, r.Minutes)
            });
        }
        return arr.Count == 0 ? null : arr.ToJsonString();
    }

    private static int IntOf(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return 0; }
        try { return v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : (int.TryParse(v.ToString(), out var n) ? n : 0); }
        catch { return 0; }
    }
}
