using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>
/// KPIs configurables de la bandeja del formulario-modulo (Ola 6/A1). Helper PURO: Read/Build del kpis_json +
/// ParseNumber para las metricas de campo. El JSON vive en <c>FormDefinition.KpisJson</c> como arreglo de
/// { "label", "metric", "field"? }. Null/vacio = KPIs por defecto (los 4 fijos historicos). Mismo patron que
/// StatusLadderJson / FormThemeJson (testeable sin BD ni Blazor).
/// </summary>
public static class KpiConfigJson
{
    // Metricas soportadas. Las de conteo no usan campo; sum/avg/min/max operan sobre un campo numerico.
    public const string Count = "count";         // total de registros
    public const string Confirmed = "confirmed"; // registros confirmados
    public const string Voided = "voided";       // registros anulados
    public const string Month = "month";         // registros de este mes + % de crecimiento
    public const string Sum = "sum";
    public const string Avg = "avg";
    public const string Min = "min";
    public const string Max = "max";

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    { Count, Confirmed, Voided, Month, Sum, Avg, Min, Max };

    /// <summary>True si la metrica necesita un campo numerico (sum/avg/min/max).</summary>
    public static bool MetricNeedsField(string? metric)
        => metric is not null && (metric.Equals(Sum, StringComparison.OrdinalIgnoreCase)
            || metric.Equals(Avg, StringComparison.OrdinalIgnoreCase)
            || metric.Equals(Min, StringComparison.OrdinalIgnoreCase)
            || metric.Equals(Max, StringComparison.OrdinalIgnoreCase));

    public sealed record Kpi(string Label, string Metric, string? Field);

    /// <summary>Lee los KPIs configurados. Lista vacia = usar los KPIs por defecto.</summary>
    public static List<Kpi> Read(string? json)
    {
        var list = new List<Kpi>();
        if (string.IsNullOrWhiteSpace(json)) { return list; }
        try
        {
            if (JsonNode.Parse(json) is not JsonArray arr) { return list; }
            foreach (var node in arr)
            {
                if (node is not JsonObject o) { continue; }
                var metric = Str(o, "metric");
                if (string.IsNullOrWhiteSpace(metric) || !Known.Contains(metric!)) { continue; }
                metric = metric!.Trim().ToLowerInvariant();
                var field = Str(o, "field");
                if (MetricNeedsField(metric) && string.IsNullOrWhiteSpace(field)) { continue; } // sin campo no sirve
                var label = Str(o, "label");
                list.Add(new Kpi(
                    string.IsNullOrWhiteSpace(label) ? DefaultLabel(metric, field) : label!.Trim(),
                    metric,
                    string.IsNullOrWhiteSpace(field) ? null : field!.Trim()));
            }
        }
        catch (JsonException) { return new List<Kpi>(); }
        return list;
    }

    /// <summary>Arma el kpis_json. null si no hay KPIs validos (=> la bandeja usa los por defecto).</summary>
    public static string? Build(IReadOnlyList<Kpi> kpis)
    {
        var arr = new JsonArray();
        foreach (var k in kpis)
        {
            if (string.IsNullOrWhiteSpace(k.Metric) || !Known.Contains(k.Metric)) { continue; }
            var metric = k.Metric.Trim().ToLowerInvariant();
            var needsField = MetricNeedsField(metric);
            if (needsField && string.IsNullOrWhiteSpace(k.Field)) { continue; }
            var o = new JsonObject
            {
                ["label"] = string.IsNullOrWhiteSpace(k.Label) ? DefaultLabel(metric, k.Field) : k.Label.Trim(),
                ["metric"] = metric
            };
            if (needsField) { o["field"] = k.Field!.Trim(); }
            arr.Add(o);
        }
        return arr.Count == 0 ? null : arr.ToJsonString();
    }

    /// <summary>Etiqueta por defecto de una metrica (cuando el usuario no la escribe).</summary>
    public static string DefaultLabel(string metric, string? field) => metric switch
    {
        Count => "Registros",
        Confirmed => "Confirmados",
        Voided => "Anulados",
        Month => "Este mes",
        Sum => "Total " + (field ?? ""),
        Avg => "Promedio " + (field ?? ""),
        Min => "Minimo " + (field ?? ""),
        Max => "Maximo " + (field ?? ""),
        _ => metric
    };

    /// <summary>Interpreta un valor de campo (texto EAV) como numero, tolerando moneda / miles / espacios y
    /// ambos estilos de separador (US "1,620,000.50" y es-CO "1620000,50"). Devuelve null si no hay numero.
    /// Regla: el ULTIMO separador es decimal solo si aparece UNA vez y con 1-2 digitos detras; si no, todos los
    /// separadores son de miles.</summary>
    public static double? ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var s = raw.Trim();
        // Camino rapido: el valor guardado por el motor es invariante (punto decimal, sin miles), incluidos
        // decimales largos ("145677.33146400"). Se parsea directo; solo si falla (moneda/miles) va la heuristica.
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var fast)) { return fast; }
        var sign = s.StartsWith('-') ? -1d : 1d;
        var body = new string(s.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray());  // sin signo/moneda/espacios
        if (body.Length == 0) { return null; }

        var lastSep = Math.Max(body.LastIndexOf('.'), body.LastIndexOf(','));
        string norm;
        if (lastSep < 0)
        {
            norm = body;
        }
        else
        {
            var sep = body[lastSep];
            var count = body.Count(c => c == sep);
            var after = body.Length - lastSep - 1;
            if (count == 1 && after is >= 1 and <= 2)   // separador decimal real
            {
                var other = sep == '.' ? "," : ".";
                norm = body.Replace(other, "").Replace(sep, '.');
            }
            else                                        // todos son de miles
            {
                norm = body.Replace(",", "").Replace(".", "");
            }
        }
        if (string.IsNullOrEmpty(norm)) { return null; }
        return double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? sign * v : null;
    }

    private static string? Str(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return null; }
        try { return v.GetValue<string?>(); } catch { return v.ToString(); }
    }
}
