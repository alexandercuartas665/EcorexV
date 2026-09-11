using System.Text.Json;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Parseo PURO (sin red) de un item de plantilla como lo devuelve YCloud en <c>items[]</c> (formato Meta:
/// name/language/status/category + components HEADER/BODY/FOOTER). Aislado del cliente HTTP para poder
/// testearlo con payloads reales. Las variables del cuerpo son POSICIONALES ({{1}}...) y sus ejemplos se
/// leen de <c>example.body_text[0]</c>.
/// </summary>
public static class YCloudTemplateParser
{
    /// <summary>Parsea un item. Devuelve null si no trae <c>name</c> (item invalido).</summary>
    public static YCloudTemplateStatus? ParseItem(JsonElement it)
    {
        var name = Str(it, "name");
        if (string.IsNullOrWhiteSpace(name)) { return null; }

        var (headerFormat, headerText, bodyText, footerText, varExamples) = ParseComponents(it);
        return new YCloudTemplateStatus(
            name!,
            Str(it, "language"),
            Str(it, "status") ?? "UNKNOWN",
            Str(it, "id"),
            Str(it, "rejectedReason") ?? Str(it, "qualityScore"),
            Str(it, "category"),
            headerFormat, headerText, bodyText, footerText, varExamples);
    }

    private static (string? headerFormat, string? headerText, string? bodyText, string? footerText, IReadOnlyList<string>? varExamples)
        ParseComponents(JsonElement it)
    {
        string? headerFormat = null, headerText = null, bodyText = null, footerText = null;
        List<string>? varExamples = null;
        if (!it.TryGetProperty("components", out var comps) || comps.ValueKind != JsonValueKind.Array)
        {
            return (null, null, null, null, null);
        }
        foreach (var c in comps.EnumerateArray())
        {
            switch (Str(c, "type")?.ToUpperInvariant())
            {
                case "HEADER":
                    headerFormat = Str(c, "format")?.ToUpperInvariant();
                    if (string.Equals(headerFormat, "TEXT", StringComparison.Ordinal)) { headerText = Str(c, "text"); }
                    break;
                case "BODY":
                    bodyText = Str(c, "text");
                    if (c.TryGetProperty("example", out var ex) && ex.ValueKind == JsonValueKind.Object
                        && ex.TryGetProperty("body_text", out var bt) && bt.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var group in bt.EnumerateArray())
                        {
                            if (group.ValueKind != JsonValueKind.Array) { continue; }
                            varExamples = new List<string>();
                            foreach (var v in group.EnumerateArray())
                            {
                                varExamples.Add(v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : v.ToString());
                            }
                            break; // solo el primer grupo de ejemplos
                        }
                    }
                    break;
                case "FOOTER":
                    footerText = Str(c, "text");
                    break;
            }
        }
        return (headerFormat, headerText, bodyText, footerText, varExamples);
    }

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
