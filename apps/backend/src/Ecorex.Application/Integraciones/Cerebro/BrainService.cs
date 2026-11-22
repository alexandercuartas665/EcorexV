using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Mcp;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Integraciones.Cerebro;

/// <summary>Maqueta vinculada a un cerebro.</summary>
public sealed record BrainMockupRefDto(Guid ProjectId, string Slug, string Name, bool IsPrimary);

/// <summary>Tarjeta de la galeria de cerebros.</summary>
public sealed record BrainCardDto(
    Guid Id, string Slug, string Name, string? Description, Guid? CategoryId, string? CategoryName, string? CategoryColor,
    int Notes, DateTimeOffset UpdatedAt, IReadOnlyList<BrainMockupRefDto> Mockups);

/// <summary>Cerebro con lo necesario para su pagina (/cerebros/{slug}).</summary>
public sealed record BrainDetailDto(
    Guid Id, string Slug, string Name, string? Description, Guid? CategoryId, string? CategoryName, string? CategoryColor,
    long Version, IReadOnlyList<BrainMockupRefDto> Mockups);

public sealed record BrainCategoryDto(Guid Id, string Name, string? Color, int SortOrder, int Brains);

/// <summary>Cerebro principal de una maqueta (el que abre la pestana Cerebro del editor).</summary>
public sealed record BrainRefDto(Guid Id, string Slug, string Name);

/// <summary>Cerebro vinculado a una maqueta (tarjeta de la pestana Cerebros del editor, ADR-0200 ola 2).</summary>
public sealed record BrainLinkedDto(
    Guid Id, string Slug, string Name, string? Description, string? CategoryName, string? CategoryColor, int Notes, bool IsPrimary);

/// <summary>
/// Catalogo de CEREBROS del tenant (ADR-0200, ola 1): galeria con categorias, alta/edicion/archivo y vinculo N:M con
/// maquetas. Las NOTAS del cerebro las maneja <see cref="IMockupBrainService"/> (por slug del cerebro).
/// Todos en el tenant ven todos los cerebros; solo Owner/Admin crea, edita, archiva, vincula y administra categorias.
/// </summary>
public interface IBrainService
{
    Task<IReadOnlyList<BrainCardDto>> ListAsync(string? search = null, Guid? categoryId = null, CancellationToken ct = default);

    Task<BrainDetailDto?> GetAsync(string slug, CancellationToken ct = default);

    Task<TaskCoreResult<BrainDetailDto>> CreateAsync(string name, string? description, Guid? categoryId, McpActor actor,
        CancellationToken ct = default);

    Task<TaskCoreResult<BrainDetailDto>> UpdateAsync(Guid id, string name, string? description, Guid? categoryId, long expectedVersion,
        McpActor actor, CancellationToken ct = default);

    /// <summary>Archiva (soft-delete) el cerebro. Sus notas quedan intactas; deja de verse y de abrirse desde las maquetas.</summary>
    Task<TaskCoreResult<bool>> ArchiveAsync(Guid id, McpActor actor, CancellationToken ct = default);

    Task<IReadOnlyList<BrainCategoryDto>> ListCategoriesAsync(CancellationToken ct = default);

    /// <summary>Crea (<paramref name="id"/> null) o renombra/recolorea una categoria.</summary>
    Task<TaskCoreResult<BrainCategoryDto>> SaveCategoryAsync(Guid? id, string name, string? color, McpActor actor, CancellationToken ct = default);

    /// <summary>Archiva la categoria; sus cerebros quedan "sin categoria" (en la misma transaccion).</summary>
    Task<TaskCoreResult<bool>> ArchiveCategoryAsync(Guid id, McpActor actor, CancellationToken ct = default);

    /// <summary>Cerebro principal de la maqueta (menor orden entre los vinculados y activos) o null.</summary>
    Task<BrainRefDto?> GetPrimaryForMockupAsync(string projectSlug, CancellationToken ct = default);

    /// <summary>Crea "Cerebro de {maqueta}" y lo vincula como principal (si la maqueta ya tiene uno, lo devuelve).</summary>
    Task<TaskCoreResult<BrainRefDto>> CreateForMockupAsync(string projectSlug, McpActor actor, CancellationToken ct = default);

    /// <summary>Vincula un cerebro a una maqueta (queda al final; idempotente).</summary>
    Task<TaskCoreResult<bool>> LinkMockupAsync(string brainSlug, string projectSlug, McpActor actor, CancellationToken ct = default);

    Task<TaskCoreResult<bool>> UnlinkMockupAsync(string brainSlug, string projectSlug, McpActor actor, CancellationToken ct = default);

    /// <summary>Cerebros ACTIVOS vinculados a la maqueta, el principal primero (orden del vinculo).</summary>
    Task<IReadOnlyList<BrainLinkedDto>> ListForMockupAsync(string projectSlug, CancellationToken ct = default);

    /// <summary>Hace PRINCIPAL de la maqueta a un cerebro ya vinculado (queda primero; el resto conserva su orden).</summary>
    Task<TaskCoreResult<bool>> SetPrimaryAsync(string brainSlug, string projectSlug, McpActor actor, CancellationToken ct = default);
}

public sealed partial class BrainService : IBrainService
{
    public const int MaxNameLength = 150;
    public const int MaxDescriptionLength = 600;
    public const int MaxCategoryNameLength = 80;
    private const string AuditReason = "cerebro";

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public BrainService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    // ------------------------------------------------------------------ cerebros

    public async Task<IReadOnlyList<BrainCardDto>> ListAsync(string? search = null, Guid? categoryId = null, CancellationToken ct = default)
    {
        var query = _db.Brains.AsNoTracking();
        if (categoryId is { } cat) { query = query.Where(b => b.CategoryId == cat); }
        var term = (search ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length > 0)
        {
            query = query.Where(b => b.Name.ToLower().Contains(term) || (b.Description != null && b.Description.ToLower().Contains(term)));
        }
        var rows = await query
            .Select(b => new
            {
                b.Id,
                b.Slug,
                b.Name,
                b.Description,
                b.CategoryId,
                CategoryName = b.Category != null ? b.Category.Name : null,
                CategoryColor = b.Category != null ? b.Category.Color : null,
                Updated = b.UpdatedAt ?? b.CreatedAt
            })
            .ToListAsync(ct);
        if (rows.Count == 0) { return Array.Empty<BrainCardDto>(); }

        var ids = rows.Select(r => r.Id).ToList();
        var notes = await _db.MockupNotes.AsNoTracking()
            .Where(n => ids.Contains(n.BrainId))
            .GroupBy(n => n.BrainId)
            .Select(g => new { BrainId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrainId, x => x.Count, ct);
        var mockups = await MockupsOfAsync(ids, ct);

        return rows
            .Select(r => new BrainCardDto(r.Id, r.Slug, r.Name, r.Description, r.CategoryId, r.CategoryName, r.CategoryColor,
                notes.GetValueOrDefault(r.Id), r.Updated, mockups.GetValueOrDefault(r.Id) ?? (IReadOnlyList<BrainMockupRefDto>)Array.Empty<BrainMockupRefDto>()))
            .OrderBy(r => r.CategoryName is null)
            .ThenBy(r => r.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<BrainDetailDto?> GetAsync(string slug, CancellationToken ct = default)
    {
        var clean = CleanSlug(slug);
        if (clean is null) { return null; }
        var b = await _db.Brains.AsNoTracking().Include(x => x.Category).FirstOrDefaultAsync(x => x.Slug == clean, ct);
        return b is null ? null : await ToDetailAsync(b, ct);
    }

    public async Task<TaskCoreResult<BrainDetailDto>> CreateAsync(string name, string? description, Guid? categoryId, McpActor actor,
        CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<BrainDetailDto>.Forbidden("Solo Owner/Admin crea cerebros."); }
        var (cleanName, desc, error) = CleanNameAndDescription(name, description);
        if (error is not null) { return TaskCoreResult<BrainDetailDto>.Invalid(error); }
        if (categoryId is { } cat && !await _db.BrainCategories.AnyAsync(c => c.Id == cat, ct))
        {
            return TaskCoreResult<BrainDetailDto>.Invalid("La categoria no existe.");
        }

        var brain = new Brain
        {
            TenantId = actor.TenantId,
            Slug = await UniqueSlugAsync(cleanName!, ct),
            Name = cleanName!,
            Description = desc,
            CategoryId = categoryId,
            CreatedByName = Trim(actor.DisplayName, 150)
        };
        _db.Brains.Add(brain);
        _audit.Write(actor.PlatformUserId, "cerebro.crear", nameof(Brain), brain.Id, previousValue: null,
            newValue: new { brain.Slug, brain.Name, brain.CategoryId }, tenantId: actor.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<BrainDetailDto>.Ok((await GetAsync(brain.Slug, ct))!);
    }

    public async Task<TaskCoreResult<BrainDetailDto>> UpdateAsync(Guid id, string name, string? description, Guid? categoryId,
        long expectedVersion, McpActor actor, CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<BrainDetailDto>.Forbidden("Solo Owner/Admin edita cerebros."); }
        var (cleanName, desc, error) = CleanNameAndDescription(name, description);
        if (error is not null) { return TaskCoreResult<BrainDetailDto>.Invalid(error); }
        var brain = await _db.Brains.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brain is null) { return TaskCoreResult<BrainDetailDto>.NotFound("El cerebro no existe o fue archivado."); }
        if (brain.Version != expectedVersion) { return TaskCoreResult<BrainDetailDto>.Conflict("Otra persona modifico el cerebro. Recarga e intenta de nuevo."); }
        if (categoryId is { } cat && !await _db.BrainCategories.AnyAsync(c => c.Id == cat, ct))
        {
            return TaskCoreResult<BrainDetailDto>.Invalid("La categoria no existe.");
        }

        var before = new { brain.Name, brain.Description, brain.CategoryId };
        brain.Name = cleanName!;
        brain.Description = desc;
        brain.CategoryId = categoryId;
        _audit.Write(actor.PlatformUserId, "cerebro.editar", nameof(Brain), brain.Id, previousValue: before,
            newValue: new { brain.Name, brain.Description, brain.CategoryId }, tenantId: actor.TenantId, reason: AuditReason);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<BrainDetailDto>.Conflict("Otra persona modifico el cerebro. Recarga e intenta de nuevo.");
        }
        return TaskCoreResult<BrainDetailDto>.Ok((await GetAsync(brain.Slug, ct))!);
    }

    public async Task<TaskCoreResult<bool>> ArchiveAsync(Guid id, McpActor actor, CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<bool>.Forbidden("Solo Owner/Admin archiva cerebros."); }
        var brain = await _db.Brains.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (brain is null) { return TaskCoreResult<bool>.NotFound("El cerebro no existe o ya fue archivado."); }
        brain.IsDeleted = true;
        brain.DeletedAt = DateTimeOffset.UtcNow;
        brain.DeletedByTenantUserId = actor.TenantUserId;
        _audit.Write(actor.PlatformUserId, "cerebro.archivar", nameof(Brain), brain.Id, previousValue: null,
            newValue: new { brain.Slug, brain.Name }, tenantId: actor.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    // ------------------------------------------------------------------ categorias

    public async Task<IReadOnlyList<BrainCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var counts = await _db.Brains.AsNoTracking()
            .Where(b => b.CategoryId != null)
            .GroupBy(b => b.CategoryId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        var rows = await _db.BrainCategories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        return rows.Select(c => new BrainCategoryDto(c.Id, c.Name, c.Color, c.SortOrder, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<TaskCoreResult<BrainCategoryDto>> SaveCategoryAsync(Guid? id, string name, string? color, McpActor actor,
        CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<BrainCategoryDto>.Forbidden("Solo Owner/Admin administra las categorias."); }
        var clean = Regex.Replace((name ?? string.Empty).Trim(), @"\s+", " ");
        if (clean.Length == 0) { return TaskCoreResult<BrainCategoryDto>.Invalid("El nombre de la categoria es obligatorio."); }
        if (clean.Length > MaxCategoryNameLength) { return TaskCoreResult<BrainCategoryDto>.Invalid($"La categoria admite maximo {MaxCategoryNameLength} caracteres."); }
        var cleanColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        if (cleanColor is not null && !ColorPattern().IsMatch(cleanColor)) { return TaskCoreResult<BrainCategoryDto>.Invalid("El color debe tener la forma #RRGGBB."); }

        var lower = clean.ToLowerInvariant();
        if (await _db.BrainCategories.AnyAsync(c => c.Name.ToLower() == lower && c.Id != (id ?? Guid.Empty), ct))
        {
            return TaskCoreResult<BrainCategoryDto>.Conflict($"Ya existe la categoria '{clean}'.");
        }

        BrainCategory cat;
        if (id is { } catId)
        {
            var found = await _db.BrainCategories.FirstOrDefaultAsync(c => c.Id == catId, ct);
            if (found is null) { return TaskCoreResult<BrainCategoryDto>.NotFound("La categoria no existe."); }
            cat = found;
            cat.Name = clean;
            cat.Color = cleanColor;
        }
        else
        {
            var max = await _db.BrainCategories.Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
            cat = new BrainCategory { TenantId = actor.TenantId, Name = clean, Color = cleanColor, SortOrder = max + 1 };
            _db.BrainCategories.Add(cat);
        }
        _audit.Write(actor.PlatformUserId, id is null ? "cerebro.categoria.crear" : "cerebro.categoria.editar", nameof(BrainCategory), cat.Id,
            previousValue: null, newValue: new { cat.Name, cat.Color }, tenantId: actor.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        var brains = await _db.Brains.CountAsync(b => b.CategoryId == cat.Id, ct);
        return TaskCoreResult<BrainCategoryDto>.Ok(new BrainCategoryDto(cat.Id, cat.Name, cat.Color, cat.SortOrder, brains));
    }

    public async Task<TaskCoreResult<bool>> ArchiveCategoryAsync(Guid id, McpActor actor, CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<bool>.Forbidden("Solo Owner/Admin administra las categorias."); }
        var cat = await _db.BrainCategories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cat is null) { return TaskCoreResult<bool>.NotFound("La categoria no existe."); }
        cat.IsDeleted = true;
        cat.DeletedAt = DateTimeOffset.UtcNow;
        cat.DeletedByTenantUserId = actor.TenantUserId;
        // Incluye los cerebros archivados: si se restauran no deben apuntar a una categoria archivada.
        var brains = await _db.Brains.IgnoreQueryFilters()
            .Where(b => b.TenantId == actor.TenantId && b.CategoryId == id)
            .ToListAsync(ct);
        foreach (var b in brains) { b.CategoryId = null; }
        _audit.Write(actor.PlatformUserId, "cerebro.categoria.archivar", nameof(BrainCategory), cat.Id, previousValue: null,
            newValue: new { cat.Name, cerebrosSinCategoria = brains.Count }, tenantId: actor.TenantId, reason: AuditReason);
        // Un solo SaveChanges = una transaccion.
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    // ------------------------------------------------------------------ maquetas

    public async Task<BrainRefDto?> GetPrimaryForMockupAsync(string projectSlug, CancellationToken ct = default)
    {
        var project = await FindProjectAsync(projectSlug, ct);
        if (project is null) { return null; }
        // El join con Brains aplica el filtro global: un cerebro archivado no cuenta como principal.
        return await (from l in _db.MockupProjectBrains.AsNoTracking()
                      where l.MockupProjectId == project.Value.Id
                      join b in _db.Brains.AsNoTracking() on l.BrainId equals b.Id
                      orderby l.SortOrder, l.CreatedAt
                      select new BrainRefDto(b.Id, b.Slug, b.Name))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TaskCoreResult<BrainRefDto>> CreateForMockupAsync(string projectSlug, McpActor actor, CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<BrainRefDto>.Forbidden("Solo Owner/Admin crea cerebros."); }
        var project = await FindProjectAsync(projectSlug, ct);
        if (project is null) { return TaskCoreResult<BrainRefDto>.NotFound($"No existe la maqueta '{projectSlug}'."); }
        var current = await GetPrimaryForMockupAsync(project.Value.Slug, ct);
        if (current is not null) { return TaskCoreResult<BrainRefDto>.Ok(current); }

        var name = Trim("Cerebro de " + project.Value.Name, MaxNameLength)!;
        var brain = new Brain
        {
            TenantId = actor.TenantId,
            Slug = await UniqueSlugAsync(project.Value.Slug, ct),
            Name = name,
            CreatedByName = Trim(actor.DisplayName, 150)
        };
        _db.Brains.Add(brain);
        var order = await NextLinkOrderAsync(project.Value.Id, ct);
        _db.MockupProjectBrains.Add(new MockupProjectBrain
        {
            TenantId = actor.TenantId,
            MockupProjectId = project.Value.Id,
            BrainId = brain.Id,
            SortOrder = order
        });
        _audit.Write(actor.PlatformUserId, "cerebro.crear", nameof(Brain), brain.Id, previousValue: null,
            newValue: new { brain.Slug, brain.Name, maqueta = project.Value.Slug }, tenantId: actor.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<BrainRefDto>.Ok(new BrainRefDto(brain.Id, brain.Slug, brain.Name));
    }

    public async Task<TaskCoreResult<bool>> LinkMockupAsync(string brainSlug, string projectSlug, McpActor actor, CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<bool>.Forbidden("Solo Owner/Admin vincula cerebros."); }
        var (brain, project, error) = await FindPairAsync(brainSlug, projectSlug, ct);
        if (error is not null) { return TaskCoreResult<bool>.NotFound(error); }
        if (await _db.MockupProjectBrains.AnyAsync(l => l.MockupProjectId == project!.Value.Id && l.BrainId == brain!.Id, ct))
        {
            return TaskCoreResult<bool>.Ok(true);
        }
        _db.MockupProjectBrains.Add(new MockupProjectBrain
        {
            TenantId = actor.TenantId,
            MockupProjectId = project!.Value.Id,
            BrainId = brain!.Id,
            SortOrder = await NextLinkOrderAsync(project.Value.Id, ct)
        });
        _audit.Write(actor.PlatformUserId, "cerebro.vincular", nameof(Brain), brain.Id, previousValue: null,
            newValue: new { cerebro = brain.Slug, maqueta = project.Value.Slug }, tenantId: actor.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<bool>> UnlinkMockupAsync(string brainSlug, string projectSlug, McpActor actor, CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<bool>.Forbidden("Solo Owner/Admin desvincula cerebros."); }
        var (brain, project, error) = await FindPairAsync(brainSlug, projectSlug, ct);
        if (error is not null) { return TaskCoreResult<bool>.NotFound(error); }
        var link = await _db.MockupProjectBrains.FirstOrDefaultAsync(l => l.MockupProjectId == project!.Value.Id && l.BrainId == brain!.Id, ct);
        if (link is null) { return TaskCoreResult<bool>.Ok(true); }
        _db.MockupProjectBrains.Remove(link);
        _audit.Write(actor.PlatformUserId, "cerebro.desvincular", nameof(Brain), brain!.Id, previousValue: null,
            newValue: new { cerebro = brain.Slug, maqueta = project!.Value.Slug }, tenantId: actor.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<BrainLinkedDto>> ListForMockupAsync(string projectSlug, CancellationToken ct = default)
    {
        var project = await FindProjectAsync(projectSlug, ct);
        if (project is null) { return Array.Empty<BrainLinkedDto>(); }
        // El join con Brains aplica el filtro global: los cerebros archivados no aparecen.
        var rows = await (from l in _db.MockupProjectBrains.AsNoTracking()
                          where l.MockupProjectId == project.Value.Id
                          join b in _db.Brains.AsNoTracking() on l.BrainId equals b.Id
                          orderby l.SortOrder, l.CreatedAt
                          select new
                          {
                              b.Id,
                              b.Slug,
                              b.Name,
                              b.Description,
                              CategoryName = b.Category != null ? b.Category.Name : null,
                              CategoryColor = b.Category != null ? b.Category.Color : null
                          })
            .ToListAsync(ct);
        if (rows.Count == 0) { return Array.Empty<BrainLinkedDto>(); }
        var ids = rows.Select(r => r.Id).ToList();
        var notes = await _db.MockupNotes.AsNoTracking()
            .Where(n => ids.Contains(n.BrainId))
            .GroupBy(n => n.BrainId)
            .Select(g => new { BrainId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrainId, x => x.Count, ct);
        return rows
            .Select((r, i) => new BrainLinkedDto(r.Id, r.Slug, r.Name, r.Description, r.CategoryName, r.CategoryColor,
                notes.GetValueOrDefault(r.Id), i == 0))
            .ToList();
    }

    public async Task<TaskCoreResult<bool>> SetPrimaryAsync(string brainSlug, string projectSlug, McpActor actor, CancellationToken ct = default)
    {
        if (!actor.IsOwnerOrAdmin) { return TaskCoreResult<bool>.Forbidden("Solo Owner/Admin elige el cerebro principal."); }
        var (brain, project, error) = await FindPairAsync(brainSlug, projectSlug, ct);
        if (error is not null) { return TaskCoreResult<bool>.NotFound(error); }
        var links = await _db.MockupProjectBrains
            .Where(l => l.MockupProjectId == project!.Value.Id)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.CreatedAt)
            .ToListAsync(ct);
        var chosen = links.FirstOrDefault(l => l.BrainId == brain!.Id);
        if (chosen is null) { return TaskCoreResult<bool>.Invalid("El cerebro no esta vinculado a esta maqueta."); }
        var order = 1;
        chosen.SortOrder = 0;
        foreach (var l in links.Where(l => l != chosen)) { l.SortOrder = order++; }
        _audit.Write(actor.PlatformUserId, "cerebro.principal", nameof(Brain), brain!.Id, previousValue: null,
            newValue: new { cerebro = brain.Slug, maqueta = project!.Value.Slug }, tenantId: actor.TenantId, reason: AuditReason);
        // Un solo SaveChanges = una transaccion (todos los ordenes cambian juntos).
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<BrainDetailDto> ToDetailAsync(Brain b, CancellationToken ct)
    {
        var mockups = await MockupsOfAsync(new List<Guid> { b.Id }, ct);
        return new BrainDetailDto(b.Id, b.Slug, b.Name, b.Description, b.CategoryId, b.Category?.Name, b.Category?.Color, b.Version,
            mockups.GetValueOrDefault(b.Id) ?? (IReadOnlyList<BrainMockupRefDto>)Array.Empty<BrainMockupRefDto>());
    }

    /// <summary>Maquetas activas vinculadas a cada cerebro; IsPrimary = el cerebro es el principal de esa maqueta.</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<BrainMockupRefDto>>> MockupsOfAsync(List<Guid> brainIds, CancellationToken ct)
    {
        var links = await (from l in _db.MockupProjectBrains.AsNoTracking()
                           join p in _db.MockupProjects.AsNoTracking() on l.MockupProjectId equals p.Id
                           where p.DeletedAt == null
                           select new { l.BrainId, l.MockupProjectId, l.SortOrder, l.CreatedAt, p.Slug, p.Name })
            .ToListAsync(ct);
        if (links.Count == 0) { return new Dictionary<Guid, IReadOnlyList<BrainMockupRefDto>>(); }
        var active = await _db.Brains.AsNoTracking().Select(b => b.Id).ToListAsync(ct);
        var activeSet = active.ToHashSet();
        var primaryByProject = links.Where(l => activeSet.Contains(l.BrainId))
            .GroupBy(l => l.MockupProjectId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAt).First().BrainId);
        return links.Where(l => brainIds.Contains(l.BrainId))
            .GroupBy(l => l.BrainId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BrainMockupRefDto>)g
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new BrainMockupRefDto(x.MockupProjectId, x.Slug, x.Name, primaryByProject.GetValueOrDefault(x.MockupProjectId) == g.Key))
                .ToList());
    }

    private async Task<(Guid Id, string Slug, string Name)?> FindProjectAsync(string slug, CancellationToken ct)
    {
        var clean = CleanSlug(slug);
        if (clean is null) { return null; }
        var p = await _db.MockupProjects.AsNoTracking()
            .Where(x => x.Slug == clean && x.DeletedAt == null)
            .Select(x => new { x.Id, x.Slug, x.Name })
            .FirstOrDefaultAsync(ct);
        return p is null ? null : (p.Id, p.Slug, p.Name);
    }

    private async Task<(Brain? Brain, (Guid Id, string Slug, string Name)? Project, string? Error)> FindPairAsync(
        string brainSlug, string projectSlug, CancellationToken ct)
    {
        var cleanBrain = CleanSlug(brainSlug);
        var brain = cleanBrain is null ? null : await _db.Brains.AsNoTracking().FirstOrDefaultAsync(b => b.Slug == cleanBrain, ct);
        if (brain is null) { return (null, null, $"No existe el cerebro '{brainSlug}'."); }
        var project = await FindProjectAsync(projectSlug, ct);
        if (project is null) { return (null, null, $"No existe la maqueta '{projectSlug}'."); }
        return (brain, project, null);
    }

    private async Task<int> NextLinkOrderAsync(Guid projectId, CancellationToken ct)
        => (await _db.MockupProjectBrains.Where(l => l.MockupProjectId == projectId).Select(l => (int?)l.SortOrder).MaxAsync(ct) ?? -1) + 1;

    /// <summary>Slug libre entre los cerebros activos: base, base-2, base-3...</summary>
    private async Task<string> UniqueSlugAsync(string nameOrSlug, CancellationToken ct)
    {
        var baseSlug = MockupService.SlugFrom(nameOrSlug);
        if (!MockupService.IsValidSlug(baseSlug)) { baseSlug = "cerebro"; }
        var taken = (await _db.Brains.AsNoTracking().Where(b => b.Slug.StartsWith(baseSlug)).Select(b => b.Slug).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(baseSlug)) { return baseSlug; }
        for (var i = 2; ; i++)
        {
            var suffix = "-" + i;
            var candidate = (baseSlug.Length + suffix.Length > 40 ? baseSlug[..(40 - suffix.Length)].TrimEnd('-') : baseSlug) + suffix;
            if (!taken.Contains(candidate)) { return candidate; }
        }
    }

    private static string? CleanSlug(string? slug)
    {
        var clean = (slug ?? string.Empty).Trim().ToLowerInvariant();
        return MockupService.IsValidSlug(clean) ? clean : null;
    }

    private static (string? Name, string? Description, string? Error) CleanNameAndDescription(string? name, string? description)
    {
        var cleanName = Regex.Replace((name ?? string.Empty).Trim(), @"\s+", " ");
        if (cleanName.Length == 0) { return (null, null, "El nombre es obligatorio."); }
        if (cleanName.Length > MaxNameLength) { return (null, null, $"El nombre admite maximo {MaxNameLength} caracteres."); }
        var desc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (desc is { Length: > MaxDescriptionLength }) { return (null, null, $"La descripcion admite maximo {MaxDescriptionLength} caracteres."); }
        return (cleanName, desc, null);
    }

    private static string? Trim(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}
