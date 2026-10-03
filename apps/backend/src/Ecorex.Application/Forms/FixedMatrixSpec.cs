using System.Text.Json;

namespace Ecorex.Application.Forms;

public sealed record FixedMatrixRow(string Id, string Label);

public sealed record FixedMatrixCol(string Id, string Label, string? Group, string? Format);

/// <summary>
/// Definicion de una MATRIZ FIJA (control <see cref="Ecorex.Domain.Enums.FormControlType.FixedMatrix"/>): filas
/// (conceptos) y columnas (agrupables) predefinidas, una casilla por celda. Es la primitiva nativa para formatos
/// tipo DIAN 350 (concepto x juridicas/naturales x base/retencion), que antes se simulaban con decenas de Rows y
/// campos sueltos alineados "por convencion de width".
///
/// OptionsJson (OBJETO):
///   {"rows":[{"id":"honorarios","label":"Honorarios"}],
///    "cols":[{"id":"jur_base","label":"Base sujeta a retencion","group":"A personas juridicas","format":"currency"}],
///    "captions":{"honorarios.jur_base":"29"},      // numero de casilla por celda (opcional)
///    "disabled":["rentas_trabajo.jur_base"]}       // celdas que NO aplican (opcional)
/// Valor del campo: objeto JSON plano {"fila.col":"valor"}.
/// Parser compartido por renderer, impresion, validador, toolset y disenador (una sola fuente de verdad).
/// </summary>
public sealed class FixedMatrixSpec
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<FixedMatrixRow> Rows { get; }
    public IReadOnlyList<FixedMatrixCol> Cols { get; }
    public IReadOnlyDictionary<string, string> Captions { get; }
    public IReadOnlySet<string> Disabled { get; }

    private FixedMatrixSpec(IReadOnlyList<FixedMatrixRow> rows, IReadOnlyList<FixedMatrixCol> cols,
        IReadOnlyDictionary<string, string> captions, IReadOnlySet<string> disabled)
    {
        Rows = rows; Cols = cols; Captions = captions; Disabled = disabled;
    }

    /// <summary>Clave de celda: "fila.col".</summary>
    public static string Key(string rowId, string colId) => rowId + "." + colId;

    public bool IsDisabled(string rowId, string colId) => Disabled.Contains(Key(rowId, colId));

    public string? Caption(string rowId, string colId) => Captions.TryGetValue(Key(rowId, colId), out var c) ? c : null;

    /// <summary>Celdas CAPTURABLES (filas x columnas menos las n/a).</summary>
    public int ActiveCellCount => Rows.Sum(r => Cols.Count(c => !IsDisabled(r.Id, c.Id)));

    /// <summary>Parsea OptionsJson. Null si no es un objeto con al menos una fila y una columna con ids
    /// unicos (misma regla que las opciones de un Select: id y label obligatorios).</summary>
    public static FixedMatrixSpec? Parse(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return null; }

            var rows = new List<FixedMatrixRow>();
            if (root.TryGetProperty("rows", out var rs) && rs.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rs.EnumerateArray())
                {
                    var id = Str(r, "id"); var label = Str(r, "label");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) { return null; }
                    rows.Add(new FixedMatrixRow(id!.Trim(), label!.Trim()));
                }
            }
            var cols = new List<FixedMatrixCol>();
            if (root.TryGetProperty("cols", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cs.EnumerateArray())
                {
                    var id = Str(c, "id"); var label = Str(c, "label");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) { return null; }
                    var group = Str(c, "group"); var format = Str(c, "format");
                    cols.Add(new FixedMatrixCol(id!.Trim(), label!.Trim(),
                        string.IsNullOrWhiteSpace(group) ? null : group!.Trim(),
                        string.IsNullOrWhiteSpace(format) ? null : format!.Trim()));
                }
            }
            if (rows.Count == 0 || cols.Count == 0) { return null; }
            if (rows.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != rows.Count) { return null; }
            if (cols.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != cols.Count) { return null; }

            var captions = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("captions", out var caps) && caps.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in caps.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } v) { captions[p.Name] = v; }
                    else if (p.Value.ValueKind == JsonValueKind.Number) { captions[p.Name] = p.Value.GetRawText(); }
                }
            }
            var disabled = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("disabled", out var dis) && dis.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in dis.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } s) { disabled.Add(s); }
                }
            }
            return new FixedMatrixSpec(rows, cols, captions, disabled);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Valor del campo -> celdas {"fila.col": valor}. Diccionario vacio si es nulo/invalido.</summary>
    public static Dictionary<string, string?> ParseValue(string? value)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) { return result; }
        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return result; }
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                result[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number => p.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
            }
        }
        catch (JsonException) { /* valor corrupto: se trata como vacio */ }
        return result;
    }

    /// <summary>Celdas -> valor del campo (omite celdas vacias; claves ordenadas para que el JSON sea estable).</summary>
    public static string SerializeValue(IDictionary<string, string?> cells)
    {
        var clean = cells.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
        return JsonSerializer.Serialize(clean, JsonOpts);
    }

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
