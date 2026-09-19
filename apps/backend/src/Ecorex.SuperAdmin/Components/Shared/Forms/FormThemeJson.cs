using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ecorex.SuperAdmin.Components.Shared.Forms;

/// <summary>
/// Apariencia / tema del formulario (Ola 5 del FormBuilder). Helper PURO: Read/Build del theme_json + ToInnerCss
/// que lo traduce a CSS (a inyectar dentro del @scope del formulario en DynamicFormRenderer). El color de marca
/// fluye por las variables que el renderer ya usa (--brand / --brand-soft), asi que un solo par de variables
/// tema todo el formulario. El look "prototipo" (hero + rotulo) es CSS estatico activado por la clase
/// dfr-theme-prototipo; aqui solo se emiten las variables dinamicas y el ocultar chips tecnicos. Mismo patron
/// que StatusLadderJson / GestionColumnJson (testeable sin BD ni Blazor).
/// </summary>
public static class FormThemeJson
{
    /// <summary>Config de apariencia ya parseada. Color validado como hex; strings recortados.</summary>
    public sealed record ThemeSpec(string Tema, string? Color, bool Hero, string? Eyebrow, bool HideChips, bool Cards)
    {
        public bool IsPrototipo => string.Equals(Tema, "prototipo", StringComparison.OrdinalIgnoreCase);
        public static ThemeSpec Default => new("clasico", null, false, null, false, false);
    }

    // Solo se acepta un color hex (#rgb, #rrggbb, #rrggbbaa): evita inyeccion en el bloque <style>.
    private static readonly Regex HexColor = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    public static ThemeSpec Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return ThemeSpec.Default; }
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null) { return ThemeSpec.Default; }
            var tema = Str(root, "tema");
            var color = Str(root, "color");
            if (!string.IsNullOrWhiteSpace(color) && !HexColor.IsMatch(color!.Trim())) { color = null; }
            return new ThemeSpec(
                string.IsNullOrWhiteSpace(tema) ? "clasico" : tema!.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(color) ? null : color!.Trim(),
                Bool(root, "hero"),
                string.IsNullOrWhiteSpace(Str(root, "eyebrow")) ? null : Str(root, "eyebrow")!.Trim(),
                Bool(root, "hideChips"),
                Bool(root, "cards"));
        }
        catch (JsonException) { return ThemeSpec.Default; }
    }

    /// <summary>Arma el theme_json. Devuelve null cuando el tema es completamente por defecto (clasico, sin color
    /// ni flags): asi no se guarda un JSON inutil y el formulario queda "clasico".</summary>
    public static string? Build(string? tema, string? color, bool hero, string? eyebrow, bool hideChips, bool cards)
    {
        var isProto = string.Equals(tema?.Trim(), "prototipo", StringComparison.OrdinalIgnoreCase);
        var cleanColor = !string.IsNullOrWhiteSpace(color) && HexColor.IsMatch(color!.Trim()) ? color!.Trim() : null;
        var cleanEyebrow = string.IsNullOrWhiteSpace(eyebrow) ? null : eyebrow!.Trim();

        if (!isProto && cleanColor is null && !hero && cleanEyebrow is null && !hideChips && !cards)
        {
            return null;   // todo por defecto -> sin tema
        }
        var o = new JsonObject { ["tema"] = isProto ? "prototipo" : "clasico" };
        if (cleanColor is not null) { o["color"] = cleanColor; }
        if (hero) { o["hero"] = true; }
        if (cleanEyebrow is not null) { o["eyebrow"] = cleanEyebrow; }
        if (hideChips) { o["hideChips"] = true; }
        if (cards) { o["cards"] = true; }
        return o.ToJsonString();
    }

    /// <summary>CSS a colocar DENTRO del @scope del formulario (variables de marca + ocultar chips tecnicos).
    /// null si no hay nada dinamico que emitir (el look prototipo es CSS estatico por la clase).</summary>
    public static string? ToInnerCss(ThemeSpec spec)
    {
        var sb = new StringBuilder();
        if (spec.Color is { } c && HexColor.IsMatch(c))
        {
            // --brand / --brand-soft ya las consume todo el renderer (optcards, chips, headings, acentos).
            sb.Append(":scope { --brand: ").Append(c)
              .Append("; --brand-soft: color-mix(in srgb, ").Append(c).Append(" 14%, white); }\n");
        }
        if (spec.HideChips)
        {
            sb.Append(".dfr-chip-tech { display: none; }\n");
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static string? Str(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return null; }
        try { return v.GetValue<string?>(); } catch { return v.ToString(); }
    }

    private static bool Bool(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) { return false; }
        try
        {
            return v.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(v.GetValue<string>(), out var b) && b,
                _ => false
            };
        }
        catch { return false; }
    }
}
