using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Storage;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Integraciones.Maqueta;

/// <summary>Proyecto de maqueta para la UI y el MCP (nunca expone el id del ZIP).</summary>
/// <param name="BoardName">Tablero donde caen las solicitudes del editor (ADR-0180).</param>
/// <param name="OpenRequests">Solicitudes del editor aun abiertas (ni terminadas ni cerradas).</param>
/// <param name="TotalRequests">Solicitudes del editor en total.</param>
/// <param name="CurrentFileCount">Archivos de la version actual.</param>
public sealed record MockupProjectDto(
    Guid Id, string Slug, string Name, string? Description, int? CurrentVersionNumber, string? EntryPath,
    bool HasAnotador, int VersionCount, DateTimeOffset? LastUploadAt,
    string? BoardName = null, int OpenRequests = 0, int TotalRequests = 0, int? CurrentFileCount = null);

public sealed record MockupVersionDto(
    Guid Id, int Number, bool IsCurrent, long SizeBytes, int FileCount, string EntryPath, bool HasAnotador,
    string? Notes, string? UploadedByName, DateTimeOffset CreatedAt);

/// <summary>Solicitud (tarjeta) nacida en el editor de un proyecto de maqueta (ADR-0180).</summary>
public sealed record MockupSolicitudDto(
    Guid TaskId, string Number, string Title, TaskItemStatus Status, string? ColumnName, TaskPriority Priority,
    DateTimeOffset CreatedAt, string? Pantalla, string? Archivo, string? Selector)
{
    public bool IsOpen => Status is not (TaskItemStatus.Done or TaskItemStatus.Closed);
}

/// <summary>Lo que necesita el middleware para servir una version (resuelto ya dentro del tenant).</summary>
public sealed record MockupServeTarget(Guid TenantId, Guid VersionId, Guid ZipFileId, string? RootPrefix, string EntryPath);

/// <summary>
/// Maquetas en la nube (ADR-0179): proyectos del tenant con versiones inmutables subidas como ZIP. Las escrituras
/// (crear, subir, cambiar version actual, archivar) son solo de Owner/Admin; listar y abrir, cualquier usuario del
/// tenant. Todo corre bajo el filtro global por tenant.
/// </summary>
public interface IMockupService
{
    Task<IReadOnlyList<MockupProjectDto>> ListProjectsAsync(CancellationToken ct = default);

    /// <summary>Proyecto activo por slug (null si no existe o esta archivado).</summary>
    Task<MockupProjectDto?> GetProjectAsync(string slug, CancellationToken ct = default);

    /// <summary>Cambia nombre, descripcion y tablero destino (Owner/Admin). El slug no cambia (es la URL).</summary>
    Task<TaskCoreResult<MockupProjectDto>> UpdateProjectAsync(Guid projectId, string name, string? description, string? boardName,
        bool isOwnerOrAdmin, CancellationToken ct = default);

    /// <summary>Solicitudes creadas desde el editor del proyecto (origen <c>maqueta-{slug}</c>), mas recientes primero.</summary>
    Task<IReadOnlyList<MockupSolicitudDto>> ListSolicitudesAsync(string slug, CancellationToken ct = default);

    Task<IReadOnlyList<MockupVersionDto>> ListVersionsAsync(Guid projectId, CancellationToken ct = default);

    Task<TaskCoreResult<MockupProjectDto>> CreateProjectAsync(string name, string? slug, string? description, bool isOwnerOrAdmin, CancellationToken ct = default);

    /// <summary>Sube una version (ZIP del sitio). Queda como version ACTUAL del proyecto.</summary>
    Task<TaskCoreResult<MockupVersionDto>> UploadVersionAsync(string projectSlug, byte[] zip, string? notes, string uploaderName,
        bool isOwnerOrAdmin, CancellationToken ct = default);

    Task<TaskCoreResult<bool>> SetCurrentVersionAsync(Guid projectId, Guid versionId, bool isOwnerOrAdmin, CancellationToken ct = default);

    Task<TaskCoreResult<bool>> ArchiveProjectAsync(Guid projectId, bool isOwnerOrAdmin, CancellationToken ct = default);

    /// <summary>Version a servir: la actual, o la numero <paramref name="versionNumber"/> si se pide.</summary>
    Task<MockupServeTarget?> ResolveForServingAsync(string slug, int? versionNumber, CancellationToken ct = default);
}

public sealed partial class MockupService : IMockupService
{
    /// <summary>Modulo del gestor de archivos donde quedan los ZIP de las versiones.</summary>
    public const string FileModule = "maqueta-sitio";

    /// <summary>Topes del ZIP: comprimido, descomprimido total y cantidad de archivos.</summary>
    public const int MaxZipBytes = 100 * 1024 * 1024;
    public const long MaxUncompressedBytes = 400L * 1024 * 1024;
    public const int MaxEntries = 10_000;

    /// <summary>Kit anotador heredado dentro del ZIP. Ya no es la entrada: el editor vive en ECOREX (ADR-0180).</summary>
    public const string AnotadorEntry = "anotador/anotador.html";

    /// <summary>Prefijo del origen (ExternalTaskLink.Source) de las solicitudes del editor: maqueta-{slug}.</summary>
    public const string SourcePrefix = "maqueta-";

    /// <summary>Origen de las solicitudes del editor para un proyecto.</summary>
    public static string SourceFor(string slug) => SourcePrefix + (slug ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Tablero por defecto de un proyecto: "Maqueta {Nombre} - Ajustes" (max 150).</summary>
    public static string DefaultBoardFor(string name)
    {
        var board = $"Maqueta {(name ?? string.Empty).Trim()} - Ajustes";
        return board.Length > 150 ? board[..150] : board;
    }

    private const string AuditReason = "maqueta-nube";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex SlugPattern();

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IFileStorageService _files;
    private readonly IAuditWriter _audit;

    public MockupService(IApplicationDbContext db, ITenantContext tenant, IFileStorageService files, IAuditWriter audit)
    {
        _db = db;
        _tenant = tenant;
        _files = files;
        _audit = audit;
    }

    public static bool IsValidSlug(string? slug) => !string.IsNullOrWhiteSpace(slug) && SlugPattern().IsMatch(slug);

    /// <summary>Slug sugerido a partir del nombre (sin tildes, minusculas, guiones).</summary>
    public static string SlugFrom(string name)
    {
        var norm = (name ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in norm)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) { continue; }
            var c = char.ToLowerInvariant(ch);
            sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');
        }
        var slug = Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
        if (slug.Length > 40) { slug = slug[..40].TrimEnd('-'); }
        return slug;
    }

    public async Task<IReadOnlyList<MockupProjectDto>> ListProjectsAsync(CancellationToken ct = default)
    {
        var rows = await _db.MockupProjects.AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Name,
                p.Description,
                p.BoardName,
                p.CurrentVersionId,
                Count = p.Versions.Count,
                Last = p.Versions.Max(v => (DateTimeOffset?)v.CreatedAt)
            })
            .ToListAsync(ct);
        var currentIds = rows.Where(r => r.CurrentVersionId != null).Select(r => r.CurrentVersionId!.Value).ToList();
        var current = await _db.MockupVersions.AsNoTracking()
            .Where(v => currentIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Number, v.EntryPath, v.HasAnotador, v.FileCount })
            .ToDictionaryAsync(v => v.Id, ct);

        // Conteo de solicitudes del editor por proyecto (origen maqueta-{slug}); el join aplica el filtro de tareas.
        var sources = rows.Select(r => SourceFor(r.Slug)).ToList();
        var links = await (from l in _db.ExternalTaskLinks.AsNoTracking()
                           where sources.Contains(l.Source)
                           join t in _db.TaskItems.AsNoTracking() on l.TaskItemId equals t.Id
                           select new { l.Source, t.Status })
            .ToListAsync(ct);
        var counts = links.GroupBy(l => l.Source).ToDictionary(g => g.Key,
            g => (Open: g.Count(x => x.Status is not (TaskItemStatus.Done or TaskItemStatus.Closed)), Total: g.Count()));

        return rows.Select(r =>
        {
            var cur = r.CurrentVersionId is Guid id && current.TryGetValue(id, out var c) ? c : null;
            var cnt = counts.TryGetValue(SourceFor(r.Slug), out var k) ? k : (Open: 0, Total: 0);
            return new MockupProjectDto(r.Id, r.Slug, r.Name, r.Description, cur?.Number, cur?.EntryPath, cur?.HasAnotador ?? false, r.Count, r.Last,
                r.BoardName ?? DefaultBoardFor(r.Name), cnt.Open, cnt.Total, cur?.FileCount);
        }).ToList();
    }

    public async Task<MockupProjectDto?> GetProjectAsync(string slug, CancellationToken ct = default)
    {
        var clean = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsValidSlug(clean)) { return null; }
        return (await ListProjectsAsync(ct)).FirstOrDefault(p => p.Slug == clean);
    }

    public async Task<TaskCoreResult<MockupProjectDto>> UpdateProjectAsync(Guid projectId, string name, string? description, string? boardName,
        bool isOwnerOrAdmin, CancellationToken ct = default)
    {
        if (!isOwnerOrAdmin) { return TaskCoreResult<MockupProjectDto>.Forbidden("Solo Owner/Admin configura proyectos de maqueta."); }
        var project = await _db.MockupProjects.FirstOrDefaultAsync(p => p.Id == projectId && p.DeletedAt == null, ct);
        if (project is null) { return TaskCoreResult<MockupProjectDto>.NotFound("Proyecto no encontrado."); }
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length == 0) { return TaskCoreResult<MockupProjectDto>.Invalid("El nombre es obligatorio."); }
        if (cleanName.Length > 150) { cleanName = cleanName[..150]; }
        var desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (desc is { Length: > 600 }) { desc = desc[..600]; }
        var board = string.IsNullOrWhiteSpace(boardName) ? DefaultBoardFor(cleanName) : boardName.Trim();
        if (board.Length > 150) { board = board[..150]; }

        var previous = new { project.Name, project.Description, project.BoardName };
        project.Name = cleanName;
        project.Description = desc;
        project.BoardName = board;
        _audit.Write(_tenant.UserId ?? Guid.Empty, "maqueta.proyecto.update", nameof(MockupProject), project.Id,
            previousValue: previous, newValue: new { project.Name, project.Description, project.BoardName }, tenantId: project.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<MockupProjectDto>.Ok((await GetProjectAsync(project.Slug, ct))!);
    }

    public async Task<IReadOnlyList<MockupSolicitudDto>> ListSolicitudesAsync(string slug, CancellationToken ct = default)
    {
        if (!IsValidSlug(slug)) { return Array.Empty<MockupSolicitudDto>(); }
        var source = SourceFor(slug);
        var rows = await (from l in _db.ExternalTaskLinks.AsNoTracking()
                          where l.Source == source
                          join t in _db.TaskItems.AsNoTracking() on l.TaskItemId equals t.Id
                          orderby t.CreatedAt descending
                          select new
                          {
                              t.Id,
                              t.Number,
                              t.Title,
                              t.Status,
                              Column = t.Column != null ? t.Column.Name : null,
                              t.Priority,
                              t.CreatedAt,
                              l.MetadataJson
                          })
            .Take(500)
            .ToListAsync(ct);
        return rows.Select(r =>
        {
            var (pantalla, archivo, selector) = ReadMetadata(r.MetadataJson);
            return new MockupSolicitudDto(r.Id, r.Number, r.Title, r.Status, r.Column, r.Priority, r.CreatedAt, pantalla, archivo, selector);
        }).ToList();
    }

    /// <summary>Pantalla, archivo y selector guardados en el metadata (camelCase) de la solicitud.</summary>
    public static (string? Pantalla, string? Archivo, string? Selector) ReadMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return (null, null, null); }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return (null, null, null); }
            var selector = root.TryGetProperty("elemento", out var el) && el.ValueKind == JsonValueKind.Object ? Str(el, "selector") : null;
            return (Str(root, "pantalla"), Str(root, "archivoMaqueta"), selector);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }

        static string? Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public async Task<IReadOnlyList<MockupVersionDto>> ListVersionsAsync(Guid projectId, CancellationToken ct = default)
    {
        var current = await _db.MockupProjects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.CurrentVersionId).FirstOrDefaultAsync(ct);
        return await _db.MockupVersions.AsNoTracking()
            .Where(v => v.MockupProjectId == projectId)
            .OrderByDescending(v => v.Number)
            .Select(v => new MockupVersionDto(v.Id, v.Number, v.Id == current, v.SizeBytes, v.FileCount, v.EntryPath, v.HasAnotador,
                v.Notes, v.UploadedByName, v.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<TaskCoreResult<MockupProjectDto>> CreateProjectAsync(string name, string? slug, string? description, bool isOwnerOrAdmin,
        CancellationToken ct = default)
    {
        if (!isOwnerOrAdmin) { return TaskCoreResult<MockupProjectDto>.Forbidden("Solo Owner/Admin crea proyectos de maqueta."); }
        if (_tenant.TenantId is not Guid tenantId) { return TaskCoreResult<MockupProjectDto>.Invalid("No hay tenant activo."); }
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length == 0) { return TaskCoreResult<MockupProjectDto>.Invalid("El nombre es obligatorio."); }
        if (cleanName.Length > 150) { cleanName = cleanName[..150]; }
        var cleanSlug = string.IsNullOrWhiteSpace(slug) ? SlugFrom(cleanName) : slug.Trim().ToLowerInvariant();
        if (!IsValidSlug(cleanSlug)) { return TaskCoreResult<MockupProjectDto>.Invalid("El identificador solo admite minusculas, digitos y guion (max 40)."); }
        if (await _db.MockupProjects.AnyAsync(p => p.Slug == cleanSlug, ct))
        {
            return TaskCoreResult<MockupProjectDto>.Conflict($"Ya existe un proyecto con el identificador '{cleanSlug}'.");
        }

        var desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (desc is { Length: > 600 }) { desc = desc[..600]; }
        var project = new MockupProject { TenantId = tenantId, Slug = cleanSlug, Name = cleanName, Description = desc, BoardName = DefaultBoardFor(cleanName) };
        _db.MockupProjects.Add(project);
        _audit.Write(_tenant.UserId ?? Guid.Empty, "maqueta.proyecto.create", nameof(MockupProject), project.Id,
            previousValue: null, newValue: new { project.Slug, project.Name }, tenantId: tenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<MockupProjectDto>.Ok(new MockupProjectDto(project.Id, project.Slug, project.Name, project.Description, null, null, false, 0, null,
            project.BoardName));
    }

    public async Task<TaskCoreResult<MockupVersionDto>> UploadVersionAsync(string projectSlug, byte[] zip, string? notes, string uploaderName,
        bool isOwnerOrAdmin, CancellationToken ct = default)
    {
        if (!isOwnerOrAdmin) { return TaskCoreResult<MockupVersionDto>.Forbidden("Solo Owner/Admin sube versiones de maqueta."); }
        if (_tenant.TenantId is not Guid tenantId) { return TaskCoreResult<MockupVersionDto>.Invalid("No hay tenant activo."); }
        var slug = (projectSlug ?? string.Empty).Trim().ToLowerInvariant();
        var project = await _db.MockupProjects.FirstOrDefaultAsync(p => p.Slug == slug && p.DeletedAt == null, ct);
        if (project is null) { return TaskCoreResult<MockupVersionDto>.NotFound($"No existe el proyecto de maqueta '{slug}'."); }

        var inspection = InspectZip(zip);
        if (inspection.Error is not null) { return TaskCoreResult<MockupVersionDto>.Invalid(inspection.Error); }

        var number = project.LastVersionNumber + 1;
        // El ZIP va al gestor unificado (Azure Blob/disco), nunca inline en la BD.
        var stored = await _files.SaveAsync(new FileStorageSaveRequest(FileModule, project.Id.ToString(), number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{project.Slug}-v{number}.zip", "application/zip", zip), ct);

        var cleanNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (cleanNotes is { Length: > 600 }) { cleanNotes = cleanNotes[..600]; }
        var version = new MockupVersion
        {
            TenantId = tenantId,
            MockupProjectId = project.Id,
            Number = number,
            ZipFileId = stored.Id,
            SizeBytes = zip.LongLength,
            FileCount = inspection.FileCount,
            RootPrefix = inspection.RootPrefix,
            EntryPath = inspection.EntryPath!,
            HasAnotador = inspection.HasAnotador,
            Notes = cleanNotes,
            UploadedByName = string.IsNullOrWhiteSpace(uploaderName) ? null : uploaderName.Trim()[..Math.Min(uploaderName.Trim().Length, 150)]
        };
        _db.MockupVersions.Add(version);
        project.LastVersionNumber = number;
        project.CurrentVersionId = version.Id;
        _audit.Write(_tenant.UserId ?? Guid.Empty, "maqueta.version.upload", nameof(MockupVersion), version.Id,
            previousValue: null, newValue: new { project.Slug, number, version.FileCount, version.SizeBytes, version.EntryPath }, tenantId: tenantId, reason: AuditReason);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otra subida simultanea tomo el mismo numero: se libera el ZIP huerfano.
            try { await _files.DeleteAsync(stored.Id, CancellationToken.None); } catch (Exception) { /* best effort */ }
            return TaskCoreResult<MockupVersionDto>.Conflict("Otra version se subio al mismo tiempo; intenta de nuevo.");
        }

        return TaskCoreResult<MockupVersionDto>.Ok(new MockupVersionDto(version.Id, version.Number, true, version.SizeBytes, version.FileCount,
            version.EntryPath, version.HasAnotador, version.Notes, version.UploadedByName, version.CreatedAt));
    }

    public async Task<TaskCoreResult<bool>> SetCurrentVersionAsync(Guid projectId, Guid versionId, bool isOwnerOrAdmin, CancellationToken ct = default)
    {
        if (!isOwnerOrAdmin) { return TaskCoreResult<bool>.Forbidden("Solo Owner/Admin cambia la version actual."); }
        var project = await _db.MockupProjects.FirstOrDefaultAsync(p => p.Id == projectId && p.DeletedAt == null, ct);
        if (project is null) { return TaskCoreResult<bool>.NotFound("Proyecto no encontrado."); }
        var version = await _db.MockupVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId && v.MockupProjectId == projectId, ct);
        if (version is null) { return TaskCoreResult<bool>.NotFound("Version no encontrada."); }
        var previous = project.CurrentVersionId;
        project.CurrentVersionId = version.Id;
        _audit.Write(_tenant.UserId ?? Guid.Empty, "maqueta.version.set-current", nameof(MockupProject), project.Id,
            previousValue: new { previous }, newValue: new { version.Number }, tenantId: project.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<bool>> ArchiveProjectAsync(Guid projectId, bool isOwnerOrAdmin, CancellationToken ct = default)
    {
        if (!isOwnerOrAdmin) { return TaskCoreResult<bool>.Forbidden("Solo Owner/Admin archiva proyectos."); }
        var project = await _db.MockupProjects.FirstOrDefaultAsync(p => p.Id == projectId && p.DeletedAt == null, ct);
        if (project is null) { return TaskCoreResult<bool>.NotFound("Proyecto no encontrado."); }
        project.DeletedAt = DateTimeOffset.UtcNow;
        _audit.Write(_tenant.UserId ?? Guid.Empty, "maqueta.proyecto.archive", nameof(MockupProject), project.Id,
            previousValue: null, newValue: new { project.Slug }, tenantId: project.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<MockupServeTarget?> ResolveForServingAsync(string slug, int? versionNumber, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null || !IsValidSlug(slug)) { return null; }
        var project = await _db.MockupProjects.AsNoTracking()
            .Where(p => p.Slug == slug && p.DeletedAt == null)
            .Select(p => new { p.Id, p.CurrentVersionId })
            .FirstOrDefaultAsync(ct);
        if (project is null) { return null; }
        var query = _db.MockupVersions.AsNoTracking().Where(v => v.MockupProjectId == project.Id);
        query = versionNumber is int n ? query.Where(v => v.Number == n) : query.Where(v => v.Id == project.CurrentVersionId);
        return await query
            .Select(v => new MockupServeTarget(v.TenantId, v.Id, v.ZipFileId, v.RootPrefix, v.EntryPath))
            .FirstOrDefaultAsync(ct);
    }

    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Resultado de inspeccionar el ZIP (sin escribir nada a disco).</summary>
    public sealed record ZipInspection(string? Error, int FileCount, string? RootPrefix, string? EntryPath, bool HasAnotador);

    /// <summary>
    /// Valida el ZIP: tamano, cantidad de entradas, tamano descomprimido, rutas seguras (sin '..', sin rutas
    /// absolutas) y que traiga al menos una pagina HTML. Si TODO cuelga de una carpeta raiz unica (zip de la carpeta),
    /// esa carpeta se quita al servir. Ignora carpetas/archivos ocultos (.git, .env, __MACOSX).
    /// </summary>
    public static ZipInspection InspectZip(byte[] zip)
    {
        if (zip is null || zip.Length == 0) { return Fail("El archivo esta vacio."); }
        if (zip.Length > MaxZipBytes) { return Fail($"El ZIP supera {MaxZipBytes / (1024 * 1024)} MB."); }

        List<string> files;
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxEntries) { return Fail($"El ZIP tiene mas de {MaxEntries} entradas."); }
            long total = 0;
            files = new List<string>();
            foreach (var e in archive.Entries)
            {
                var path = NormalizeEntry(e.FullName);
                if (path is null) { return Fail($"Ruta no permitida en el ZIP: '{e.FullName}'."); }
                if (path.Length == 0 || path.EndsWith('/')) { continue; }
                total += e.Length;
                if (total > MaxUncompressedBytes) { return Fail($"El contenido descomprimido supera {MaxUncompressedBytes / (1024 * 1024)} MB."); }
                if (IsHiddenPath(path)) { continue; }
                files.Add(path);
            }
        }
        catch (InvalidDataException)
        {
            return Fail("El archivo no es un ZIP valido.");
        }

        if (files.Count == 0) { return Fail("El ZIP no trae archivos."); }

        // Carpeta raiz unica -> se quita (zip de la carpeta "tronox.maqueta/...").
        string? root = null;
        var firstSegments = files.Select(f => f.Contains('/') ? f[..(f.IndexOf('/') + 1)] : null).Distinct().ToList();
        if (firstSegments.Count == 1 && firstSegments[0] is { } only)
        {
            root = only;
        }
        var served = root is null ? files : files.Select(f => f[root.Length..]).ToList();
        var set = new HashSet<string>(served, StringComparer.OrdinalIgnoreCase);
        var hasAnotador = set.Contains(AnotadorEntry);
        // La maqueta es HTML puro y el editor vive en ECOREX (ADR-0180): la entrada es index.html (o la primera
        // pagina fuera del kit anotador/ heredado, que si viene se conserva pero ya no es la entrada).
        string? entry = set.Contains("index.html") ? "index.html"
            : served.Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !f.StartsWith("anotador/", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Count(c => c == '/')).ThenBy(f => f).FirstOrDefault()
              ?? (hasAnotador ? AnotadorEntry : null);
        if (entry is null) { return Fail("El ZIP no trae ninguna pagina .html."); }
        return new ZipInspection(null, served.Count, root, entry, hasAnotador);

        static ZipInspection Fail(string error) => new(error, 0, null, null, false);
    }

    /// <summary>Normaliza una entrada del ZIP a ruta relativa con '/'. Null si es insegura (absoluta o con '..').</summary>
    public static string? NormalizeEntry(string fullName)
    {
        var p = (fullName ?? string.Empty).Replace('\\', '/');
        if (p.StartsWith('/') || (p.Length > 1 && p[1] == ':')) { return null; }
        var segments = p.Split('/');
        if (segments.Any(s => s == "..")) { return null; }
        return string.Join('/', segments.Where(s => s != "."));
    }

    /// <summary>True si algun segmento es oculto (empieza por '.') o es basura de macOS.</summary>
    public static bool IsHiddenPath(string path)
        => path.Split('/').Any(s => s.StartsWith('.') || string.Equals(s, "__MACOSX", StringComparison.OrdinalIgnoreCase));
}
