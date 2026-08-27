using System.Text;
using System.Text.Json;

namespace Ecorex.Application.Forms;

/// <summary>
/// Renderiza el valor de un campo Canvas (croquis, ADR-0080) a HTML de impresion. El valor puede ser:
///  - MULTIPAGINA: {"v":1,"pages":["&lt;svg...&gt;", ...]} (formato actual del editor).
///  - LEGACY: un SVG suelto ("&lt;svg...&gt;") -> 1 pagina (registros viejos siguen sirviendo).
/// Cada pagina se emite en su propia HOJA imprimible (salto de pagina CSS) con un contador
/// "Pagina X de N" (solo si hay mas de una). El SVG se emite SIN escapar (Chromium lo rinde).
/// Compartido por la ruta de plantilla (FormTemplateRenderService) y la impresion directa (FormPrint).
/// </summary>
public static class FormCanvasHtml
{
    /// <summary>Extrae las paginas (cada una un string SVG) de un valor Canvas. Lista vacia si no aplica.</summary>
    public static IReadOnlyList<string> ExtractPages(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) { return Array.Empty<string>(); }
        if (v.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)) { return new[] { v }; }
        if (v[0] == '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(v);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("pages", out var pages)
                    && pages.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var el in pages.EnumerateArray())
                    {
                        var svg = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(svg)) { list.Add(svg!); }
                    }
                    return list;
                }
            }
            catch (JsonException) { /* no es Canvas multipagina */ }
        }
        return Array.Empty<string>();
    }

    /// <summary>true si el valor es un artefacto Canvas (SVG suelto o sobre multipagina con paginas).</summary>
    public static bool IsCanvasValue(string? value) => ExtractPages(value).Count > 0;

    /// <summary>HTML de impresion del valor Canvas: una hoja por pagina, con salto de pagina y un numeral
    /// "{counterLabel} X de N" (solo si hay &gt;1 pagina). Si <paramref name="pageHeaderHtml"/> no esta vacio,
    /// se repite ese cabezote (HTML ya resuelto) ARRIBA de cada hoja. Cadena vacia si el valor no es Canvas.</summary>
    public static string Render(string? value, string? pageHeaderHtml = null, string counterLabel = "Grafico")
    {
        var pages = ExtractPages(value);
        if (pages.Count == 0) { return string.Empty; }
        var hasHeader = !string.IsNullOrWhiteSpace(pageHeaderHtml);
        var label = string.IsNullOrWhiteSpace(counterLabel) ? "Grafico" : counterLabel.Trim();
        var sb = new StringBuilder();
        for (var i = 0; i < pages.Count; i++)
        {
            // La 1a pagina no fuerza salto (ya esta en su posicion); las demas caen en hoja nueva.
            var brk = i > 0 ? "break-before:always;page-break-before:always;" : "";
            sb.Append("<div class=\"dfr-canvas-page\" style=\"").Append(brk).Append("\">");
            if (hasHeader)
            {
                // Cabezote repetido por hoja (identificacion del proyecto). Es HTML de config ya resuelto.
                sb.Append("<div class=\"dfr-canvas-hd\" style=\"font-size:11px;color:#333;border-bottom:1px solid #bbb;padding-bottom:3px;margin-bottom:4px;\">")
                  .Append(pageHeaderHtml).Append("</div>");
            }
            sb.Append(pages[i]);
            if (pages.Count > 1)
            {
                sb.Append("<div class=\"dfr-canvas-pageno\" style=\"text-align:right;font-size:11px;color:#555;margin-top:2px;\">")
                  .Append(label).Append(' ').Append(i + 1).Append(" de ").Append(pages.Count).Append("</div>");
            }
            sb.Append("</div>");
        }
        return sb.ToString();
    }
}
