using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>
/// Config del contenedor Tabs (FormContainer.TabsJson). SIN migracion: TabsJson admite DOS formas, para no
/// romper los formularios existentes ni tocar el ModelSnapshot:
///   - LEGACY (arreglo de strings):  ["Paso 1","Paso 2"]                 -> presentacion "tabs" (pestanas).
///   - OBJETO:                        {"tabs":["Paso 1"],"presentation":"wizard"} -> pestanas o asistente.
/// Un parser unico que usan el renderer y el disenador. Default = "tabs" (comportamiento actual).
/// </summary>
public sealed record FormTabsConfig(IReadOnlyList<string> Tabs, string Presentation)
{
    public const string PresentationTabs = "tabs";
    public const string PresentationWizard = "wizard";

    public bool IsWizard => string.Equals(Presentation, PresentationWizard, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lee TabsJson en cualquiera de las dos formas. Tolerante: vacio/invalido -> ([], "tabs").</summary>
    public static FormTabsConfig Parse(string? tabsJson)
    {
        if (string.IsNullOrWhiteSpace(tabsJson)) { return new FormTabsConfig(Array.Empty<string>(), PresentationTabs); }
        try
        {
            using var doc = JsonDocument.Parse(tabsJson);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return new FormTabsConfig(ReadStrings(root), PresentationTabs);
            }
            if (root.ValueKind == JsonValueKind.Object)
            {
                var tabs = root.TryGetProperty("tabs", out var t) && t.ValueKind == JsonValueKind.Array
                    ? ReadStrings(t) : Array.Empty<string>();
                var pres = root.TryGetProperty("presentation", out var p) && p.ValueKind == JsonValueKind.String
                    ? NormalizePresentation(p.GetString()) : PresentationTabs;
                return new FormTabsConfig(tabs, pres);
            }
        }
        catch (JsonException) { /* cae al default */ }
        return new FormTabsConfig(Array.Empty<string>(), PresentationTabs);
    }

    /// <summary>Serializa a TabsJson. "tabs" -> arreglo legacy (round-trip estable); "wizard" -> objeto.</summary>
    public static string Serialize(IReadOnlyList<string> tabs, string presentation)
    {
        var names = tabs ?? Array.Empty<string>();
        if (string.Equals(NormalizePresentation(presentation), PresentationWizard, StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new { tabs = names, presentation = PresentationWizard });
        }
        return JsonSerializer.Serialize(names);
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement arr)
    {
        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            list.Add(el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : "");
        }
        return list;
    }

    private static string NormalizePresentation(string? p)
        => string.Equals(p?.Trim(), PresentationWizard, StringComparison.OrdinalIgnoreCase) ? PresentationWizard : PresentationTabs;
}
