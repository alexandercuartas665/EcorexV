using System.Globalization;
using Ecorex.Application.Forms.Lookups;

namespace Ecorex.Application.Forms.Calc;

/// <summary>
/// CAP 2: resuelve una columna CROSS-GRID (referencia entre grillas del mismo registro) contra las filas
/// YA computadas de la grilla origen. Puro y sin BD: lo llaman el recalculo del servidor (autoritativo) y
/// el del cliente, DESPUES de que la grilla origen se recalculo (orden por dependencias). Reusa
/// <see cref="FormGridCalculator.Aggregate(Ecorex.Domain.Enums.FormAggregate, System.Collections.Generic.IEnumerable{string})"/>
/// y <see cref="FormGridCalculator.NormalizeKey"/> para que el emparejamiento (VLOOKUP/SUMIF) y los
/// subtotales agrupados (CAP 1) casen con la MISMA semantica exacta.
/// </summary>
public static class FormGridCrossRefResolver
{
    /// <summary>
    /// Valor de la celda cross-grid para <paramref name="currentRow"/>. En VLOOKUP devuelve el campo de la
    /// PRIMERA fila origen que empareja por todas las claves de <c>Match</c>; en SUMIF agrega el campo sobre
    /// TODAS las filas origen que emparejan (SUMIF/COUNTIF por grupo). Null si no hay match (VLOOKUP) o el
    /// agregado es nulo. Las refs de <c>Match</c> ("{col}" fila actual, "{#campo}" encabezado, o literal) se
    /// resuelven contra la fila actual/encabezado y se normalizan igual que la clave origen.
    /// </summary>
    public static string? Resolve(
        FormGridCrossRef cross,
        IReadOnlyList<Dictionary<string, string?>> sourceRows,
        IReadOnlyDictionary<string, string?> currentRow,
        IReadOnlyDictionary<string, string?>? headerValues = null)
    {
        // Valor NORMALIZADO esperado por cada columna clave de la grilla origen.
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceCol, refExpr) in cross.Match)
        {
            expected[sourceCol] = FormGridCalculator.NormalizeKey(ResolveRef(refExpr, currentRow, headerValues));
        }

        bool Matches(Dictionary<string, string?> r)
            => expected.All(kv => FormGridCalculator.NormalizeKey(r.GetValueOrDefault(kv.Key)) == kv.Value);

        if (cross.IsSumIf)
        {
            var values = sourceRows.Where(Matches).Select(r => r.GetValueOrDefault(cross.ValueField));
            return FormGridCalculator.Aggregate(cross.Agg, values)?.ToString(CultureInfo.InvariantCulture);
        }

        var hit = sourceRows.FirstOrDefault(Matches);
        return hit?.GetValueOrDefault(cross.ValueField);
    }

    /// <summary>Resuelve una ref de match: "{col}" = celda de la fila actual, "{#campo}" = valor del
    /// encabezado, cualquier otra cosa = literal.</summary>
    private static string? ResolveRef(
        string refExpr,
        IReadOnlyDictionary<string, string?> currentRow,
        IReadOnlyDictionary<string, string?>? headerValues)
    {
        var s = (refExpr ?? string.Empty).Trim();
        if (s.Length >= 3 && s.StartsWith("{#", StringComparison.Ordinal) && s.EndsWith("}", StringComparison.Ordinal))
        {
            var field = s[2..^1].Trim();
            return headerValues is not null && headerValues.TryGetValue(field, out var hv) ? hv : null;
        }
        if (s.Length >= 2 && s[0] == '{' && s[^1] == '}')
        {
            var col = s[1..^1].Trim();
            return currentRow.GetValueOrDefault(col);
        }
        return s; // literal
    }
}
