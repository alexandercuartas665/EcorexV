using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Ecorex.Application.Confidentiality;

/// <summary>
/// Saneador de HTML por allowlist (ADR-0160). El contenido de la politica lo escribe el admin del
/// tenant pero se muestra en una pagina PUBLICA anonima (/pc/{token}); por eso se depura en el
/// GUARDADO con AngleSharp: se descartan por completo los elementos peligrosos (script/style/iframe/
/// object/embed/form/...), se "desenvuelven" los no permitidos conservando su texto, y de los
/// permitidos solo sobreviven atributos de una lista corta (y href/src con esquema http/https/mailto).
/// Asi un MarkupString en la pagina publica no puede ejecutar script inyectado (XSS almacenado).
/// </summary>
public static class ConfidentialityHtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "h1", "h2", "h3", "h4", "h5", "h6", "strong", "b", "em", "i", "u", "s",
        "ul", "ol", "li", "a", "blockquote", "hr", "span", "div",
        "table", "thead", "tbody", "tr", "th", "td", "pre", "code", "img"
    };

    private static readonly HashSet<string> DroppedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "link", "meta", "base",
        "form", "input", "button", "textarea", "select", "svg", "math", "noscript", "template"
    };

    private static readonly HashSet<string> AllowedAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "src", "alt", "title", "target", "rel", "colspan", "rowspan", "width", "height"
    };

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) { return string.Empty; }

        var doc = new HtmlParser().ParseDocument($"<body>{html}</body>");
        var body = doc.Body;
        if (body is null) { return string.Empty; }

        CleanChildren(body);
        return body.InnerHtml;
    }

    private static void CleanChildren(IElement parent)
    {
        foreach (var node in parent.ChildNodes.ToArray())
        {
            switch (node)
            {
                case IElement el:
                    var tag = el.LocalName;
                    if (DroppedTags.Contains(tag))
                    {
                        el.Remove();
                        continue;
                    }
                    CleanChildren(el);
                    if (!AllowedTags.Contains(tag))
                    {
                        Unwrap(el);
                        continue;
                    }
                    StripAttributes(el);
                    break;

                case IComment:
                    node.Parent?.RemoveChild(node);
                    break;
            }
        }
    }

    private static void Unwrap(IElement el)
    {
        var parent = el.Parent;
        if (parent is null) { el.Remove(); return; }
        while (el.FirstChild is { } child)
        {
            parent.InsertBefore(child, el);
        }
        el.Remove();
    }

    private static void StripAttributes(IElement el)
    {
        foreach (var attr in el.Attributes.ToArray())
        {
            var name = attr.Name;
            if (!AllowedAttrs.Contains(name))
            {
                el.RemoveAttribute(name);
                continue;
            }
            var isSrc = name.Equals("src", StringComparison.OrdinalIgnoreCase);
            if ((name.Equals("href", StringComparison.OrdinalIgnoreCase) || isSrc)
                && !IsSafeUrl(attr.Value, allowDataImage: isSrc))
            {
                el.RemoveAttribute(name);
            }
        }
    }

    private static bool IsSafeUrl(string? value, bool allowDataImage)
    {
        if (string.IsNullOrWhiteSpace(value)) { return false; }
        var v = value.Trim();
        // Relativas (sin esquema) son seguras; con esquema solo http/https/mailto.
        if (v.StartsWith('/') || v.StartsWith('#') || v.StartsWith('.')) { return true; }
        // Imagenes embebidas en base64 (data:image/...) SOLO en src: no ejecutan script y permiten
        // pegar HTML con imagenes incrustadas. data:text/html y demas quedan prohibidos (XSS).
        if (allowDataImage && v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) { return true; }
        return v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }
}
