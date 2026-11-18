using System.IO.Compression;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Application.Mcp;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Integraciones.Maqueta;

/// <summary>Nota en la lista del cerebro.</summary>
public sealed record MockupNoteSummaryDto(
    Guid Id, string Title, string Snippet, DateTimeOffset UpdatedAt, string? UpdatedByName, int OutLinks, int BackLinks);

/// <summary>Enlace saliente de una nota, ya resuelto (si el destino existe).</summary>
/// <param name="Exists">La nota/tarjeta existe. Para pantallas siempre true (la UI valida contra la version).</param>
public sealed record MockupNoteLinkDto(string Kind, string Key, string Label, bool Exists, Guid? NoteId);

/// <summary>Nota que enlaza a otra (backlink).</summary>
public sealed record MockupNoteRefDto(Guid Id, string Title, string Snippet);

/// <summary>Nota completa con sus enlaces y backlinks.</summary>
public sealed record MockupNoteDto(
    Guid Id, string Title, string Body, long Version, Guid? AuthorTenantUserId, string? AuthorName, string? UpdatedByName,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? ImportedFrom,
    IReadOnlyList<MockupNoteLinkDto> Links, IReadOnlyList<MockupNoteRefDto> Backlinks);

/// <summary>Nodo del grafo. Id = "{tipo}:{llave}" (nota por id; el resto por llave).</summary>
/// <param name="Kind">note | page | task | tag | missing (nota enlazada que aun no existe).</param>
public sealed record MockupBrainNodeDto(string Id, string Kind, string Label, int Degree, Guid? NoteId, string? Key);

public sealed record MockupBrainEdgeDto(string Source, string Target);

public sealed record MockupBrainGraphDto(IReadOnlyList<MockupBrainNodeDto> Nodes, IReadOnlyList<MockupBrainEdgeDto> Edges);

/// <summary>Resultado de importar un vault de Obsidian.</summary>
public sealed record MockupBrainImportResult(int Created, int Updated, int Skipped, int IgnoredFiles, int Links, IReadOnlyList<string> Warnings);

/// <summary>
/// Cerebro digital de un proyecto de maqueta (ADR-0183): notas markdown enlazadas al estilo Obsidian, con backlinks y
/// grafo. Uno por proyecto. Cualquier usuario del tenant escribe; archiva el autor o un Owner/Admin. Los enlaces se
/// derivan del cuerpo y se reescriben en la MISMA transaccion (un SaveChanges) en que se guarda la nota.
/// </summary>
public interface IMockupBrainService
{
    Task<IReadOnlyList<MockupNoteSummaryDto>> ListNotesAsync(string slug, string? search = null, CancellationToken ct = default);

    /// <summary>Nota por id o, si no viene, por titulo (sin distinguir mayusculas). Null si no existe.</summary>
    Task<MockupNoteDto?> GetNoteAsync(string slug, Guid? id, string? title = null, CancellationToken ct = default);

    /// <summary>
    /// Crea (<paramref name="id"/> null) o actualiza una nota. Al actualizar exige la <paramref name="expectedVersion"/>
    /// leida (conflicto si otro la cambio). Si el titulo cambia, reescribe los <c>[[...]]</c> de las demas notas que
    /// apuntaban a ella.
    /// </summary>
    Task<TaskCoreResult<MockupNoteDto>> SaveNoteAsync(string slug, Guid? id, string title, string body, long? expectedVersion,
        McpActor actor, CancellationToken ct = default);

    Task<TaskCoreResult<bool>> ArchiveNoteAsync(string slug, Guid id, McpActor actor, CancellationToken ct = default);

    Task<MockupBrainGraphDto?> GetGraphAsync(string slug, CancellationToken ct = default);

    /// <summary>Importa un ZIP de vault de Obsidian (.md). Las notas con titulo existente se omiten salvo <paramref name="overwrite"/>.</summary>
    Task<TaskCoreResult<MockupBrainImportResult>> ImportVaultAsync(string slug, byte[] zip, bool overwrite, McpActor actor,
        CancellationToken ct = default);

    /// <summary>Id de la tarjeta por numero (T00046) dentro del tenant, para abrirla desde un enlace. Null si no existe.</summary>
    Task<Guid?> FindTaskIdAsync(string number, CancellationToken ct = default);
}

public sealed class MockupBrainService : IMockupBrainService
{
    public const int MaxBodyLength = 200_000;
    public const int MaxNotesPerProject = 5_000;
    public const int MaxVaultZipBytes = 30 * 1024 * 1024;
    public const int MaxVaultNotes = 3_000;
    public const int MaxLinksPerNote = 500;

    private const string AuditReason = "maqueta-cerebro";

    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public MockupBrainService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    // ------------------------------------------------------------------ lectura

    public async Task<IReadOnlyList<MockupNoteSummaryDto>> ListNotesAsync(string slug, string? search = null, CancellationToken ct = default)
    {
        var project = await FindProjectAsync(slug, ct);
        if (project is null) { return Array.Empty<MockupNoteSummaryDto>(); }

        var query = _db.MockupNotes.AsNoTracking().Where(n => n.MockupProjectId == project.Id);
        var s = (search ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length > 0)
        {
            query = query.Where(n => n.TitleKey.Contains(s) || n.Body.ToLower().Contains(s));
        }
        var notes = await query
            .OrderByDescending(n => n.UpdatedAt ?? n.CreatedAt)
            .Take(MaxNotesPerProject)
            .Select(n => new { n.Id, n.Title, n.TitleKey, n.Body, n.CreatedAt, n.UpdatedAt, n.UpdatedByName, n.AuthorName })
            .ToListAsync(ct);

        var counts = await LinkCountsAsync(project.Id, ct);
        return notes.Select(n => new MockupNoteSummaryDto(
                n.Id, n.Title, MockupBrainLinks.Snippet(n.Body), n.UpdatedAt ?? n.CreatedAt, n.UpdatedByName ?? n.AuthorName,
                counts.Out.GetValueOrDefault(n.Id), counts.Back.GetValueOrDefault(n.TitleKey)))
            .ToList();
    }

    public async Task<MockupNoteDto?> GetNoteAsync(string slug, Guid? id, string? title = null, CancellationToken ct = default)
    {
        var project = await FindProjectAsync(slug, ct);
        if (project is null) { return null; }
        MockupNote? note;
        if (id is Guid noteId)
        {
            note = await _db.MockupNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId && n.MockupProjectId == project.Id, ct);
        }
        else
        {
            var key = MockupBrainLinks.NormalizeTitle(title);
            if (key.Length == 0) { return null; }
            note = await _db.MockupNotes.AsNoTracking().FirstOrDefaultAsync(n => n.TitleKey == key && n.MockupProjectId == project.Id, ct);
        }
        return note is null ? null : await ToDtoAsync(note, ct);
    }

    public async Task<MockupBrainGraphDto?> GetGraphAsync(string slug, CancellationToken ct = default)
    {
        var project = await FindProjectAsync(slug, ct);
        if (project is null) { return null; }
        var notes = await _db.MockupNotes.AsNoTracking()
            .Where(n => n.MockupProjectId == project.Id)
            .Select(n => new { n.Id, n.Title, n.TitleKey })
            .ToListAsync(ct);
        var links = await ActiveLinksQuery(project.Id)
            .Select(l => new { l.FromNoteId, l.TargetKind, l.TargetKey, l.Label })
            .ToListAsync(ct);

        var byKey = notes.ToDictionary(n => n.TitleKey, n => n.Id);
        var taskNumbers = links.Where(l => l.TargetKind == MockupBrainLinkKinds.Task).Select(l => l.TargetKey).Distinct().ToList();
        var tasks = await TaskTitlesAsync(taskNumbers, ct);

        var nodes = new Dictionary<string, MockupBrainNodeDto>(StringComparer.Ordinal);
        foreach (var n in notes)
        {
            var nid = NoteNodeId(n.Id);
            nodes[nid] = new MockupBrainNodeDto(nid, "note", n.Title, 0, n.Id, n.TitleKey);
        }

        var edges = new HashSet<(string, string)>();
        foreach (var l in links)
        {
            string target;
            if (l.TargetKind == MockupBrainLinkKinds.Note)
            {
                if (byKey.TryGetValue(l.TargetKey, out var targetId))
                {
                    target = NoteNodeId(targetId);
                }
                else
                {
                    target = "missing:" + l.TargetKey;
                    if (!nodes.ContainsKey(target)) { nodes[target] = new MockupBrainNodeDto(target, "missing", l.Label ?? l.TargetKey, 0, null, l.TargetKey); }
                }
            }
            else
            {
                target = l.TargetKind + ":" + l.TargetKey;
                if (!nodes.ContainsKey(target))
                {
                    var label = l.TargetKind switch
                    {
                        MockupBrainLinkKinds.Task => tasks.TryGetValue(l.TargetKey, out var t) ? $"{l.TargetKey} {t}" : l.TargetKey,
                        MockupBrainLinkKinds.Tag => "#" + (l.Label ?? l.TargetKey),
                        _ => l.Label ?? l.TargetKey
                    };
                    nodes[target] = new MockupBrainNodeDto(target, l.TargetKind, label, 0, null, l.TargetKey);
                }
            }
            var source = NoteNodeId(l.FromNoteId);
            if (source != target && nodes.ContainsKey(source)) { edges.Add((source, target)); }
        }

        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (a, b) in edges)
        {
            degree[a] = degree.GetValueOrDefault(a) + 1;
            degree[b] = degree.GetValueOrDefault(b) + 1;
        }
        return new MockupBrainGraphDto(
            nodes.Values.Select(n => n with { Degree = degree.GetValueOrDefault(n.Id) }).ToList(),
            edges.Select(e => new MockupBrainEdgeDto(e.Item1, e.Item2)).ToList());
    }

    // ------------------------------------------------------------------ escritura

    public async Task<TaskCoreResult<MockupNoteDto>> SaveNoteAsync(string slug, Guid? id, string title, string body, long? expectedVersion,
        McpActor actor, CancellationToken ct = default)
    {
        var project = await FindProjectAsync(slug, ct);
        if (project is null) { return TaskCoreResult<MockupNoteDto>.NotFound($"No existe el proyecto de maqueta '{slug}'."); }
        var (cleanTitle, titleError) = MockupBrainLinks.CleanTitle(title);
        if (titleError is not null) { return TaskCoreResult<MockupNoteDto>.Invalid(titleError); }
        var cleanBody = (body ?? string.Empty).Replace("\r\n", "\n");
        if (cleanBody.Length > MaxBodyLength) { return TaskCoreResult<MockupNoteDto>.Invalid($"La nota supera {MaxBodyLength:N0} caracteres."); }
        var key = MockupBrainLinks.NormalizeTitle(cleanTitle);
        var userName = Trim150(actor.DisplayName);

        var duplicate = await _db.MockupNotes.AnyAsync(n => n.MockupProjectId == project.Id && n.TitleKey == key && n.Id != (id ?? Guid.Empty), ct);
        if (duplicate) { return TaskCoreResult<MockupNoteDto>.Conflict($"Ya existe una nota llamada '{cleanTitle}'."); }

        MockupNote note;
        string? oldKey = null;
        if (id is Guid noteId)
        {
            var found = await _db.MockupNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.MockupProjectId == project.Id, ct);
            if (found is null) { return TaskCoreResult<MockupNoteDto>.NotFound("La nota no existe o fue archivada."); }
            note = found;
            if (expectedVersion is long v && v != note.Version)
            {
                return TaskCoreResult<MockupNoteDto>.Conflict("Otra persona modifico la nota mientras la editabas. Recargala para ver sus cambios.");
            }
            // El token de concurrencia usa la version leida por el cliente (no la de esta lectura).
            if (expectedVersion is long ev) { _db.MockupNotes.Entry(note).Property(n => n.Version).OriginalValue = ev; }
            if (note.TitleKey != key) { oldKey = note.TitleKey; }
            note.Title = cleanTitle!;
            note.TitleKey = key;
            note.Body = cleanBody;
            note.UpdatedByName = userName;
        }
        else
        {
            var total = await _db.MockupNotes.CountAsync(n => n.MockupProjectId == project.Id, ct);
            if (total >= MaxNotesPerProject) { return TaskCoreResult<MockupNoteDto>.Invalid($"El cerebro ya tiene {MaxNotesPerProject:N0} notas (tope)."); }
            note = new MockupNote
            {
                TenantId = actor.TenantId,
                MockupProjectId = project.Id,
                Title = cleanTitle!,
                TitleKey = key,
                Body = cleanBody,
                AuthorTenantUserId = actor.TenantUserId,
                AuthorName = userName,
                UpdatedByName = userName
            };
            _db.MockupNotes.Add(note);
        }

        await ReplaceLinksAsync(note, project.Id, ct);

        // Renombrar: los [[Viejo]] de las demas notas pasan a [[Nuevo]] (y sus enlaces derivados se recalculan).
        var renamed = 0;
        if (oldKey is not null)
        {
            var referencing = await ActiveLinksQuery(project.Id)
                .Where(l => l.TargetKind == MockupBrainLinkKinds.Note && l.TargetKey == oldKey && l.FromNoteId != note.Id)
                .Select(l => l.FromNoteId).Distinct().ToListAsync(ct);
            if (referencing.Count > 0)
            {
                var others = await _db.MockupNotes.Where(n => referencing.Contains(n.Id)).ToListAsync(ct);
                foreach (var other in others)
                {
                    var rewritten = MockupBrainLinks.RewriteNoteLinks(other.Body, oldKey, note.Title);
                    if (rewritten == other.Body) { continue; }
                    other.Body = rewritten;
                    other.UpdatedByName = userName;
                    await ReplaceLinksAsync(other, project.Id, ct);
                    renamed++;
                }
            }
        }

        _audit.Write(actor.PlatformUserId, id is null ? "maqueta.nota.create" : "maqueta.nota.update", nameof(MockupNote), note.Id,
            previousValue: oldKey is null ? null : new { titulo = oldKey },
            newValue: new { proyecto = project.Slug, note.Title, caracteres = note.Body.Length, notasRenombradas = renamed },
            tenantId: actor.TenantId, reason: AuditReason);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<MockupNoteDto>.Conflict("Otra persona modifico la nota mientras la editabas. Recargala para ver sus cambios.");
        }
        catch (DbUpdateException)
        {
            return TaskCoreResult<MockupNoteDto>.Conflict($"Ya existe una nota llamada '{cleanTitle}'.");
        }

        var saved = await _db.MockupNotes.AsNoTracking().FirstAsync(n => n.Id == note.Id, ct);
        return TaskCoreResult<MockupNoteDto>.Ok(await ToDtoAsync(saved, ct));
    }

    public async Task<TaskCoreResult<bool>> ArchiveNoteAsync(string slug, Guid id, McpActor actor, CancellationToken ct = default)
    {
        var project = await FindProjectAsync(slug, ct);
        if (project is null) { return TaskCoreResult<bool>.NotFound($"No existe el proyecto de maqueta '{slug}'."); }
        var note = await _db.MockupNotes.FirstOrDefaultAsync(n => n.Id == id && n.MockupProjectId == project.Id, ct);
        if (note is null) { return TaskCoreResult<bool>.NotFound("La nota no existe o ya fue archivada."); }
        if (!actor.IsOwnerOrAdmin && note.AuthorTenantUserId != actor.TenantUserId)
        {
            return TaskCoreResult<bool>.Forbidden("Solo quien creo la nota o un Owner/Admin puede archivarla.");
        }

        note.IsDeleted = true;
        note.DeletedAt = DateTimeOffset.UtcNow;
        note.DeletedByTenantUserId = actor.TenantUserId;
        // Los enlaces SALIENTES son derivados: se quitan. Los ENTRANTES quedan y su destino pasa a "fantasma".
        var outgoing = await _db.MockupNoteLinks.Where(l => l.FromNoteId == note.Id).ToListAsync(ct);
        _db.MockupNoteLinks.RemoveRange(outgoing);
        _audit.Write(actor.PlatformUserId, "maqueta.nota.archive", nameof(MockupNote), note.Id,
            previousValue: null, newValue: new { proyecto = project.Slug, note.Title }, tenantId: actor.TenantId, reason: AuditReason);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<bool>.Conflict("Otra persona modifico la nota. Recargala e intenta de nuevo.");
        }
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<MockupBrainImportResult>> ImportVaultAsync(string slug, byte[] zip, bool overwrite, McpActor actor,
        CancellationToken ct = default)
    {
        var project = await FindProjectAsync(slug, ct);
        if (project is null) { return TaskCoreResult<MockupBrainImportResult>.NotFound($"No existe el proyecto de maqueta '{slug}'."); }
        var read = ReadVault(zip);
        if (read.Error is not null) { return TaskCoreResult<MockupBrainImportResult>.Invalid(read.Error); }

        var warnings = new List<string>(read.Warnings);
        var userName = Trim150(actor.DisplayName);
        var existing = await _db.MockupNotes.Where(n => n.MockupProjectId == project.Id).ToListAsync(ct);
        var byKey = existing.ToDictionary(n => n.TitleKey);
        if (existing.Count + read.Notes.Count(n => !byKey.ContainsKey(MockupBrainLinks.NormalizeTitle(n.Title))) > MaxNotesPerProject)
        {
            return TaskCoreResult<MockupBrainImportResult>.Invalid($"La importacion superaria el tope de {MaxNotesPerProject:N0} notas del cerebro.");
        }

        int created = 0, updated = 0, skipped = 0, links = 0;
        foreach (var v in read.Notes)
        {
            var key = MockupBrainLinks.NormalizeTitle(v.Title);
            MockupNote note;
            if (byKey.TryGetValue(key, out var current))
            {
                if (!overwrite) { skipped++; continue; }
                note = current;
                note.Body = v.Body;
                note.UpdatedByName = userName;
                note.ImportedFrom = v.Path;
                updated++;
            }
            else
            {
                note = new MockupNote
                {
                    TenantId = actor.TenantId,
                    MockupProjectId = project.Id,
                    Title = v.Title,
                    TitleKey = key,
                    Body = v.Body,
                    AuthorTenantUserId = actor.TenantUserId,
                    AuthorName = userName,
                    UpdatedByName = userName,
                    ImportedFrom = v.Path
                };
                _db.MockupNotes.Add(note);
                byKey[key] = note;
                created++;
            }
            links += await ReplaceLinksAsync(note, project.Id, ct);
        }

        _audit.Write(actor.PlatformUserId, "maqueta.nota.import", nameof(MockupProject), project.Id,
            previousValue: null, newValue: new { proyecto = project.Slug, created, updated, skipped, ignorados = read.IgnoredFiles, overwrite },
            tenantId: actor.TenantId, reason: AuditReason);
        try
        {
            // Un solo SaveChanges = una transaccion: o entra todo el vault o nada.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return TaskCoreResult<MockupBrainImportResult>.Conflict("Otra persona cambio el cerebro durante la importacion; intenta de nuevo.");
        }
        return TaskCoreResult<MockupBrainImportResult>.Ok(new MockupBrainImportResult(created, updated, skipped, read.IgnoredFiles, links, warnings));
    }

    public async Task<Guid?> FindTaskIdAsync(string number, CancellationToken ct = default)
    {
        var n = (number ?? string.Empty).Trim().ToUpperInvariant();
        if (n.Length == 0) { return null; }
        return await _db.TaskItems.AsNoTracking().Where(t => t.Number == n).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
    }

    // ------------------------------------------------------------------ vault de Obsidian

    /// <summary>Nota leida del vault.</summary>
    public sealed record VaultNote(string Title, string Body, string Path);

    public sealed record VaultRead(string? Error, IReadOnlyList<VaultNote> Notes, int IgnoredFiles, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Lee los .md de un ZIP de vault de Obsidian (PURO, sin BD): el titulo es el nombre del archivo (asi enlaza Obsidian).
    /// Ignora la carpeta .obsidian, ocultos, __MACOSX y los adjuntos. Si dos archivos de carpetas distintas se llaman
    /// igual, el segundo se titula "Nombre (carpeta)" y se avisa.
    /// </summary>
    public static VaultRead ReadVault(byte[] zip)
    {
        if (zip is null || zip.Length == 0) { return Fail("El archivo esta vacio."); }
        if (zip.Length > MaxVaultZipBytes) { return Fail($"El ZIP supera {MaxVaultZipBytes / (1024 * 1024)} MB."); }
        var notes = new List<VaultNote>();
        var warnings = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ignored = 0;
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count > MockupService.MaxEntries) { return Fail($"El ZIP tiene mas de {MockupService.MaxEntries} entradas."); }
            foreach (var e in archive.Entries.OrderBy(e => e.FullName.Count(c => c == '/' || c == '\\')).ThenBy(e => e.FullName, StringComparer.Ordinal))
            {
                var path = MockupService.NormalizeEntry(e.FullName);
                if (path is null) { return Fail($"Ruta no permitida en el ZIP: '{e.FullName}'."); }
                if (path.Length == 0 || path.EndsWith('/')) { continue; }
                if (MockupService.IsHiddenPath(path)) { continue; }
                if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { ignored++; continue; }
                if (notes.Count >= MaxVaultNotes) { return Fail($"El vault trae mas de {MaxVaultNotes} notas (tope por importacion)."); }
                if (e.Length > MaxBodyLength * 4L) { warnings.Add($"'{path}' es demasiado grande; se omitio."); continue; }

                string body;
                using (var reader = new StreamReader(e.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    body = reader.ReadToEnd().Replace("\r\n", "\n");
                }
                if (body.Length > MaxBodyLength) { warnings.Add($"'{path}' supera {MaxBodyLength:N0} caracteres; se omitio."); continue; }

                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var (title, error) = MockupBrainLinks.CleanTitle(name);
                if (error is not null)
                {
                    // Nombres con caracteres reservados: se limpian para no perder la nota.
                    (title, error) = MockupBrainLinks.CleanTitle(MockupBrainLinks.SafeTitle(name) + " (nota)");
                    if (error is not null) { warnings.Add($"'{path}': {error} Se omitio."); continue; }
                    warnings.Add($"'{path}' se importo como '{title}' (el nombre tenia caracteres reservados).");
                }
                if (!keys.Add(MockupBrainLinks.NormalizeTitle(title)))
                {
                    var folder = path.Contains('/') ? path[..path.LastIndexOf('/')].Split('/').Last() : "raiz";
                    var alt = MockupBrainLinks.CleanTitle($"{title} ({folder})").Title;
                    if (alt is null || !keys.Add(MockupBrainLinks.NormalizeTitle(alt)))
                    {
                        warnings.Add($"'{path}' repite el titulo '{title}'; se omitio.");
                        continue;
                    }
                    warnings.Add($"'{path}' repite el titulo '{title}'; se importo como '{alt}'.");
                    title = alt;
                }
                notes.Add(new VaultNote(title!, body, path.Length > 400 ? path[^400..] : path));
            }
        }
        catch (InvalidDataException)
        {
            return Fail("El archivo no es un ZIP valido.");
        }
        if (notes.Count == 0) { return Fail("El ZIP no trae notas .md."); }
        if (ignored > 0) { warnings.Add($"Se ignoraron {ignored} archivos que no son notas .md (imagenes, PDF, adjuntos)."); }
        return new VaultRead(null, notes, ignored, warnings);

        static VaultRead Fail(string error) => new(error, Array.Empty<VaultNote>(), 0, Array.Empty<string>());
    }

    // ------------------------------------------------------------------ helpers

    private sealed record ProjectRef(Guid Id, string Slug);

    private async Task<ProjectRef?> FindProjectAsync(string slug, CancellationToken ct)
    {
        var clean = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!MockupService.IsValidSlug(clean)) { return null; }
        return await _db.MockupProjects.AsNoTracking()
            .Where(p => p.Slug == clean && p.DeletedAt == null)
            .Select(p => new ProjectRef(p.Id, p.Slug))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Enlaces cuyo origen es una nota activa (el join aplica el filtro de tenant y de soft-delete de la nota).</summary>
    private IQueryable<MockupNoteLink> ActiveLinksQuery(Guid projectId)
        => from l in _db.MockupNoteLinks.AsNoTracking()
           where l.MockupProjectId == projectId
           join n in _db.MockupNotes.AsNoTracking() on l.FromNoteId equals n.Id
           select l;

    /// <summary>Reescribe los enlaces derivados de la nota (en el change tracker; se guardan con la nota). Devuelve cuantos.</summary>
    private async Task<int> ReplaceLinksAsync(MockupNote note, Guid projectId, CancellationToken ct)
    {
        if (_db.MockupNotes.Entry(note).State != EntityState.Added)
        {
            var old = await _db.MockupNoteLinks.Where(l => l.FromNoteId == note.Id).ToListAsync(ct);
            _db.MockupNoteLinks.RemoveRange(old);
        }
        var parsed = MockupBrainLinks.Parse(note.Body)
            .Where(l => !(l.Kind == MockupBrainLinkKinds.Note && l.Key == note.TitleKey))
            .Take(MaxLinksPerNote)
            .ToList();
        foreach (var l in parsed)
        {
            _db.MockupNoteLinks.Add(new MockupNoteLink
            {
                TenantId = note.TenantId,
                MockupProjectId = projectId,
                FromNoteId = note.Id,
                TargetKind = l.Kind,
                TargetKey = l.Key.Length > 200 ? l.Key[..200] : l.Key,
                Label = l.Label.Length > 200 ? l.Label[..200] : l.Label
            });
        }
        return parsed.Count;
    }

    private async Task<(Dictionary<Guid, int> Out, Dictionary<string, int> Back)> LinkCountsAsync(Guid projectId, CancellationToken ct)
    {
        var rows = await ActiveLinksQuery(projectId)
            .Select(l => new { l.FromNoteId, l.TargetKind, l.TargetKey })
            .ToListAsync(ct);
        var outCounts = rows.Where(r => r.TargetKind != MockupBrainLinkKinds.Tag).GroupBy(r => r.FromNoteId).ToDictionary(g => g.Key, g => g.Count());
        var back = rows.Where(r => r.TargetKind == MockupBrainLinkKinds.Note)
            .GroupBy(r => r.TargetKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FromNoteId).Distinct().Count(), StringComparer.Ordinal);
        return (outCounts, back);
    }

    private async Task<Dictionary<string, string>> TaskTitlesAsync(IReadOnlyCollection<string> numbers, CancellationToken ct)
    {
        if (numbers.Count == 0) { return new Dictionary<string, string>(StringComparer.Ordinal); }
        var rows = await _db.TaskItems.AsNoTracking()
            .Where(t => numbers.Contains(t.Number))
            .Select(t => new { t.Number, t.Title })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Number).ToDictionary(g => g.Key, g => g.First().Title, StringComparer.Ordinal);
    }

    private async Task<MockupNoteDto> ToDtoAsync(MockupNote note, CancellationToken ct)
    {
        var outgoing = MockupBrainLinks.Parse(note.Body)
            .Where(l => !(l.Kind == MockupBrainLinkKinds.Note && l.Key == note.TitleKey))
            .Take(MaxLinksPerNote)
            .ToList();
        var noteKeys = outgoing.Where(l => l.Kind == MockupBrainLinkKinds.Note).Select(l => l.Key).ToList();
        var targets = noteKeys.Count == 0
            ? new Dictionary<string, Guid>()
            : await _db.MockupNotes.AsNoTracking()
                .Where(n => n.MockupProjectId == note.MockupProjectId && noteKeys.Contains(n.TitleKey))
                .ToDictionaryAsync(n => n.TitleKey, n => n.Id, ct);
        var tasks = await TaskTitlesAsync(outgoing.Where(l => l.Kind == MockupBrainLinkKinds.Task).Select(l => l.Key).ToList(), ct);

        var links = outgoing.Select(l => l.Kind switch
        {
            MockupBrainLinkKinds.Note => new MockupNoteLinkDto(l.Kind, l.Key, l.Label, targets.ContainsKey(l.Key), targets.TryGetValue(l.Key, out var nid) ? nid : null),
            MockupBrainLinkKinds.Task => new MockupNoteLinkDto(l.Kind, l.Key, tasks.TryGetValue(l.Key, out var tt) ? $"{l.Key} {tt}" : l.Key, tasks.ContainsKey(l.Key), null),
            _ => new MockupNoteLinkDto(l.Kind, l.Key, l.Label, true, null)
        }).ToList();

        var backIds = await ActiveLinksQuery(note.MockupProjectId)
            .Where(l => l.TargetKind == MockupBrainLinkKinds.Note && l.TargetKey == note.TitleKey && l.FromNoteId != note.Id)
            .Select(l => l.FromNoteId).Distinct().ToListAsync(ct);
        var backlinks = backIds.Count == 0
            ? new List<MockupNoteRefDto>()
            : (await _db.MockupNotes.AsNoTracking()
                    .Where(n => backIds.Contains(n.Id))
                    .Select(n => new { n.Id, n.Title, n.Body })
                    .ToListAsync(ct))
                .OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
                .Select(n => new MockupNoteRefDto(n.Id, n.Title, MockupBrainLinks.Snippet(n.Body)))
                .ToList();

        return new MockupNoteDto(note.Id, note.Title, note.Body, note.Version, note.AuthorTenantUserId, note.AuthorName, note.UpdatedByName,
            note.CreatedAt, note.UpdatedAt ?? note.CreatedAt, note.ImportedFrom, links, backlinks);
    }

    private static string NoteNodeId(Guid id) => "note:" + id.ToString("N");

    private static string? Trim150(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        var t = s.Trim();
        return t.Length > 150 ? t[..150] : t;
    }
}
