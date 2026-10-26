using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>Un bloque del Lienzo: etiqueta + posicion en la grilla + acento + mapeo de origen opcional.</summary>
public sealed record LienzoBlock(
    string Id,
    string Label,
    string? Accent,
    int Col,
    int Row,
    int ColSpan,
    int RowSpan,
    IReadOnlyList<string> Source);

/// <summary>
/// Config del control "Lienzo de bloques" (FormControlType.LienzoBloques), guardada en FormQuestion.OptionsJson y
/// editable en el FormDesigner (sin SQL). Generico: el autor define los bloques; BMC (Business Model Canvas) es un
/// PRESET de 9 bloques. El valor del campo en FormResponse.Data es un objeto JSON { blockId: texto }. 'source'
/// (field codes del mismo formulario) pre-llena el bloque concatenando esas respuestas (editable).
/// </summary>
public sealed record LienzoBloquesConfig(string? Preset, int Columns, IReadOnlyList<LienzoBlock> Blocks)
{
    public static LienzoBloquesConfig Empty => new(null, 10, Array.Empty<LienzoBlock>());

    public const string AccentPrimary = "primary";

    /// <summary>Lee la config desde el OptionsJson del control (forma objeto {preset,columns,blocks}). Tolerante.</summary>
    public static LienzoBloquesConfig Parse(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) { return Bmc(); }   // sin config aun -> BMC por defecto
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) { return Bmc(); }
            var preset = r.TryGetProperty("preset", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var columns = r.TryGetProperty("columns", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : 10;
            if (columns is < 1 or > 24) { columns = 10; }
            var blocks = new List<LienzoBlock>();
            if (r.TryGetProperty("blocks", out var bs) && bs.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in bs.EnumerateArray())
                {
                    if (b.ValueKind != JsonValueKind.Object) { continue; }
                    var id = Str(b, "id");
                    if (string.IsNullOrWhiteSpace(id)) { continue; }
                    blocks.Add(new LienzoBlock(
                        Id: id!,
                        Label: Str(b, "label") ?? id!,
                        Accent: Str(b, "accent"),
                        Col: IntOr(b, "col", 1),
                        Row: IntOr(b, "row", 1),
                        ColSpan: Math.Max(1, IntOr(b, "colSpan", 1)),
                        RowSpan: Math.Max(1, IntOr(b, "rowSpan", 1)),
                        Source: ReadStrings(b, "source")));
                }
            }
            if (blocks.Count == 0) { return Bmc(); }
            return new LienzoBloquesConfig(preset, columns, blocks);
        }
        catch (JsonException) { return Bmc(); }
    }

    /// <summary>Serializa a OptionsJson (lo que escribe el disenador).</summary>
    public static string Serialize(LienzoBloquesConfig cfg)
        => JsonSerializer.Serialize(new
        {
            preset = cfg.Preset,
            columns = cfg.Columns,
            blocks = cfg.Blocks.Select(b => new { id = b.Id, label = b.Label, accent = b.Accent, col = b.Col, row = b.Row, colSpan = b.ColSpan, rowSpan = b.RowSpan, source = b.Source })
        });

    /// <summary>Preset Business Model Canvas: 9 bloques en la disposicion canonica (grilla de 10 columnas internas
    /// para un bottom 50/50 limpio). 'source' vacio: el autor lo mapea en su formulario.</summary>
    public static LienzoBloquesConfig Bmc()
    {
        var b = new List<LienzoBlock>
        {
            new("partners",   "Socios clave",            null,          1, 1, 2, 2, Array.Empty<string>()),
            new("activities", "Actividades clave",       null,          3, 1, 2, 1, Array.Empty<string>()),
            new("resources",  "Recursos clave",          null,          3, 2, 2, 1, Array.Empty<string>()),
            new("value",      "Propuesta de valor",      AccentPrimary, 5, 1, 2, 2, Array.Empty<string>()),
            new("relations",  "Relaciones",              null,          7, 1, 2, 1, Array.Empty<string>()),
            new("channels",   "Canales",                 null,          7, 2, 2, 1, Array.Empty<string>()),
            new("segments",   "Segmentos de clientes",   null,          9, 1, 2, 2, Array.Empty<string>()),
            new("costs",      "Estructura de costos",    null,          1, 3, 5, 1, Array.Empty<string>()),
            new("revenue",    "Fuentes de ingresos",     null,          6, 3, 5, 1, Array.Empty<string>()),
        };
        return new LienzoBloquesConfig("bmc", 10, b);
    }

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int IntOr(JsonElement o, string name, int fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    private static IReadOnlyList<string> ReadStrings(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) { return Array.Empty<string>(); }
        var list = new List<string>();
        foreach (var el in v.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString())) { list.Add(el.GetString()!); }
        }
        return list;
    }
}
