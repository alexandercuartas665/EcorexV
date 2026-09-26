using System.Text.Json;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Arma los parametros de los BOTONES URL DINAMICOS al ENVIAR una plantilla HSM (botones URL con sufijo
/// variable {{1}}). Puro (sin red ni EF) para poder testearlo. Recorre los botones de la plantilla EN ORDEN
/// (el indice = su posicion, como lo espera Meta); por cada boton URL con variable busca su enlace de decision
/// por el TEXTO del boton (== buttonLabel de la regla) y calcula el SUFIJO a inyectar = la URL del enlace menos
/// el prefijo fijo del boton (lo que va antes de "{{"). Los botones fijos (sin variable) NO llevan parametro:
/// Meta los pinta solos.
/// </summary>
public static class WhatsAppButtonComposer
{
    /// <summary>Devuelve un parametro por cada boton URL con variable que tenga enlace, o null si no aplica.</summary>
    public static IReadOnlyList<WhatsAppUrlButtonParam>? BuildUrlButtonParams(
        string? buttonsJson, IReadOnlyDictionary<string, string>? decisionLinksByButtonLabel)
    {
        if (string.IsNullOrWhiteSpace(buttonsJson) || decisionLinksByButtonLabel is null || decisionLinksByButtonLabel.Count == 0)
        {
            return null;
        }
        // Lookup de enlaces por etiqueta, sin importar la caja ni espacios.
        var byLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in decisionLinksByButtonLabel)
        {
            var k = (kv.Key ?? "").Trim();
            if (k.Length > 0 && !string.IsNullOrWhiteSpace(kv.Value)) { byLabel[k] = kv.Value; }
        }
        if (byLabel.Count == 0) { return null; }

        var result = new List<WhatsAppUrlButtonParam>();
        try
        {
            using var doc = JsonDocument.Parse(buttonsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return null; }
            var index = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                // Cada elemento consume una posicion (indice del boton), sea del tipo que sea.
                var current = index++;
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                var type = ReadProp(el, "type")?.Trim().ToUpperInvariant();
                var url = ReadProp(el, "url");
                if (type != "URL" || string.IsNullOrWhiteSpace(url) || !url!.Contains("{{", StringComparison.Ordinal))
                {
                    continue;   // solo botones URL con variable llevan parametro dinamico
                }
                var text = (ReadProp(el, "text") ?? "").Trim();
                if (text.Length == 0 || !byLabel.TryGetValue(text, out var decisionUrl)) { continue; }

                // Sufijo = la URL del enlace menos el prefijo fijo del boton (antes de "{{").
                var varAt = url.IndexOf("{{", StringComparison.Ordinal);
                var prefix = varAt > 0 ? url.Substring(0, varAt) : "";
                var suffix = (prefix.Length > 0 && decisionUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    ? decisionUrl.Substring(prefix.Length)
                    : decisionUrl;
                result.Add(new WhatsAppUrlButtonParam(current, suffix));
            }
        }
        catch { return null; }
        return result.Count > 0 ? result : null;
    }

    private static string? ReadProp(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            }
        }
        return null;
    }
}
