using System.Net;
using System.Text.RegularExpressions;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Formato de texto de WhatsApp a HTML para las VISTAS PREVIAS (ADR-0186): *negrita*, _cursiva_, ~tachado~ y
/// ```monoespaciado```, con las mismas reglas que la app (el marcador va pegado al texto y no dentro de una palabra,
/// asi snake_case o 2*3*4 no se formatean). Primero codifica el HTML: el resultado es seguro para MarkupString.
/// </summary>
public static class WhatsAppTextFormat
{
    private static readonly Regex Mono = new(@"```(.+?)```", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Bold = Marker(@"\*");
    private static readonly Regex Italic = Marker("_");
    private static readonly Regex Strike = Marker("~");

    // Marcador de apertura sin letra/numero antes, contenido sin saltos que no empieza ni termina en espacio, y
    // marcador de cierre sin letra/numero despues.
    private static Regex Marker(string m)
        => new(@"(?<![\p{L}\p{N}" + m + @"])" + m + @"(?=\S)([^\n]*?\S)" + m + @"(?![\p{L}\p{N}" + m + @"])", RegexOptions.Compiled);

    public static string ToHtml(string? text)
    {
        var html = WebUtility.HtmlEncode(text ?? string.Empty);
        html = Mono.Replace(html, "<code>$1</code>");
        html = Bold.Replace(html, "<strong>$1</strong>");
        html = Italic.Replace(html, "<em>$1</em>");
        html = Strike.Replace(html, "<s>$1</s>");
        return html;
    }
}
