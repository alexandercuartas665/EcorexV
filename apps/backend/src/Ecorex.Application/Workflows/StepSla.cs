using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Lee las filas (dias,horas,minutos) de la lista. Ausente/invalido = vacio. Acepta tanto el array
    /// plano (legacy) como el objeto { "items":[...], "window":{...} } que lleva ademas la ventana horaria.</summary>
    public static IReadOnlyList<(int Days, int Hours, int Minutes)> ReadRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<(int, int, int)>(); }
        try
        {
            var node = JsonNode.Parse(json);
            var arr = node as JsonArray ?? (node as JsonObject)?["items"] as JsonArray;
            if (arr is null) { return Array.Empty<(int, int, int)>(); }
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
        => Build(rows, null);

    /// <summary>
    /// Arma el JSON de la cadencia, embebiendo opcionalmente la VENTANA HORARIA. Sin filas -> null (sin
    /// cadencia; la ventana sola no aplica). Con filas pero SIN ventana -> array plano (compat hacia atras,
    /// misma forma de siempre). Con filas Y ventana -> objeto { "items":[...], "window":{...} }.
    /// </summary>
    public static string? Build(IEnumerable<(int Days, int Hours, int Minutes)> rows, PulseSendWindow? window)
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
        if (arr.Count == 0) { return null; }
        var win = window?.ToJson();
        if (win is null) { return arr.ToJsonString(); }
        return new JsonObject { ["items"] = arr, ["window"] = win }.ToJsonString();
    }

    private static int IntOf(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return 0; }
        try { return v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : (int.TryParse(v.ToString(), out var n) ? n : 0); }
        catch { return 0; }
    }
}

/// <summary>
/// VENTANA HORARIA DE ENVIO (horario de atencion) de los PULSOS del agente de un nodo: franja [inicio,fin] del
/// dia en minutos desde medianoche + dias de semana activos (ISO 1=Lunes..7=Domingo), con opcion de saltar los
/// dias no operativos del tenant (festivos). Si un pulso (recordatorio de la cadencia de seguimiento) cae FUERA
/// de la ventana, se corre al INICIO de la proxima ventana disponible, para no escribir a la gente de madrugada.
/// Se persiste DENTRO del propio <c>WorkflowNode.AgentFollowUpJson</c> (sin columna nueva; ver StepSlaList).
/// Helper PURO (parse/build/clamp): usa TimeZoneInfo pero no BD, testeable aislado.
/// </summary>
public sealed record PulseSendWindow(bool Enabled, int StartMin, int EndMin, IReadOnlySet<int> Days, bool RespectHolidays)
{
    public static readonly PulseSendWindow Disabled = new(false, 0, 0, new HashSet<int>(), false);

    private const int DayMinutes = 24 * 60;

    /// <summary>Lee la ventana embebida en el JSON de seguimiento. Array plano (legacy)/ausente/invalida = Disabled.</summary>
    public static PulseSendWindow Read(string? followUpJson)
    {
        if (string.IsNullOrWhiteSpace(followUpJson)) { return Disabled; }
        try
        {
            if (JsonNode.Parse(followUpJson) is not JsonObject root) { return Disabled; }
            if (root["window"] is not JsonObject w) { return Disabled; }
            if (!(w["enabled"]?.GetValue<bool>() ?? false)) { return Disabled; }
            var start = ClampMin(IntOf(w, "startMin"));
            var end = ClampMin(IntOf(w, "endMin"));
            if (end <= start) { return Disabled; }
            var days = new HashSet<int>();
            if (w["days"] is JsonArray da)
            {
                foreach (var el in da)
                {
                    if (el is null) { continue; }
                    try { var n = el.GetValue<int>(); if (n >= 1 && n <= 7) { days.Add(n); } } catch { /* entrada no numerica: se ignora */ }
                }
            }
            if (days.Count == 0) { for (var d = 1; d <= 7; d++) { days.Add(d); } }
            var holidays = w["respectHolidays"]?.GetValue<bool>() ?? false;
            return new PulseSendWindow(true, start, end, days, holidays);
        }
        catch (JsonException) { return Disabled; }
    }

    /// <summary>Serializa la ventana a un JsonObject para embeberla en el JSON de seguimiento. Null = deshabilitada.</summary>
    public JsonObject? ToJson()
    {
        if (!Enabled || EndMin <= StartMin) { return null; }
        var da = new JsonArray();
        foreach (var d in Days.Where(x => x >= 1 && x <= 7).OrderBy(x => x)) { da.Add(d); }
        return new JsonObject
        {
            ["enabled"] = true,
            ["startMin"] = StartMin,
            ["endMin"] = EndMin,
            ["days"] = da,
            ["respectHolidays"] = RespectHolidays
        };
    }

    /// <summary>
    /// Ajusta un instante UTC a la ventana (evaluada en la zona del tenant). Dentro de la ventana -> sin cambio.
    /// Fuera -> inicio de la proxima ventana disponible (proximo dia activo y no festivo, a la hora de apertura).
    /// </summary>
    public DateTimeOffset ClampUtc(DateTimeOffset dueUtc, TimeZoneInfo tz, IReadOnlySet<DateOnly> nonWorking)
    {
        if (!Enabled || EndMin <= StartMin) { return dueUtc; }
        var local = TimeZoneInfo.ConvertTime(dueUtc, tz);
        var day = DateOnly.FromDateTime(local.DateTime);
        var minuteOfDay = (local.Hour * 60) + local.Minute;

        bool DayOk(DateOnly d) => Days.Contains(IsoDow(d.DayOfWeek)) && !(RespectHolidays && nonWorking.Contains(d));

        if (DayOk(day) && minuteOfDay >= StartMin && minuteOfDay < EndMin) { return dueUtc; }
        if (DayOk(day) && minuteOfDay < StartMin) { return ToUtc(day, StartMin, tz); }
        for (var i = 1; i <= 420; i++)
        {
            var d = day.AddDays(i);
            if (DayOk(d)) { return ToUtc(d, StartMin, tz); }
        }
        return dueUtc; // ninguna ventana en ~14 meses (todo apagado/festivo): no bloquea el pulso
    }

    private static DateTimeOffset ToUtc(DateOnly d, int minuteOfDay, TimeZoneInfo tz)
    {
        var localDt = d.ToDateTime(new TimeOnly(minuteOfDay / 60, minuteOfDay % 60));
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified), tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static int IsoDow(DayOfWeek d) => d == DayOfWeek.Sunday ? 7 : (int)d; // Lunes=1..Domingo=7
    private static int ClampMin(int v) => v < 0 ? 0 : (v > DayMinutes ? DayMinutes : v);

    private static int IntOf(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return 0; }
        try { return v.GetValueKind() == JsonValueKind.Number ? v.GetValue<int>() : (int.TryParse(v.ToString(), out var n) ? n : 0); }
        catch { return 0; }
    }
}
