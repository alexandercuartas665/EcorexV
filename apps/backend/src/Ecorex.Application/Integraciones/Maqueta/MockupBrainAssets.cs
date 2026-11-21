namespace Ecorex.Application.Integraciones.Maqueta;

/// <summary>Adjunto (imagen o PDF) del cerebro digital de un proyecto de maqueta.</summary>
/// <param name="Url">URL del gestor de archivos (/archivo/{id}); exige sesion del tenant.</param>
/// <param name="UsedIn">Titulos de las notas que lo embeben (solo si se pidio el uso; si no, vacio).</param>
public sealed record MockupBrainAssetDto(Guid Id, string Name, string ContentType, long SizeBytes, string Url,
    IReadOnlyList<string>? UsedIn = null)
{
    public bool IsImage => ContentType.StartsWith("image/", StringComparison.Ordinal);
}

/// <summary>
/// Reglas PURAS de los adjuntos del cerebro (ADR-0183, ola 3b). Viven en el gestor unificado de archivos (modulo
/// <see cref="Module"/>, Ref1 = id del proyecto, nombre = el que usa Obsidian en <c>![[nombre]]</c>). Como
/// <c>/archivo/{id}</c> sirve INLINE en el mismo origen de la app, solo se aceptan formatos sin contenido activo
/// (imagenes rasterizadas y PDF) y se verifica la firma de los bytes: nada de SVG ni HTML.
/// </summary>
public static class MockupBrainAssets
{
    public const string Module = "maqueta-cerebro";
    public const int MaxAssetBytes = 10 * 1024 * 1024;
    public const int MaxAssetsPerImport = 2_000;
    public const int MaxNameLength = 150;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".pdf"] = "application/pdf"
    };

    /// <summary>Content-type permitido por la extension, o null si no se acepta (svg, html, docx...).</summary>
    public static string? ContentTypeFor(string? fileName)
        => Types.TryGetValue(Path.GetExtension(fileName ?? string.Empty), out var type) ? type : null;

    public static bool IsImageName(string? fileName) => ContentTypeFor(fileName)?.StartsWith("image/", StringComparison.Ordinal) == true;

    /// <summary>La firma de los bytes coincide con el tipo declarado (evita un HTML renombrado a .png).</summary>
    public static bool MatchesSignature(byte[] bytes, string contentType)
    {
        if (bytes is null || bytes.Length < 4) { return false; }
        bool Starts(params byte[] sig) => bytes.Length >= sig.Length && sig.Select((b, i) => bytes[i] == b).All(x => x);
        return contentType switch
        {
            "image/png" => Starts(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            "image/jpeg" => Starts(0xFF, 0xD8, 0xFF),
            "image/gif" => Starts(0x47, 0x49, 0x46, 0x38),
            "image/webp" => bytes.Length >= 12 && Starts(0x52, 0x49, 0x46, 0x46) && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            "image/bmp" => Starts(0x42, 0x4D),
            "application/pdf" => Starts(0x25, 0x50, 0x44, 0x46),
            _ => false
        };
    }

    /// <summary>Nombre de archivo seguro: sin carpeta, sin caracteres de ruta/enlace, max 150 (conserva la extension).</summary>
    public static string? SafeName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/').Last()).Trim();
        if (name.Length == 0) { return null; }
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) || c is '[' or ']' or '|' or '#' or '^' ? '-' : c).ToArray());
        if (name.Length > MaxNameLength)
        {
            var ext = Path.GetExtension(name);
            name = name[..(MaxNameLength - ext.Length)] + ext;
        }
        return name;
    }

    /// <summary>Nombre libre: si ya existe (sin mayusculas), agrega " (2)", " (3)"... antes de la extension.</summary>
    public static string UniqueName(string name, ISet<string> taken)
    {
        if (!taken.Contains(name)) { return name; }
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!taken.Contains(candidate)) { return candidate; }
        }
    }

    /// <summary>
    /// Llaves (ver <see cref="Key"/>) de los adjuntos que embebe una nota: <c>![[foto.png]]</c>, <c>![[foto.png|300]]</c>
    /// y <c>![alt](ruta/foto.png)</c>. Ignora el codigo y las imagenes externas (http/https).
    /// </summary>
    public static IReadOnlyList<string> Embeds(string? body)
    {
        var text = MockupBrainLinks.StripCode(body ?? string.Empty);
        var keys = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in MockupBrainLinks.WikiLink().Matches(text))
        {
            if (m.Groups[1].Value != "!") { continue; }
            Add(MockupBrainLinks.SplitAlias(m.Groups[2].Value).Target);
        }
        foreach (System.Text.RegularExpressions.Match m in MdImagePattern.Matches(text))
        {
            var target = m.Groups[1].Value;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { continue; }
            Add(target);
        }
        return keys;

        void Add(string reference)
        {
            if (ContentTypeFor(Key(reference)) is null) { return; }
            var key = Key(reference);
            if (!keys.Contains(key)) { keys.Add(key); }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex MdImagePattern =
        new(@"!\[[^\[\]\n]*\]\(<?([^\s()<>]+)>?\)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Llave con la que se busca un adjunto desde un enlace: el nombre del archivo, sin carpeta, sin mayusculas
    /// y con los %20 decodificados (Obsidian usa el nombre; el markdown estandar puede traer ruta y espacios codificados).</summary>
    public static string Key(string? reference)
    {
        var r = (reference ?? string.Empty).Trim();
        var hash = r.IndexOfAny(new[] { '#', '?' });
        if (hash >= 0) { r = r[..hash]; }
        try { r = Uri.UnescapeDataString(r); } catch (UriFormatException) { }
        return r.Replace('\\', '/').Split('/').Last().Trim().ToLowerInvariant();
    }
}
