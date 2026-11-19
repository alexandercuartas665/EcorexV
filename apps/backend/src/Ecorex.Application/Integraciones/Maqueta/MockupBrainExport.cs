using System.IO.Compression;
using System.Text;

namespace Ecorex.Application.Integraciones.Maqueta;

/// <summary>Nota a exportar (titulo y cuerpo markdown tal cual se guardan).</summary>
public sealed record MockupBrainExportNote(string Title, string Body, DateTimeOffset UpdatedAt);

/// <summary>Adjunto a exportar (nombre del gestor de archivos y sus bytes).</summary>
public sealed record MockupBrainExportAsset(string Name, byte[] Content);

/// <summary>ZIP del vault ya armado.</summary>
/// <param name="Renamed">Notas cuyo archivo no pudo llamarse igual que el titulo (caracteres invalidos en Windows o
/// repetidos): "titulo -> archivo". Sus <c>[[enlaces]]</c> se reescribieron en el ZIP para que Obsidian los resuelva.</param>
public sealed record MockupBrainExportZip(byte[] Zip, int Notes, int Assets, IReadOnlyList<string> Renamed);

/// <summary>
/// Exportar el cerebro como vault de Obsidian (ADR-0183, v0.16.352): la vuelta de la importacion. Puro (sin BD) para
/// probarlo con un ida y vuelta contra <see cref="MockupBrainService.ReadVault"/>.
/// Estructura: <c>{slug}/{Titulo}.md</c> (el titulo es el nombre del archivo, asi enlaza Obsidian) y
/// <c>{slug}/adjuntos/{nombre}</c> (Obsidian resuelve <c>![[nombre]]</c> por nombre en todo el vault).
/// </summary>
public static class MockupBrainExport
{
    public const string AssetsFolder = "adjuntos";

    /// <summary>Caracteres que el titulo admite pero un nombre de archivo de Windows no.</summary>
    private static readonly char[] InvalidFileChars = { ':', '*', '?', '"', '<', '>' };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Nombre de archivo (sin .md) para el titulo: igual al titulo salvo caracteres que Windows no acepta.</summary>
    public static string FileTitle(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title)
        {
            sb.Append(char.IsControl(ch) || Array.IndexOf(InvalidFileChars, ch) >= 0 ? '-' : ch);
        }
        var name = sb.ToString().Trim().TrimEnd('.', ' ');
        if (name.Length == 0) { name = "nota"; }
        if (ReservedNames.Contains(name)) { name += " (nota)"; }
        return name;
    }

    /// <summary>Arma el ZIP. Notas en orden de titulo; los adjuntos con nombre repetido (sin mayusculas) se omiten.</summary>
    public static MockupBrainExportZip Build(string slug, IReadOnlyList<MockupBrainExportNote> notes, IReadOnlyList<MockupBrainExportAsset> assets,
        IReadOnlyList<string>? warnings = null)
    {
        var root = string.IsNullOrWhiteSpace(slug) ? "cerebro" : slug.Trim();
        var ordered = notes.OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToList();

        // 1) Nombre de archivo por nota (unico sin mayusculas, como en Windows/macOS).
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(MockupBrainExportNote Note, string File)>(ordered.Count);
        var renamed = new List<string>();
        foreach (var n in ordered)
        {
            var baseName = FileTitle(n.Title);
            var file = baseName;
            for (var i = 2; !taken.Add(file); i++) { file = $"{baseName} ({i})"; }
            files.Add((n, file));
            if (!string.Equals(file, n.Title, StringComparison.Ordinal)) { renamed.Add($"{n.Title} -> {file}.md"); }
        }

        // 2) Si una nota cambio de nombre, los [[enlaces]] que la nombran pasan al nombre del archivo.
        var bodies = files.Select(f => f.Note.Body.Replace("\r\n", "\n")).ToArray();
        foreach (var (note, file) in files.Where(f => !string.Equals(f.File, f.Note.Title, StringComparison.Ordinal)))
        {
            var oldKey = MockupBrainLinks.NormalizeTitle(note.Title);
            for (var i = 0; i < bodies.Length; i++) { bodies[i] = MockupBrainLinks.RewriteNoteLinks(bodies[i], oldKey, file); }
        }

        using var ms = new MemoryStream();
        var assetCount = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < files.Count; i++)
            {
                var entry = zip.CreateEntry($"{root}/{files[i].File}.md", CompressionLevel.Optimal);
                entry.LastWriteTime = Clamp(files[i].Note.UpdatedAt);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(bodies[i]);
            }

            var assetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in assets.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!assetNames.Add(a.Name)) { continue; }
                var entry = zip.CreateEntry($"{root}/{AssetsFolder}/{a.Name}", CompressionLevel.Fastest);
                using var s = entry.Open();
                s.Write(a.Content);
                assetCount++;
            }

            var notice = new List<string>();
            if (renamed.Count > 0)
            {
                notice.Add("Notas cuyo archivo no se llama igual que su titulo (caracteres no validos en un nombre de archivo o repetidos):");
                notice.AddRange(renamed.Select(r => "- " + r));
            }
            if (warnings is { Count: > 0 })
            {
                if (notice.Count > 0) { notice.Add(string.Empty); }
                notice.Add("Avisos:");
                notice.AddRange(warnings.Select(x => "- " + x));
            }
            if (notice.Count > 0)
            {
                using var w = new StreamWriter(zip.CreateEntry($"{root}/LEEME-exportacion.txt").Open(), new UTF8Encoding(false));
                w.Write(string.Join("\n", notice) + "\n");
            }
        }
        return new MockupBrainExportZip(ms.ToArray(), files.Count, assetCount, renamed);
    }

    /// <summary>El formato ZIP solo guarda fechas entre 1980 y 2107.</summary>
    private static DateTimeOffset Clamp(DateTimeOffset d)
    {
        var min = new DateTimeOffset(1980, 1, 2, 0, 0, 0, TimeSpan.Zero);
        return d < min ? min : d;
    }
}
