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
    bool Required = false,
    string? AggWhen = null)
{
    /// <summary>La columna captura de una lista fija (Select).</summary>
    public bool IsSelect => string.Equals(Kind, "select", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Resultado del recalculo de una tabla. Ademas de las filas computadas y los roll-ups, expone
/// que FILAS quedaron FUERA del agregado por la condicion <c>aggWhen</c> (C4): el calculo es el
/// unico que sabe evaluarla, y el renderer necesita el dato para marcar la fila visualmente sin
/// volver a interpretar formulas.
/// </summary>
public sealed record FormGridComputation(
    List<Dictionary<string, string?>> Rows,
    Dictionary<string, string?> Rollups,
    IReadOnlyList<int> ExcludedRowIndexes,
    IReadOnlyDictionary<string, IReadOnlyList<int>> ExcludedRowIndexesByColumn);

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
                // C4: condicion de INCLUSION del agregado ("aggWhen": "{sin_stock}=0"). Ausente =
                // se suman todas las filas, o sea el comportamiento de siempre.
                var aggWhen = el.TryGetProperty("aggWhen", out var paw) ? paw.GetString() : null;

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
                    required,
                    string.IsNullOrWhiteSpace(aggWhen) ? null : aggWhen));
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
    /// C4: decide si una fila ENTRA en el agregado de una columna. Condicion vacia = entra (es el
    /// comportamiento historico). Condicion INVALIDA tambien entra: preferimos fallar "abierto" a
    /// que un error de tipeo en la formula reduzca un total de dinero en silencio.
    /// </summary>
    public static bool RowIsIncluded(
        string? aggWhen,
        IReadOnlyDictionary<string, string?> row,
        IReadOnlyDictionary<string, string?>? headerValues = null)
    {
        if (string.IsNullOrWhiteSpace(aggWhen)) { return true; }
        var result = FormExpressionEvaluator.Evaluate(aggWhen, row, headerValues);
        return result is null || result.Value != 0m;
    }

    /// <summary>Agrega los valores numericos de una columna respetando su condicion <c>aggWhen</c>.</summary>
    public static decimal? Aggregate(
        FormGridColumn column,
        IEnumerable<Dictionary<string, string?>> rows,
        IReadOnlyDictionary<string, string?>? headerValues = null)
        => Aggregate(
            column.Agg,
            rows.Where(r => RowIsIncluded(column.AggWhen, r, headerValues)).Select(r => r.GetValueOrDefault(column.Id)));

    /// <summary>
    /// Recalcula las filas: evalua las columnas con formula por fila (in place) y devuelve las
    /// filas computadas mas el mapa de roll-ups (campo del encabezado -> total de columna).
    /// Envoltorio de <see cref="Compute"/> que se conserva para no romper a los llamadores.
    /// </summary>
    public static (List<Dictionary<string, string?>> Rows, Dictionary<string, string?> Rollups) Recompute(
        IReadOnlyList<Dictionary<string, string?>> rows,
        IReadOnlyList<FormGridColumn> columns,
        IReadOnlyDictionary<string, string?>? headerValues = null)
    {
        var computation = Compute(rows, columns, headerValues);
        return (computation.Rows, computation.Rollups);
    }

    /// <summary>
    /// Recalcula las filas y devuelve ademas que filas quedaron excluidas del agregado (C4).
    /// <paramref name="headerValues"/> resuelve las referencias <c>{#campo}</c> al encabezado del
    /// formulario (C3): asi el IVA de la cotizacion, editable por documento, entra a la formula de
    /// una columna sin duplicarlo en cada fila.
    /// </summary>
    public static FormGridComputation Compute(
        IReadOnlyList<Dictionary<string, string?>> rows,
        IReadOnlyList<FormGridColumn> columns,
        IReadOnlyDictionary<string, string?>? headerValues = null)
    {
        var result = rows.Select(r => new Dictionary<string, string?>(r, StringComparer.Ordinal)).ToList();
        foreach (var row in result)
        {
            foreach (var col in columns.Where(c => c.Calc is not null))
            {
                var res = FormExpressionEvaluator.Evaluate(col.Calc, row, headerValues);
                row[col.Id] = res?.ToString(CultureInfo.InvariantCulture);
            }
        }

        // Las exclusiones se calculan DESPUES de las formulas de fila: una condicion tipica
        // ("{sin_stock}=0") mira justamente una columna calculada.
        var excludedByColumn = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        var excludedAny = new SortedSet<int>();
        foreach (var col in columns.Where(c => !string.IsNullOrWhiteSpace(c.AggWhen)))
        {
            var excluded = new List<int>();
            for (var i = 0; i < result.Count; i++)
            {
                if (!RowIsIncluded(col.AggWhen, result[i], headerValues)) { excluded.Add(i); }
            }
            excludedByColumn[col.Id] = excluded;
            foreach (var i in excluded) { excludedAny.Add(i); }
        }

        var rollups = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var col in columns.Where(c => c.Agg != FormAggregate.None && !string.IsNullOrWhiteSpace(c.Rollup)))
        {
            var total = Aggregate(col, result, headerValues);
            rollups[col.Rollup!] = total?.ToString(CultureInfo.InvariantCulture);
        }
        return new FormGridComputation(result, rollups, excludedAny.ToList(), excludedByColumn);
    }

    private static decimal? ParseNumber(string? v)
        => decimal.TryParse((v ?? "").Replace(" ", "").Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
}
