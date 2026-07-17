using System.Globalization;
using System.Text.Json;
using Ecorex.Application.Forms;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms.Calc;

/// <summary>
/// Columna de un GridDetail. Ademas de las propiedades de calculo (ola F2, doc 01 D5): formula por
/// fila (<see cref="Calc"/>), agregado de columna (<see cref="Agg"/>) y roll-up al encabezado
/// (<see cref="Rollup"/>), una columna declara su tipo de captura (D3): <see cref="Kind"/> "text"
/// (por defecto) o "select" con su lista <see cref="Options"/>, y si es <see cref="Required"/>. Se
/// parsea del OptionsJson de la pregunta; columnas viejas [{id,label}] siguen valiendo (todo lo
/// demas es opcional y cae a texto no-requerido).
/// </summary>
public sealed record FormGridColumn(
    string Id,
    string Label,
    string? Calc,
    FormAggregate Agg,
    string? Rollup,
    string Kind = "text",
    IReadOnlyList<FormOption>? Options = null,
    bool Required = false)
{
    /// <summary>La columna captura de una lista fija (Select).</summary>
    public bool IsSelect => string.Equals(Kind, "select", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Calculo de tablas (GridDetail) compartido por el renderer (UX inmediata) y el servidor
/// (revalidacion al guardar; fuente de verdad). Reusa <see cref="FormExpressionEvaluator"/> para
/// las formulas por fila; los agregados y el roll-up son aritmetica pura, sin SQL ni reflexion.
/// </summary>
public static class FormGridCalculator
{
    public static IReadOnlyList<FormGridColumn> ParseColumns(string? optionsJson)
    {
        var list = new List<FormGridColumn>();
        if (string.IsNullOrWhiteSpace(optionsJson)) { return list; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return list; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var id = el.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) { continue; }
                var label = el.TryGetProperty("label", out var pl) ? pl.GetString() ?? id : id;
                var calc = el.TryGetProperty("calc", out var pc) ? pc.GetString() : null;
                var rollup = el.TryGetProperty("rollup", out var pr) ? pr.GetString() : null;
                var agg = FormAggregate.None;
                if (el.TryGetProperty("agg", out var pa) && Enum.TryParse<FormAggregate>(pa.GetString(), ignoreCase: true, out var parsed)) { agg = parsed; }

                // D3: tipo de captura y, si es lista, sus opciones. "type" en el JSON por consistencia
                // con el campo (que usa control_type); aqui es solo "text" o "select".
                var kind = el.TryGetProperty("type", out var pt) ? (pt.GetString() ?? "text") : "text";
                var required = el.TryGetProperty("required", out var prq) && prq.ValueKind == JsonValueKind.True;
                List<FormOption>? options = null;
                if (el.TryGetProperty("options", out var po) && po.ValueKind == JsonValueKind.Array)
                {
                    options = new List<FormOption>();
                    foreach (var oe in po.EnumerateArray())
                    {
                        if (oe.ValueKind != JsonValueKind.Object) { continue; }
                        var oid = oe.TryGetProperty("id", out var poid) ? poid.GetString() : null;
                        if (string.IsNullOrWhiteSpace(oid)) { continue; }
                        var olabel = oe.TryGetProperty("label", out var pol) ? pol.GetString() ?? oid : oid;
                        options.Add(new FormOption(oid!, olabel));
                    }
                }

                list.Add(new FormGridColumn(
                    id!, label,
                    string.IsNullOrWhiteSpace(calc) ? null : calc,
                    agg,
                    string.IsNullOrWhiteSpace(rollup) ? null : rollup,
                    string.IsNullOrWhiteSpace(kind) ? "text" : kind.Trim().ToLowerInvariant(),
                    options,
                    required));
            }
        }
        catch (JsonException) { /* columnas invalidas: tabla vacia */ }
        return list;
    }

    /// <summary>Agrega los valores numericos de una columna segun el tipo de agregado.</summary>
    public static decimal? Aggregate(FormAggregate agg, IEnumerable<string?> rawValues)
    {
        var nums = rawValues
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(ParseNumber)
            .Where(n => n.HasValue).Select(n => n!.Value).ToList();
        return agg switch
        {
            FormAggregate.Sum => nums.Sum(),
            FormAggregate.Count => nums.Count,
            FormAggregate.Avg => nums.Count == 0 ? null : nums.Average(),
            FormAggregate.Min => nums.Count == 0 ? null : nums.Min(),
            FormAggregate.Max => nums.Count == 0 ? null : nums.Max(),
            _ => null,
        };
    }

    /// <summary>
    /// Recalcula las filas: evalua las columnas con formula por fila (in place) y devuelve las
    /// filas computadas mas el mapa de roll-ups (campo del encabezado -> total de columna).
    /// </summary>
    public static (List<Dictionary<string, string?>> Rows, Dictionary<string, string?> Rollups) Recompute(
        IReadOnlyList<Dictionary<string, string?>> rows, IReadOnlyList<FormGridColumn> columns)
    {
        var result = rows.Select(r => new Dictionary<string, string?>(r, StringComparer.Ordinal)).ToList();
        foreach (var row in result)
        {
            foreach (var col in columns.Where(c => c.Calc is not null))
            {
                var res = FormExpressionEvaluator.Evaluate(col.Calc, row);
                row[col.Id] = res?.ToString(CultureInfo.InvariantCulture);
            }
        }
        var rollups = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var col in columns.Where(c => c.Agg != FormAggregate.None && !string.IsNullOrWhiteSpace(c.Rollup)))
        {
            var total = Aggregate(col.Agg, result.Select(r => r.GetValueOrDefault(col.Id)));
            rollups[col.Rollup!] = total?.ToString(CultureInfo.InvariantCulture);
        }
        return (result, rollups);
    }

    private static decimal? ParseNumber(string? v)
        => decimal.TryParse((v ?? "").Replace(" ", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
}
