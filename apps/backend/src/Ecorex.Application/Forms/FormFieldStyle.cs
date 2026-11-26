using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ecorex.Application.Forms;

/// <summary>
/// Diseno POR CONTROL de un campo de formulario (FormQuestion.StyleJson). Cuatro grupos:
/// etiqueta (tamano, negrita, cursiva, color, ocultar), caja del control (fondo, borde, alto, radio,
/// alineacion), bloque (margen arriba/abajo, fondo resaltado) y letra del valor (tamano, negrita, color).
/// Helper PURO: Parse/Normalize saneados (colores solo #hex, numeros acotados) para que el JSON nunca
/// pueda inyectar CSS; el renderer solo emite variables CSS y clases bandera a partir de este record.
/// </summary>
public sealed record FormFieldStyle
{
    [JsonPropertyName("labelSize")] public int? LabelSize { get; init; }
    [JsonPropertyName("labelBold")] public bool LabelBold { get; init; }
    [JsonPropertyName("labelItalic")] public bool LabelItalic { get; init; }
    [JsonPropertyName("labelColor")] public string? LabelColor { get; init; }
    [JsonPropertyName("hideLabel")] public bool HideLabel { get; init; }

    [JsonPropertyName("boxBg")] public string? BoxBg { get; init; }
    [JsonPropertyName("borderColor")] public string? BorderColor { get; init; }
    [JsonPropertyName("borderWidth")] public int? BorderWidth { get; init; }
    [JsonPropertyName("radius")] public int? Radius { get; init; }
    [JsonPropertyName("height")] public int? Height { get; init; }
    /// <summary>left | center | right.</summary>
    [JsonPropertyName("align")] public string? Align { get; init; }

    [JsonPropertyName("marginTop")] public int? MarginTop { get; init; }
    [JsonPropertyName("marginBottom")] public int? MarginBottom { get; init; }
    [JsonPropertyName("blockBg")] public string? BlockBg { get; init; }

    [JsonPropertyName("valueSize")] public int? ValueSize { get; init; }
    [JsonPropertyName("valueBold")] public bool ValueBold { get; init; }
    [JsonPropertyName("valueColor")] public string? ValueColor { get; init; }

    public static FormFieldStyle Empty { get; } = new();

    public bool IsEmpty => this == Empty;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    private static readonly Regex HexColor = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Lee y SANEA el JSON. Invalido / vacio => Empty.</summary>
    public static FormFieldStyle Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Empty; }
        try
        {
            var raw = JsonSerializer.Deserialize<FormFieldStyle>(json, JsonOpts);
            return raw is null ? Empty : Sanitize(raw);
        }
        catch (JsonException) { return Empty; }
    }

    /// <summary>JSON canonico saneado; null cuando no hay ningun ajuste (no se guarda basura).</summary>
    public static string? ToJson(FormFieldStyle style)
    {
        var s = Sanitize(style);
        return s.IsEmpty ? null : JsonSerializer.Serialize(s, JsonOpts);
    }

    /// <summary>Normaliza un JSON entrante (lo que guarda el servicio).</summary>
    public static string? Normalize(string? json) => ToJson(Parse(json));

    public static FormFieldStyle Sanitize(FormFieldStyle s) => new()
    {
        LabelSize = Clamp(s.LabelSize, 8, 48),
        LabelBold = s.LabelBold,
        LabelItalic = s.LabelItalic,
        LabelColor = Color(s.LabelColor),
        HideLabel = s.HideLabel,
        BoxBg = Color(s.BoxBg),
        BorderColor = Color(s.BorderColor),
        BorderWidth = Clamp(s.BorderWidth, 0, 8),
        Radius = Clamp(s.Radius, 0, 40),
        Height = Clamp(s.Height, 20, 400),
        Align = s.Align?.Trim().ToLowerInvariant() is "left" or "center" or "right" ? s.Align.Trim().ToLowerInvariant() : null,
        MarginTop = Clamp(s.MarginTop, 0, 120),
        MarginBottom = Clamp(s.MarginBottom, 0, 120),
        BlockBg = Color(s.BlockBg),
        ValueSize = Clamp(s.ValueSize, 8, 64),
        ValueBold = s.ValueBold,
        ValueColor = Color(s.ValueColor),
    };

    private static int? Clamp(int? v, int min, int max) => v is int n ? Math.Clamp(n, min, max) : null;

    private static string? Color(string? c)
    {
        var t = c?.Trim();
        return !string.IsNullOrEmpty(t) && HexColor.IsMatch(t) ? t.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Clases bandera para el bloque del campo (el CSS del renderer las usa para aplicar solo lo
    /// configurado y dejar el resto con el estilo del tema). Vacio si no hay estilo.
    /// </summary>
    public string CssClasses()
    {
        if (IsEmpty) { return string.Empty; }
        var c = new List<string> { "dfr-fs" };
        if (LabelSize is not null) { c.Add("fs-lsize"); }
        if (LabelBold) { c.Add("fs-lbold"); }
        if (LabelItalic) { c.Add("fs-litalic"); }
        if (LabelColor is not null) { c.Add("fs-lcolor"); }
        if (HideLabel) { c.Add("fs-nolabel"); }
        if (BoxBg is not null) { c.Add("fs-bg"); }
        if (BorderColor is not null || BorderWidth is not null) { c.Add("fs-bd"); }
        if (Radius is not null) { c.Add("fs-rad"); }
        if (Height is not null) { c.Add("fs-h"); }
        if (Align is not null) { c.Add("fs-align"); }
        if (BlockBg is not null) { c.Add("fs-block"); }
        if (ValueSize is not null) { c.Add("fs-vsize"); }
        if (ValueBold) { c.Add("fs-vbold"); }
        if (ValueColor is not null) { c.Add("fs-vcolor"); }
        return string.Join(' ', c);
    }

    /// <summary>Estilo inline del bloque: variables CSS + margenes/fondo. Solo valores ya saneados.</summary>
    public string InlineStyle()
    {
        if (IsEmpty) { return string.Empty; }
        var sb = new StringBuilder();
        void Var(string name, string? value)
        {
            if (value is not null) { sb.Append("--").Append(name).Append(':').Append(value).Append(';'); }
        }
        static string? Px(int? v) => v is int n ? n.ToString(CultureInfo.InvariantCulture) + "px" : null;
        Var("fs-l-size", Px(LabelSize));
        Var("fs-l-color", LabelColor);
        Var("fs-bg", BoxBg);
        Var("fs-bd-color", BorderColor);
        Var("fs-bd-w", Px(BorderWidth));
        Var("fs-rad", Px(Radius));
        Var("fs-h", Px(Height));
        Var("fs-align", Align);
        Var("fs-block-bg", BlockBg);
        Var("fs-v-size", Px(ValueSize));
        Var("fs-v-color", ValueColor);
        if (MarginTop is int mt) { sb.Append("margin-top:").Append(mt.ToString(CultureInfo.InvariantCulture)).Append("px;"); }
        if (MarginBottom is int mb) { sb.Append("margin-bottom:").Append(mb.ToString(CultureInfo.InvariantCulture)).Append("px;"); }
        return sb.ToString();
    }
}
