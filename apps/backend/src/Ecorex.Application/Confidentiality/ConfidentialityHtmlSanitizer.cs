using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Ecorex.Application.Confidentiality;

/// <summary>
/// Saneador del HTML de la politica de confidencialidad (ADR-0160). El admin del tenant suele PEGAR un
/// DOCUMENTO HTML COMPLETO (con `&lt;head&gt;&lt;style&gt;...`, clases y tablas) generado por una herramienta
/// de diseno; por eso NO se usa un allowlist que borre el CSS, sino que se CONSERVA la estructura y el
/// estilo y solo se quita el contenido ACTIVO o peligroso: script/noscript, manejadores de eventos
/// (on*), esquemas javascript:/vbscript:/data:text/html en URLs, vectores historicos en CSS, y los
/// elementos de carga/accion externa (iframe/object/embed/form/input/button/base). La pagina lo pinta
/// dentro de un IFRAME con `sandbox` (sin allow-scripts): esa es la frontera de seguridad real (ningun
/// script se ejecuta aunque algo se escape); este saneo es defensa en profundidad para que el HTML
/// almacenado tampoco sea peligroso si alguna vez se rinde fuera del sandbox.
/// </summary>
public static class ConfidentialityHtmlSanitizer
{
    // Elementos de contenido activo o de carga/accion externa: se eliminan por completo.
    private static readonly HashSet<string> DroppedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "noscript", "iframe", "object", "embed", "applet",
        "form", "input", "button", "textarea", "select", "base"
    };

    // Vectores historicos que neutralizamos dentro de CSS (inline y en <style>). Muertos en navegadores
    // modernos, pero se limpian como defensa en profundidad; "</style" evita romper el bloque.
    private static readonly string[] CssBad =
    {
        "javascript:", "vbscript:", "expression(", "behavior:", "-moz-binding", "</style", "<!--", "-->"
    };

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) { return string.Empty; }

        var doc = new HtmlParser().ParseDocument(html);
        if (doc.DocumentElement is null) { return string.Empty; }

        foreach (var el in doc.All.ToArray())
        {
            // El elemento pudo haber sido removido al eliminar un ancestro.
            if (el.Parent is null && !ReferenceEquals(el, doc.DocumentElement)) { continue; }

            if (DroppedTags.Contains(el.LocalName))
            {
                el.Remove();
                continue;
            }

            foreach (var attr in el.Attributes.ToArray())
            {
                var name = attr.Name;

                // Manejadores de eventos (onclick, onload, onerror, ...).
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                {
                    el.RemoveAttribute(name);
                    continue;
                }

                // URLs con esquema peligroso.
                if (IsUrlAttr(name) && IsDangerousUrl(attr.Value))
                {
                    el.RemoveAttribute(name);
                    continue;
                }

                // CSS inline.
                if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    var clean = CleanCss(attr.Value);
                    if (clean != attr.Value) { el.SetAttribute(name, clean); }
                }
            }

            if (el.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                var clean = CleanCss(el.TextContent);
                if (clean != el.TextContent) { el.TextContent = clean; }
            }
        }

        // Documento completo (para srcdoc del iframe). Se antepone el doctype para evitar quirks mode.
        return "<!DOCTYPE html>\n" + doc.DocumentElement.OuterHtml;
    }

    private static bool IsUrlAttr(string name)
        => name.Equals("href", StringComparison.OrdinalIgnoreCase)
        || name.Equals("src", StringComparison.OrdinalIgnoreCase)
        || name.Equals("srcset", StringComparison.OrdinalIgnoreCase)
        || name.Equals("action", StringComparison.OrdinalIgnoreCase)
        || name.Equals("formaction", StringComparison.OrdinalIgnoreCase)
        || name.Equals("poster", StringComparison.OrdinalIgnoreCase)
        || name.Equals("background", StringComparison.OrdinalIgnoreCase);

    private static bool IsDangerousUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return false; }
        // Quita espacios/controles intercalados que los atacantes usan para evadir ("java\tscript:").
        var v = new string(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray())
            .ToLowerInvariant();
        return v.StartsWith("javascript:", StringComparison.Ordinal)
            || v.StartsWith("vbscript:", StringComparison.Ordinal)
            || v.StartsWith("data:text/html", StringComparison.Ordinal);
    }

    private static string CleanCss(string? css)
    {
        if (string.IsNullOrEmpty(css)) { return css ?? string.Empty; }
        var c = css;
        foreach (var bad in CssBad)
        {
            c = Regex.Replace(c, Regex.Escape(bad), string.Empty, RegexOptions.IgnoreCase);
        }
        return c;
    }
}
