using System.Net;
using System.Text.RegularExpressions;

namespace Ecorex.SuperAdmin.Maqueta;

/// <summary>Pantalla de la maqueta para el selector del editor (ADR-0180).</summary>
/// <param name="File">Ruta relativa a la raiz de la version, con '/'.</param>
/// <param name="Title">meta maq-titulo, o el &lt;title&gt;, o el nombre del archivo.</param>
/// <param name="Module">meta maq-modulo, o la carpeta, o vacio.</param>
public sealed record MockupPage(string File, string Title, string Module);

/// <summary>Lo que el editor necesita de una version servida: sus pantallas y si trae manifiesto de temas.</summary>
/// <param name="HasManifest">La raiz trae anotador.config.js (window.ANOTADOR_MANIFEST: temas, nombre).</param>
public sealed record MockupSiteInfo(IReadOnlyList<MockupPage> Pages, bool HasManifest);

/// <summary>
/// Recorre la carpeta de una maqueta (version descomprimida o carpeta local de dev) y arma la lista de pantallas
/// .html para el editor del sistema (ADR-0180). Ignora ocultos, el kit anotador/ heredado, vendor/node_modules y las
/// plantillas que empiezan por '_'. Lee solo la cabecera de cada pagina (titulo y metas maq-*).
/// </summary>
public static partial class MockupPages
{
    /// <summary>Tope de pantallas listadas (una maqueta normal tiene decenas).</summary>
    public const int MaxPages = 2000;

    public const string ManifestFile = "anotador.config.js";

    private static readonly string[] SkippedFolders = { "anotador", "vendor", "node_modules" };

    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitlePattern();

    [GeneratedRegex("<meta\\s+[^>]*name\\s*=\\s*[\"']maq-([a-z]+)[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaPattern();

    [GeneratedRegex("content\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex ContentPattern();

    public static MockupSiteInfo Scan(string root)
    {
        var full = Path.GetFullPath(root);
        var pages = new List<MockupPage>();
        if (Directory.Exists(full))
        {
            foreach (var path in Directory.EnumerateFiles(full, "*.html", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(full, path).Replace('\\', '/');
                var segments = rel.Split('/');
                if (segments.Any(s => s.StartsWith('.') || s.StartsWith('_'))) { continue; }
                if (segments.Length > 1 && SkippedFolders.Contains(segments[0], StringComparer.OrdinalIgnoreCase)) { continue; }
                pages.Add(Read(path, rel));
                if (pages.Count >= MaxPages) { break; }
            }
        }

        var ordered = pages
            .OrderBy(p => string.Equals(p.File, "index.html", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MockupSiteInfo(ordered, File.Exists(Path.Combine(full, ManifestFile)));
    }

    /// <summary>Titulo y modulo desde la cabecera (primeros 16 KB) de la pagina.</summary>
    public static MockupPage Read(string path, string rel)
    {
        string head;
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[16 * 1024];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            head = new string(buffer, 0, read);
        }
        catch (IOException)
        {
            head = string.Empty;
        }
        return Parse(head, rel);
    }

    public static MockupPage Parse(string head, string rel)
    {
        string? metaTitle = null, metaModule = null;
        foreach (Match m in MetaPattern().Matches(head))
        {
            var content = ContentPattern().Match(m.Value);
            if (!content.Success) { continue; }
            var value = Clean(content.Groups[1].Value);
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "titulo": metaTitle ??= value; break;
                case "modulo": metaModule ??= value; break;
            }
        }
        var title = metaTitle;
        if (string.IsNullOrEmpty(title))
        {
            var t = TitlePattern().Match(head);
            title = t.Success ? Clean(t.Groups[1].Value) : null;
        }
        if (string.IsNullOrEmpty(title)) { title = rel; }
        var module = metaModule;
        if (string.IsNullOrEmpty(module)) { module = rel.Contains('/') ? rel[..rel.IndexOf('/')] : string.Empty; }
        return new MockupPage(rel, title.Length > 120 ? title[..120] : title, module.Length > 60 ? module[..60] : module);
    }

    private static string Clean(string value) => Regex.Replace(WebUtility.HtmlDecode(value), "\\s+", " ").Trim();
}
