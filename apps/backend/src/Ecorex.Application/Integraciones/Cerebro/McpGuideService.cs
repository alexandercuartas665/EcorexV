using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Maqueta;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Integraciones.Cerebro;

/// <summary>Nota de la guia (titulo + cuerpo + enlaces salientes a otras notas).</summary>
public sealed record McpGuideNote(string Title, string Body, IReadOnlyList<string> Links);

/// <summary>Guia publicada de un tema (para listar los cerebros MCP disponibles).</summary>
public sealed record McpGuideTopicDto(string Topic, string Name, int Version);

/// <summary>Temas de las guias MCP (ADR-0211). "general" es la guia de entrada (columna null en la BD).</summary>
public static class McpGuideTopics
{
    public const string General = "general";

    /// <summary>Temas sugeridos en la consola (el servicio acepta cualquier slug valido).</summary>
    public static readonly string[] Known = { General, "flujos", "formularios" };

    /// <summary>Normaliza: null/vacio -> "general"; minusculas; solo [a-z0-9_] (1..40). Invalido -> null.</summary>
    public static string? Normalize(string? topic)
    {
        var t = (topic ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0) { return General; }
        if (t.Length > 40 || !t.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) { return null; }
        return t;
    }
}

/// <summary>Resultado de busqueda en la guia.</summary>
public sealed record McpGuideHit(string Title, string Snippet);

/// <summary>Indice de la guia: nota de entrada + titulos de todas las notas.</summary>
/// <param name="Source">"cerebro" (el sembrado de la empresa) o "plantilla" (respaldo).</param>
public sealed record McpGuideIndex(string GuideName, int Version, string Source, McpGuideNote? Entry, IReadOnlyList<string> Titles);

/// <summary>
/// Lee la GUIA de uso de los MCP (ADR-0204) para la herramienta guia_ecorex. Origen: el cerebro SEMBRADO de la
/// empresa actual (Brain.SourceTemplateId = plantilla marcada IsMcpGuide), que es lo publicado; si la empresa aun no lo
/// tiene (creada despues de publicar), RESPALDO con las notas de la plantilla, solo si ya se publico alguna vez. Solo
/// lectura; el cerebro y sus notas pasan por el filtro global de tenant (una empresa nunca lee el cerebro de otra). La
/// plantilla es metadato de plataforma (sin tenant) y aqui solo se lee la que la plataforma marco como guia.
/// </summary>
public interface IMcpGuideService
{
    /// <summary>Error legible si no hay guia publicada; si no, null.</summary>
    Task<(McpGuideIndex? Index, string? Error)> IndexAsync(CancellationToken ct = default, string? topic = null);

    Task<(McpGuideNote? Note, string? Error)> NoteAsync(string title, CancellationToken ct = default, string? topic = null);

    Task<(IReadOnlyList<McpGuideHit> Hits, string? Error)> SearchAsync(string text, int max = McpGuideService.MaxHits, CancellationToken ct = default, string? topic = null);

    /// <summary>Guias publicadas por tema (general primero).</summary>
    Task<IReadOnlyList<McpGuideTopicDto>> ListTopicsAsync(CancellationToken ct = default);
}

public sealed class McpGuideService : IMcpGuideService
{
    public const int MaxHits = 20;

    /// <summary>Titulo de la nota de entrada (se compara normalizado).</summary>
    public const string EntryTitle = "00 - LEEME PRIMERO";

    public const string NotPublishedMessage =
        "La guia de los MCP aun no esta publicada por la plataforma. Usa las descripciones de las herramientas (tools/list).";

    private readonly IApplicationDbContext _db;

    public McpGuideService(IApplicationDbContext db) => _db = db;

    public async Task<(McpGuideIndex? Index, string? Error)> IndexAsync(CancellationToken ct = default, string? topic = null)
    {
        var (guide, error) = await LoadAsync(topic, ct);
        if (guide is null) { return (null, error); }
        var entryKey = MockupBrainLinks.NormalizeTitle(EntryTitle);
        var entry = guide.Notes.FirstOrDefault(n => n.Key == entryKey)
                    ?? guide.Notes.FirstOrDefault(n => n.Title.StartsWith("00", StringComparison.Ordinal));
        return (new McpGuideIndex(guide.Name, guide.Version, guide.Source, entry is null ? null : ToNote(entry),
            guide.Notes.Select(n => n.Title).ToList()), null);
    }

    public async Task<(McpGuideNote? Note, string? Error)> NoteAsync(string title, CancellationToken ct = default, string? topic = null)
    {
        var (guide, error) = await LoadAsync(topic, ct);
        if (guide is null) { return (null, error); }
        var key = MockupBrainLinks.NormalizeTitle(title);
        var hit = guide.Notes.FirstOrDefault(n => n.Key == key);
        if (hit is null)
        {
            // Tolerancia: titulo parcial unico (ej. "R05" o "crear un contenedor").
            var partial = guide.Notes.Where(n => n.Key.Contains(key, StringComparison.Ordinal)).Take(2).ToList();
            hit = partial.Count == 1 ? partial[0] : null;
        }
        return hit is null
            ? (null, $"No hay una nota '{title}' en la guia{TopicSuffix(topic)}. Llama guia_ecorex{(McpGuideTopics.Normalize(topic) is { } k && k != McpGuideTopics.General ? $"(tema=\"{k}\")" : " sin argumentos")} para ver los titulos.")
            : (ToNote(hit), null);
    }

    public async Task<(IReadOnlyList<McpGuideHit> Hits, string? Error)> SearchAsync(string text, int max = MaxHits, CancellationToken ct = default, string? topic = null)
    {
        var (guide, error) = await LoadAsync(topic, ct);
        if (guide is null) { return (Array.Empty<McpGuideHit>(), error); }
        var q = (text ?? string.Empty).Trim();
        if (q.Length == 0) { return (Array.Empty<McpGuideHit>(), "Indica el texto a buscar."); }
        var take = Math.Clamp(max, 1, MaxHits);
        var hits = guide.Notes
            .Select(n => new { n, inTitle = n.Title.Contains(q, StringComparison.OrdinalIgnoreCase), at = n.Body.IndexOf(q, StringComparison.OrdinalIgnoreCase) })
            .Where(x => x.inTitle || x.at >= 0)
            .OrderByDescending(x => x.inTitle)
            .ThenBy(x => x.n.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(x => new McpGuideHit(x.n.Title, Fragment(x.n.Body, x.at, q.Length)))
            .ToList();
        return (hits, null);
    }

    // ------------------------------------------------------------------ carga

    private sealed record GuideNote(string Title, string Key, string Body);

    private sealed record Guide(string Name, int Version, string Source, IReadOnlyList<GuideNote> Notes);

    public async Task<IReadOnlyList<McpGuideTopicDto>> ListTopicsAsync(CancellationToken ct = default)
    {
        var rows = await _db.BrainTemplates.AsNoTracking()
            .Where(t => t.IsMcpGuide && !t.IsArchived && t.PublishedVersion > 0)
            .Select(t => new { t.Name, t.PublishedVersion, t.McpGuideTopic })
            .ToListAsync(ct);
        return rows
            .Select(r => new McpGuideTopicDto(McpGuideTopics.Normalize(r.McpGuideTopic) ?? McpGuideTopics.General, r.Name, r.PublishedVersion))
            .GroupBy(r => r.Topic).Select(g => g.First())
            .OrderBy(r => r.Topic == McpGuideTopics.General ? 0 : 1).ThenBy(r => r.Topic, StringComparer.Ordinal)
            .ToList();
    }

    private static string TopicSuffix(string? topic)
        => McpGuideTopics.Normalize(topic) is { } k && k != McpGuideTopics.General ? $" de {k}" : string.Empty;

    private async Task<(Guide? Guide, string? Error)> LoadAsync(string? topic, CancellationToken ct)
    {
        var key = McpGuideTopics.Normalize(topic);
        if (key is null) { return (null, "Tema no valido: usa minusculas (ej. flujos, formularios)."); }
        var candidates = await _db.BrainTemplates.AsNoTracking()
            .Where(t => t.IsMcpGuide && !t.IsArchived)
            .Select(t => new { t.Id, t.Name, t.PublishedVersion, t.McpGuideTopic })
            .ToListAsync(ct);
        var template = candidates.FirstOrDefault(t => (McpGuideTopics.Normalize(t.McpGuideTopic) ?? McpGuideTopics.General) == key);
        if (template is null || template.PublishedVersion <= 0)
        {
            if (key == McpGuideTopics.General) { return (null, NotPublishedMessage); }
            var topics = (await ListTopicsAsync(ct)).Select(t => t.Topic).ToList();
            return (null, $"No hay guia publicada para el tema '{key}'. Temas disponibles: {(topics.Count > 0 ? string.Join(", ", topics) : "ninguno")}.");
        }

        // 1) Cerebro sembrado en ESTA empresa (filtro global de tenant + borrado logico).
        var brain = await _db.Brains.AsNoTracking()
            .Where(b => b.SourceTemplateId == template.Id)
            .Select(b => new { b.Id, b.TemplateVersion })
            .FirstOrDefaultAsync(ct);
        if (brain is not null)
        {
            var notes = await _db.MockupNotes.AsNoTracking()
                .Where(n => n.BrainId == brain.Id)
                .OrderBy(n => n.Title)
                .Select(n => new GuideNote(n.Title, n.TitleKey, n.Body))
                .ToListAsync(ct);
            if (notes.Count > 0) { return (new Guide(template.Name, brain.TemplateVersion ?? template.PublishedVersion, "cerebro", notes), null); }
        }

        // 2) Respaldo: la plantilla publicada (empresa creada despues de la ultima publicacion).
        var fromTemplate = await _db.BrainTemplateNotes.AsNoTracking()
            .Where(n => n.TemplateId == template.Id)
            .OrderBy(n => n.Title)
            .Select(n => new GuideNote(n.Title, n.TitleKey, n.Body))
            .ToListAsync(ct);
        return fromTemplate.Count == 0
            ? (null, NotPublishedMessage)
            : (new Guide(template.Name, template.PublishedVersion, "plantilla", fromTemplate), null);
    }

    private static McpGuideNote ToNote(GuideNote n)
        => new(n.Title, n.Body, MockupBrainLinks.Parse(n.Body)
            .Where(l => l.Kind == MockupBrainLinkKinds.Note)
            .Select(l => l.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());

    /// <summary>Fragmento alrededor de la coincidencia (o el inicio si solo coincide el titulo).</summary>
    private static string Fragment(string body, int at, int len)
    {
        if (at < 0) { return MockupBrainLinks.Snippet(body, 200); }
        var start = Math.Max(0, at - 120);
        var end = Math.Min(body.Length, at + len + 160);
        var s = body[start..end].Replace('\n', ' ').Replace('\r', ' ').Trim();
        return (start > 0 ? "..." : string.Empty) + s + (end < body.Length ? "..." : string.Empty);
    }
}
