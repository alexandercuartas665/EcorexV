using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ecorex.Application.Integraciones.Maqueta;

namespace Ecorex.SuperAdmin.Maqueta;

/// <summary>
/// Markdown -> HTML MINIMO y SANITIZADO para el cerebro digital (ADR-0183). Nada de HTML crudo del usuario: TODO el
/// texto se codifica y solo se emiten etiquetas fijas (encabezados, parrafos, listas, tareas, citas, codigo, enfasis,
/// enlaces http/https). El editor vive en el mismo origen de la app, asi que un XSS aqui seria grave.
/// Los <c>[[...]]</c> salen como <c>a.mb-wl[data-kind][data-key]</c> (el JS del cerebro intercepta el clic) y las
/// <c>#etiquetas</c> como <c>span.mb-tag</c>.
/// </summary>
public static partial class MockupMarkdown
{
    /// <summary>Dice si un destino existe (para pintar los fantasmas): (tipo, llave) -> existe.</summary>
    public delegate bool LinkExists(string kind, string key);

    /// <summary>URL de un adjunto del cerebro por su referencia (nombre o ruta), o null si no existe (ola 3b).</summary>
    public delegate string? AssetUrl(string reference);

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*([-*+])\s+\[([ xX])\]\s+(.*)$")]
    private static partial Regex TaskItem();

    [GeneratedRegex(@"^\s*([-*+])\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*(\d{1,9})[.)]\s+(.*)$")]
    private static partial Regex Ordered();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\[([^\[\]\n]+)\]\((https?://[^\s()]+)\)")]
    private static partial Regex MdLink();

    [GeneratedRegex(@"!\[([^\[\]\n]*)\]\(<?([^\s()<>]+)>?\)")]
    private static partial Regex MdImage();

    [GeneratedRegex(@"(?<![\w/""'=])https?://[^\s<>()\[\]]+[^\s<>()\[\].,;:!?""']")]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"\*\*(?=\S)(.+?)(?<=\S)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"(?<![\w*])\*(?=\S)(.+?)(?<=\S)\*(?![\w*])")]
    private static partial Regex ItalicStar();

    [GeneratedRegex(@"(?<![\w_])_(?=\S)(.+?)(?<=\S)_(?![\w_])")]
    private static partial Regex ItalicUnderscore();

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~")]
    private static partial Regex Strike();

    [GeneratedRegex(@"==(?=\S)(.+?)(?<=\S)==")]
    private static partial Regex Highlight();

    [GeneratedRegex("\u0001(\\d+)\u0002")]
    private static partial Regex Placeholder();

    public static string ToHtml(string? markdown, LinkExists? exists = null, AssetUrl? assets = null)
    {
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var html = new StringBuilder();
        var para = new List<string>();
        string? listTag = null;
        var i = 0;

        // Propiedades (frontmatter YAML de Obsidian) al inicio: se muestran como bloque discreto.
        if (lines.Length > 1 && lines[0].Trim() == "---")
        {
            var end = Array.FindIndex(lines, 1, l => l.Trim() == "---");
            if (end > 0)
            {
                html.Append("<pre class=\"mb-props\">").Append(Enc(string.Join('\n', lines[1..end]))).Append("</pre>");
                i = end + 1;
            }
        }

        void FlushPara()
        {
            if (para.Count == 0) { return; }
            html.Append("<p>").Append(string.Join("<br>", para.Select(p => Inline(p, exists, assets)))).Append("</p>");
            para.Clear();
        }

        void CloseList()
        {
            if (listTag is null) { return; }
            html.Append("</").Append(listTag).Append('>');
            listTag = null;
        }

        void OpenList(string tag, string? cls = null)
        {
            if (listTag == tag) { return; }
            CloseList();
            html.Append('<').Append(tag).Append(cls is null ? string.Empty : $" class=\"{cls}\"").Append('>');
            listTag = tag;
        }

        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                FlushPara();
                CloseList();
                var fence = trimmed[..3];
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal); i++) { code.Add(lines[i]); }
                html.Append("<pre class=\"mb-code\"><code>").Append(Enc(string.Join('\n', code))).Append("</code></pre>");
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushPara();
                CloseList();
                continue;
            }

            if (Rule().IsMatch(line))
            {
                FlushPara();
                CloseList();
                html.Append("<hr>");
                continue;
            }

            var h = Heading().Match(line);
            if (h.Success)
            {
                FlushPara();
                CloseList();
                var level = h.Groups[1].Value.Length;
                html.Append("<h").Append(level).Append('>').Append(Inline(h.Groups[2].Value, exists, assets)).Append("</h").Append(level).Append('>');
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushPara();
                CloseList();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('>'); i++)
                {
                    var q = lines[i].TrimStart()[1..];
                    quote.Add(q.StartsWith(' ') ? q[1..] : q);
                }
                i--;
                // Callout de Obsidian: > [!nota] Titulo
                var first = quote.Count > 0 ? quote[0] : string.Empty;
                var callout = Regex.Match(first, @"^\[!(\w+)\][+-]?\s*(.*)$");
                if (callout.Success)
                {
                    html.Append("<div class=\"mb-callout\"><div class=\"mb-callout-title\">")
                        .Append(Inline(callout.Groups[2].Value.Length > 0 ? callout.Groups[2].Value : callout.Groups[1].Value, exists, assets))
                        .Append("</div>").Append(ToHtml(string.Join('\n', quote.Skip(1)), exists, assets)).Append("</div>");
                }
                else
                {
                    html.Append("<blockquote>").Append(ToHtml(string.Join('\n', quote), exists, assets)).Append("</blockquote>");
                }
                continue;
            }

            var t = TaskItem().Match(line);
            if (t.Success)
            {
                FlushPara();
                OpenList("ul");
                var done = t.Groups[2].Value is "x" or "X";
                html.Append(done ? "<li class=\"mb-task done\"><i class=\"fa-regular fa-square-check\"></i> " : "<li class=\"mb-task\"><i class=\"fa-regular fa-square\"></i> ")
                    .Append(Inline(t.Groups[3].Value, exists, assets)).Append("</li>");
                continue;
            }

            var b = Bullet().Match(line);
            if (b.Success)
            {
                FlushPara();
                OpenList("ul");
                html.Append("<li>").Append(Inline(b.Groups[2].Value, exists, assets)).Append("</li>");
                continue;
            }

            var o = Ordered().Match(line);
            if (o.Success)
            {
                FlushPara();
                OpenList("ol");
                html.Append("<li>").Append(Inline(o.Groups[2].Value, exists, assets)).Append("</li>");
                continue;
            }

            CloseList();
            para.Add(line.Trim());
        }
        FlushPara();
        CloseList();
        return html.ToString();
    }

    /// <summary>Formato en linea. Los tokens (codigo, enlaces, URLs, etiquetas) se apartan con marcadores ANTES de
    /// codificar, asi su HTML no se re-procesa ni se puede inyectar nada.</summary>
    public static string Inline(string text, LinkExists? exists = null, AssetUrl? assets = null)
    {
        var tokens = new List<string>();
        string Hold(string html)
        {
            tokens.Add(html);
            return "\u0001" + (tokens.Count - 1) + "\u0002";
        }

        var s = (text ?? string.Empty).Replace("\u0001", string.Empty).Replace("\u0002", string.Empty);
        s = CodeSpan().Replace(s, m => Hold("<code>" + Enc(m.Groups[1].Value) + "</code>"));
        s = MockupBrainLinks.WikiLink().Replace(s, m => Hold(WikiHtml(m, exists, assets)));
        s = MdImage().Replace(s, m => Hold(MdImageHtml(m.Groups[1].Value, m.Groups[2].Value, assets)));
        s = MdLink().Replace(s, m => Hold(ExternalLink(m.Groups[2].Value, m.Groups[1].Value)));
        s = BareUrl().Replace(s, m => Hold(ExternalLink(m.Value, m.Value)));
        s = MockupBrainLinks.TagPattern().Replace(s, m =>
        {
            var tag = m.Groups[1].Value.TrimEnd('/', '-');
            if (tag.Length == 0 || tag.All(char.IsDigit)) { return m.Value; }
            var rest = m.Groups[1].Value[tag.Length..];
            return Hold($"<span class=\"mb-tag\" data-kind=\"tag\" data-key=\"{Enc(tag.ToLowerInvariant())}\">#{Enc(tag)}</span>") + rest;
        });

        s = Enc(s);
        s = Bold().Replace(s, "<strong>$1</strong>");
        s = ItalicStar().Replace(s, "<em>$1</em>");
        s = ItalicUnderscore().Replace(s, "<em>$1</em>");
        s = Strike().Replace(s, "<del>$1</del>");
        s = Highlight().Replace(s, "<mark>$1</mark>");
        // Los marcadores pueden anidar (un token cuyo HTML contiene otro): se restauran hasta agotar.
        for (var guard = 0; guard < 4 && s.Contains('\u0001'); guard++)
        {
            s = Placeholder().Replace(s, m => int.TryParse(m.Groups[1].Value, out var n) && n < tokens.Count ? tokens[n] : string.Empty);
        }
        return s;
    }

    private static string WikiHtml(Match m, LinkExists? exists, AssetUrl? assets)
    {
        var inner = m.Groups[2].Value;
        var pipe = inner.IndexOf('|');
        var target = pipe >= 0 ? inner[..pipe] : inner;
        var shown = pipe >= 0 ? inner[(pipe + 1)..].Trim() : target.Trim();
        if (m.Groups[1].Value == "!")
        {
            // ![[foto.png]] o ![[foto.png|300]]: el alias numerico es el ancho (sintaxis de Obsidian).
            return EmbedHtml(target.Trim(), pipe >= 0 ? inner[(pipe + 1)..].Trim() : null, assets);
        }
        var link = MockupBrainLinks.Classify(target);
        if (link is null) { return Enc(m.Value); }
        var ok = exists?.Invoke(link.Kind, link.Key) ?? true;
        var icon = link.Kind switch
        {
            MockupBrainLinkKinds.Page => "<i class=\"fa-regular fa-window-maximize\"></i> ",
            MockupBrainLinkKinds.Task => "<i class=\"fa-regular fa-square-check\"></i> ",
            _ => string.Empty
        };
        var title = link.Kind switch
        {
            MockupBrainLinkKinds.Page => ok ? "Abrir la pantalla en la maqueta" : "Esta pantalla no esta en la version abierta",
            MockupBrainLinkKinds.Task => ok ? "Abrir la tarjeta" : "La tarjeta no existe",
            _ => ok ? "Abrir la nota" : "Nota pendiente: clic para crearla"
        };
        return $"<a href=\"#\" class=\"mb-wl k-{link.Kind}{(ok ? string.Empty : " missing")}\" data-kind=\"{link.Kind}\" data-key=\"{Enc(link.Kind == MockupBrainLinkKinds.Note ? link.Label : link.Key)}\" title=\"{title}\">{icon}{Enc(shown)}</a>";
    }

    /// <summary>Adjunto embebido: imagen -> img (src del gestor de archivos); PDF -> enlace; sin resolver -> aviso.</summary>
    private static string EmbedHtml(string reference, string? alias, AssetUrl? assets)
    {
        var url = assets?.Invoke(reference);
        var name = reference.Replace('\\', '/').Split('/').Last();
        if (url is null || !url.StartsWith("/archivo/", StringComparison.Ordinal))
        {
            return $"<span class=\"mb-embed missing\" title=\"Adjunto no encontrado en el cerebro: subelo o importalo\"><i class=\"fa-regular fa-file\"></i> {Enc(name)}</span>";
        }
        if (!MockupBrainAssets.IsImageName(name))
        {
            return $"<a class=\"mb-file\" href=\"{Enc(url)}\" target=\"_blank\" rel=\"noopener\"><i class=\"fa-regular fa-file-pdf\"></i> {Enc(name)}</a>";
        }
        var width = string.Empty;
        if (alias is not null)
        {
            var w = alias.Split('x')[0];
            if (int.TryParse(w, out var px) && px is > 0 and <= 4000) { width = $" width=\"{px}\""; }
        }
        var alt = alias is not null && width.Length == 0 ? alias : name;
        return $"<img class=\"mb-img\" src=\"{Enc(url)}\" alt=\"{Enc(alt)}\" loading=\"lazy\"{width}>";
    }

    /// <summary>![alt](ruta): si la ruta es un adjunto del cerebro se embebe; una URL externa queda como enlace (no se
    /// cargan imagenes de terceros dentro de la app).</summary>
    private static string MdImageHtml(string alt, string target, AssetUrl? assets)
    {
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return ExternalLink(target, string.IsNullOrWhiteSpace(alt) ? target : alt);
        }
        return EmbedHtml(target, string.IsNullOrWhiteSpace(alt) ? null : alt, assets);
    }

    private static string ExternalLink(string url, string text)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
            ? $"<a href=\"{Enc(u.AbsoluteUri)}\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">{Enc(text)}</a>"
            : Enc(text);

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
