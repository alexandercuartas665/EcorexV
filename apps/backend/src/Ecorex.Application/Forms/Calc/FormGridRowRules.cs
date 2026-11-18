using System.Text.Json;

namespace Ecorex.Application.Forms.Calc;

/// <summary>Reglas POR FILA de una columna de grilla (ADR-0181). Ver <see cref="FormGridRowRules"/>.</summary>
public sealed record FormGridRowRule(string ColumnId, string Label, string? RequiredWhen, string? RequiredUnless, string? MaxCol);

/// <summary>
/// Reglas que relacionan columnas de la MISMA fila de una grilla (GridDetail). Son DATO en el options_json de cada
/// columna, igual que "required" o "min", y el disenador las conserva aunque no las edite:
///   - "requiredWhen": "colId"   la celda es obligatoria cuando la columna colId de la misma fila esta MARCADA
///                                (ej. "motivo" obligatorio si "no_cotiza" esta marcada).
///   - "requiredUnless": "colId" la celda es obligatoria SALVO que colId este marcada (ej. "vlr_uni" obligatorio
///                                excepto en los items que no se cotizan).
///   - "maxCol": "colId"         el valor numerico no puede superar al de colId en la misma fila
///                                (ej. cantidad cotizada &lt;= cantidad solicitada).
/// "Marcada" = no vacia y distinta de false / no / 0 / [] (sirve para una lista Si/No, un multicheck o un toggle).
/// Se valida al ENVIAR el formulario (FormFieldValidator) y desde los botones que lo piden antes de actuar.
/// </summary>
public static class FormGridRowRules
{
    public static IReadOnlyList<FormGridRowRule> Parse(string? optionsJson)
    {
        var list = new List<FormGridRowRule>();
        if (string.IsNullOrWhiteSpace(optionsJson)) { return list; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return list; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var id = Str(el, "id");
                if (string.IsNullOrWhiteSpace(id)) { continue; }
                var when = Str(el, "requiredWhen");
                var unless = Str(el, "requiredUnless");
                var max = Str(el, "maxCol");
                if (when is null && unless is null && max is null) { continue; }
                var label = Str(el, "label") ?? Str(el, "name") ?? id;
                list.Add(new FormGridRowRule(id!, label!, when, unless, max));
            }
        }
        catch (JsonException) { /* options_json corrupto: sin reglas */ }
        return list;
    }

    /// <summary>Primer error de las reglas por fila, o null si todo cuadra. Las filas vienen como en el valor
    /// guardado (columna -> texto).</summary>
    public static string? Validate(IReadOnlyList<IReadOnlyDictionary<string, string?>> rows, string? optionsJson)
    {
        var rules = Parse(optionsJson);
        if (rules.Count == 0 || rows.Count == 0) { return null; }
        var labels = FormGridCalculator.ParseColumns(optionsJson)
            .ToDictionary(c => c.Id, c => c.Label, StringComparer.Ordinal);
        string L(string id) => labels.TryGetValue(id, out var l) ? l : id;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            foreach (var r in rules)
            {
                var cell = row.GetValueOrDefault(r.ColumnId);
                var empty = string.IsNullOrWhiteSpace(cell);
                if (r.RequiredWhen is { } w && empty && IsMarked(row.GetValueOrDefault(w)))
                {
                    return $"La columna '{r.Label}' es obligatoria cuando '{L(w)}' esta marcada (fila {i + 1}).";
                }
                if (r.RequiredUnless is { } u && empty && !IsMarked(row.GetValueOrDefault(u)))
                {
                    return $"La columna '{r.Label}' es obligatoria (fila {i + 1}), salvo que marques '{L(u)}'.";
                }
                if (r.MaxCol is { } m && !empty)
                {
                    var v = FormGridCalculator.ParseNumber(cell);
                    var tope = FormGridCalculator.ParseNumber(row.GetValueOrDefault(m));
                    if (v is decimal a && tope is decimal b && a > b)
                    {
                        return $"'{r.Label}' ({cell}) no puede superar '{L(m)}' ({row.GetValueOrDefault(m)}) en la fila {i + 1}.";
                    }
                }
            }
        }
        return null;
    }

    /// <summary>Valor "marcado": no vacio y distinto de false/no/0/[].</summary>
    public static bool IsMarked(string? value)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t)) { return false; }
        return !(t.Equals("false", StringComparison.OrdinalIgnoreCase) || t.Equals("no", StringComparison.OrdinalIgnoreCase)
            || t == "0" || t == "[]");
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString())
            ? p.GetString()!.Trim() : null;
}
