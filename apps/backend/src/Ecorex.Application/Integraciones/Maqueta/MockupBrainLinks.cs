using System.Text;
using System.Text.RegularExpressions;

namespace Ecorex.Application.Integraciones.Maqueta;

/// <summary>Tipos de destino de un enlace del cerebro digital (ADR-0183).</summary>
public static class MockupBrainLinkKinds
{
    public const string Note = "note";
    public const string Page = "page";
    public const string Task = "task";
    public const string Tag = "tag";
}

/// <summary>Enlace encontrado en el cuerpo de una nota.</summary>
/// <param name="Kind">note | page | task | tag.</param>
/// <param name="Key">Llave de resolucion: titulo normalizado, archivo .html, numero T00000 o etiqueta en minusculas.</param>
/// <param name="Label">Como se escribio el destino (titulo original, archivo, numero, etiqueta).</param>
public sealed record MockupBrainLink(string Kind, string Key, string Label);

/// <summary>
/// Parser PURO de la sintaxis del cerebro digital (ADR-0183), compatible con Obsidian:
/// <c>[[Titulo]]</c>, <c>[[Titulo|texto visible]]</c>, <c>[[Titulo#Seccion]]</c> -> nota;
/// <c>[[pantalla.html]]</c> -> pantalla de la maqueta; <c>[[T00046]]</c> -> solicitud (tarjeta);
/// <c>#etiqueta</c> -> etiqueta. Los bloques e intervalos de codigo no generan enlaces. Los embebidos de adjuntos de
/// Obsidian (<c>![[imagen.png]]</c>) se ignoran.
/// </summary>
public static partial class MockupBrainLinks
{
    public const int MaxTitleLength = 200;

    [GeneratedRegex(@"(!?)\[\[([^\[\]\r\n]+?)\]\]")]
    public static partial Regex WikiLink();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_&/#\]])#([\p{L}\p{N}_][\p{L}\p{N}_\-/]*)")]
    public static partial Regex TagPattern();

    [GeneratedRegex(@"^[Tt]\d{5}$")]
    private static partial Regex TaskNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static readonly string[] PageExtensions = { ".html", ".htm" };

    /// <summary>Extensiones que en Obsidian son adjuntos (no notas): sus enlaces se ignoran.</summary>
    private static readonly HashSet<string> AttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".pdf", ".mp3", ".mp4", ".webm", ".wav", ".ogg",
        ".m4a", ".mov", ".canvas", ".excalidraw", ".zip", ".xlsx", ".docx", ".pptx", ".csv", ".json", ".txt"
    };

    /// <summary>Llave de un titulo: espacios colapsados, sin .md final, minusculas (invariante).</summary>
    public static string NormalizeTitle(string? title)
    {
        var t = Spaces().Replace((title ?? string.Empty).Trim(), " ");
        if (t.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { t = t[..^3].TrimEnd(); }
        return t.ToLowerInvariant();
    }

    /// <summary>Titulo limpio para guardar (espacios colapsados, max 200) o el error de validacion.</summary>
    public static (string? Title, string? Error) CleanTitle(string? title)
    {
        var t = Spaces().Replace((title ?? string.Empty).Trim(), " ");
        if (t.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { t = t[..^3].TrimEnd(); }
        if (t.Length == 0) { return (null, "El titulo es obligatorio."); }
        if (t.Length > MaxTitleLength) { return (null, $"El titulo admite maximo {MaxTitleLength} caracteres."); }
        if (t.IndexOfAny(ReservedTitleChars) >= 0) { return (null, "El titulo no puede llevar [ ] | # ^ / \\ (se usan en los enlaces y carpetas)."); }
        if (TaskNumber().IsMatch(t)) { return (null, "Un titulo con forma de numero de tarjeta (T00000) se confunde con una solicitud."); }
        if (IsPageTarget(t)) { return (null, "Un titulo terminado en .html se confunde con una pantalla de la maqueta."); }
        return (t, null);
    }

    /// <summary>Caracteres que no van en un titulo: los de la sintaxis de enlaces y los separadores de carpeta.</summary>
    public static readonly char[] ReservedTitleChars = { '[', ']', '|', '#', '^', '/', '\\' };

    /// <summary>Reemplaza los caracteres reservados por guion (titulos generados: pantallas, archivos importados).</summary>
    public static string SafeTitle(string? raw)
        => new((raw ?? string.Empty).Select(c => Array.IndexOf(ReservedTitleChars, c) >= 0 ? '-' : c).ToArray());

    public static bool IsPageTarget(string target)
        => PageExtensions.Any(e => target.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Clasifica el destino de un <c>[[...]]</c> (sin alias). Null si no es enlazable (adjunto, vacio).</summary>
    public static MockupBrainLink? Classify(string rawTarget)
    {
        var target = (rawTarget ?? string.Empty).Trim();
        var hash = target.IndexOf('#');
        if (hash >= 0) { target = target[..hash].TrimEnd(); }
        if (target.Length == 0) { return null; }

        if (TaskNumber().IsMatch(target)) { return new MockupBrainLink(MockupBrainLinkKinds.Task, target.ToUpperInvariant(), target.ToUpperInvariant()); }
        if (IsPageTarget(target))
        {
            var page = target.Replace('\\', '/').TrimStart('/');
            return page.Length > MaxTitleLength ? null : new MockupBrainLink(MockupBrainLinkKinds.Page, page, page);
        }

        var ext = Path.GetExtension(target);
        if (ext.Length > 0 && AttachmentExtensions.Contains(ext)) { return null; }

        // Obsidian admite rutas "carpeta/Nota": la nota se identifica por su nombre.
        var slash = target.LastIndexOf('/');
        if (slash >= 0) { target = target[(slash + 1)..].Trim(); }
        var key = NormalizeTitle(target);
        if (key.Length == 0 || key.Length > MaxTitleLength) { return null; }
        var label = Spaces().Replace(target, " ");
        if (label.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { label = label[..^3].TrimEnd(); }
        return new MockupBrainLink(MockupBrainLinkKinds.Note, key, label);
    }

    /// <summary>Todos los enlaces distintos (por tipo y llave) del cuerpo, en orden de aparicion.</summary>
    public static IReadOnlyList<MockupBrainLink> Parse(string? body)
    {
        var result = new List<MockupBrainLink>();
        var seen = new HashSet<(string, string)>();
        var text = StripCode(body ?? string.Empty);

        foreach (Match m in WikiLink().Matches(text))
        {
            if (m.Groups[1].Value == "!") { continue; }
            var inner = m.Groups[2].Value;
            var pipe = inner.IndexOf('|');
            var link = Classify(pipe >= 0 ? inner[..pipe] : inner);
            if (link is not null && seen.Add((link.Kind, link.Key))) { result.Add(link); }
        }

        // Las etiquetas se buscan fuera de los [[...]] (un "#" dentro del enlace es una seccion).
        var withoutLinks = WikiLink().Replace(text, " ");
        foreach (Match m in TagPattern().Matches(withoutLinks))
        {
            var tag = m.Groups[1].Value.TrimEnd('/', '-');
            if (tag.Length == 0 || tag.All(char.IsDigit) || tag.Length > 100) { continue; }
            var key = tag.ToLowerInvariant();
            if (seen.Add((MockupBrainLinkKinds.Tag, key))) { result.Add(new MockupBrainLink(MockupBrainLinkKinds.Tag, key, tag)); }
        }
        return result;
    }

    /// <summary>
    /// Reemplaza los enlaces a la nota <paramref name="oldKey"/> por <paramref name="newTitle"/>, conservando la seccion
    /// (<c>#...</c>) y el alias (<c>|...</c>). No toca los bloques de codigo. Devuelve el cuerpo (igual si no hubo cambios).
    /// </summary>
    public static string RewriteNoteLinks(string body, string oldKey, string newTitle)
    {
        if (string.IsNullOrEmpty(body)) { return body ?? string.Empty; }
        var code = CodeRanges(body);
        return WikiLink().Replace(body, m =>
        {
            if (m.Groups[1].Value == "!" || code.Any(r => m.Index >= r.Start && m.Index < r.End)) { return m.Value; }
            var inner = m.Groups[2].Value;
            var pipe = inner.IndexOf('|');
            var target = pipe >= 0 ? inner[..pipe] : inner;
            var link = Classify(target);
            if (link is null || link.Kind != MockupBrainLinkKinds.Note || link.Key != oldKey) { return m.Value; }
            var hash = target.IndexOf('#');
            var section = hash >= 0 ? target[hash..] : string.Empty;
            var alias = pipe >= 0 ? inner[pipe..] : string.Empty;
            return "[[" + newTitle + section + alias + "]]";
        });
    }

    /// <summary>Resumen corto (texto plano) para listas y backlinks.</summary>
    public static string Snippet(string? body, int max = 160)
    {
        var text = StripCode(body ?? string.Empty);
        var sb = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '>', '-', '*', ' ').Trim();
            if (line.Length == 0 || line == "---") { continue; }
            if (sb.Length > 0) { sb.Append(' '); }
            sb.Append(line);
            if (sb.Length >= max) { break; }
        }
        var s = WikiLink().Replace(sb.ToString(), m =>
        {
            var inner = m.Groups[2].Value;
            var pipe = inner.IndexOf('|');
            return pipe >= 0 ? inner[(pipe + 1)..] : inner;
        });
        s = s.Replace("**", string.Empty).Replace("__", string.Empty);
        return s.Length > max ? s[..max].TrimEnd() + "..." : s;
    }

    /// <summary>Quita bloques ``` y ~~~ y codigo en linea (`...`), conservando los saltos de linea.</summary>
    public static string StripCode(string body)
    {
        var ranges = CodeRanges(body);
        if (ranges.Count == 0) { return body; }
        var chars = body.ToCharArray();
        foreach (var (start, end) in ranges)
        {
            for (var i = start; i < end; i++)
            {
                if (chars[i] != '\n' && chars[i] != '\r') { chars[i] = ' '; }
            }
        }
        return new string(chars);
    }

    /// <summary>Rangos [inicio, fin) de codigo: bloques cercados (``` o ~~~) y tramos `en linea`.</summary>
    private static List<(int Start, int End)> CodeRanges(string body)
    {
        var ranges = new List<(int Start, int End)>();
        var i = 0;
        var lineStart = true;
        string? fence = null;
        var fenceStart = 0;
        while (i < body.Length)
        {
            if (lineStart)
            {
                var j = i;
                while (j < body.Length && (body[j] == ' ' || body[j] == '\t') && j - i < 4) { j++; }
                var marker = j + 3 <= body.Length ? body.Substring(j, 3) : string.Empty;
                if (marker is "```" or "~~~")
                {
                    if (fence is null)
                    {
                        fence = marker;
                        fenceStart = i;
                    }
                    else if (marker == fence)
                    {
                        var eol = body.IndexOf('\n', j);
                        var end = eol < 0 ? body.Length : eol;
                        ranges.Add((fenceStart, end));
                        fence = null;
                        i = end;
                        continue;
                    }
                }
            }

            var c = body[i];
            if (fence is null && c == '`')
            {
                var close = body.IndexOf('`', i + 1);
                var eol = body.IndexOf('\n', i + 1);
                if (close > i && (eol < 0 || close < eol))
                {
                    ranges.Add((i, close + 1));
                    i = close + 1;
                    lineStart = false;
                    continue;
                }
            }
            lineStart = c == '\n';
            i++;
        }
        if (fence is not null) { ranges.Add((fenceStart, body.Length)); }
        return ranges;
    }
}
