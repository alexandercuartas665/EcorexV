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
