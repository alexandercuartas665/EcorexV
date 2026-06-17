using CubotNails.Application.Common;
using CubotNails.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Application.Tenancy;

public sealed record HairLengthRefImageDto(Guid Id, string Url, string? FileName, int SortOrder);
public sealed record HairLengthCategoryDto(
    Guid Id, string Name, string? Description, int SortOrder, bool IsActive, IReadOnlyList<HairLengthRefImageDto> Images);
public sealed record SaveHairLengthCategoryRequest(string Name, string? Description);

/// <summary>
/// Medidas de cabello del salon (categorias personalizables + imagenes de referencia). Tenant-scoped.
/// Las imagenes de referencia son archivos publicos en wwwroot/uploads/hair (no son fotos de clientas).
/// </summary>
public interface IHairLengthService
{
    Task<IReadOnlyList<HairLengthCategoryDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<HairLengthCategoryDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<HairLengthCategoryDto?> CreateCategoryAsync(SaveHairLengthCategoryRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<HairLengthCategoryDto?> UpdateCategoryAsync(Guid id, SaveHairLengthCategoryRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteCategoryAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<HairLengthRefImageDto?> AddImageAsync(Guid categoryId, byte[] content, string contentType, string? fileName, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> RemoveImageAsync(Guid imageId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class HairLengthService : IHairLengthService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public HairLengthService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    public async Task<IReadOnlyList<HairLengthCategoryDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var cats = await _db.HairLengthCategories.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        if (cats.Count == 0) { return new List<HairLengthCategoryDto>(); }
        var ids = cats.Select(c => c.Id).ToList();
        var imgs = await _db.HairLengthReferenceImages.AsNoTracking()
            .Where(i => ids.Contains(i.CategoryId)).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var byCat = imgs.GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => (IEnumerable<HairLengthReferenceImage>)g);
        return cats.Select(c => Map(c, byCat.TryGetValue(c.Id, out var im) ? im : Enumerable.Empty<HairLengthReferenceImage>())).ToList();
    }

    public async Task<HairLengthCategoryDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await _db.HairLengthCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null) { return null; }
        var imgs = await _db.HairLengthReferenceImages.AsNoTracking()
            .Where(i => i.CategoryId == id).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        return Map(c, imgs);
    }

    public async Task<HairLengthCategoryDto?> CreateCategoryAsync(SaveHairLengthCategoryRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }
        var nextOrder = (await _db.HairLengthCategories.Select(c => (int?)c.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var entity = new HairLengthCategory
        {
            TenantId = tenantId,
            Name = name,
            Description = Clean(request.Description),
            SortOrder = nextOrder,
            IsActive = true
        };
        _db.HairLengthCategories.Add(entity);
        _audit.Write(actorUserId, "hair-length.create", nameof(HairLengthCategory), entity.Id, null, new { entity.Name }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity, Enumerable.Empty<HairLengthReferenceImage>());
    }

    public async Task<HairLengthCategoryDto?> UpdateCategoryAsync(Guid id, SaveHairLengthCategoryRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.HairLengthCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }
        entity.Name = name;
        entity.Description = Clean(request.Description);
        _audit.Write(actorUserId, "hair-length.update", nameof(HairLengthCategory), entity.Id, null, new { entity.Name }, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.HairLengthCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) { return false; }
        entity.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteCategoryAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.HairLengthCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) { return false; }
        _db.HairLengthReferenceImages.RemoveRange(await _db.HairLengthReferenceImages.Where(i => i.CategoryId == id).ToListAsync(cancellationToken));
        _db.HairLengthCategories.Remove(entity);
        _audit.Write(actorUserId, "hair-length.delete", nameof(HairLengthCategory), entity.Id, new { entity.Name }, null, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<HairLengthRefImageDto?> AddImageAsync(Guid categoryId, byte[] content, string contentType, string? fileName, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (content is null || content.Length == 0) { return null; }
        if (!await _db.HairLengthCategories.AnyAsync(c => c.Id == categoryId, cancellationToken)) { return null; }
        var next = (await _db.HairLengthReferenceImages.Where(i => i.CategoryId == categoryId).Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var img = new HairLengthReferenceImage
        {
            TenantId = tenantId,
            CategoryId = categoryId,
            Content = content,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
            FileName = fileName,
            SortOrder = next
        };
        _db.HairLengthReferenceImages.Add(img);
        await _db.SaveChangesAsync(cancellationToken);
        return new HairLengthRefImageDto(img.Id, $"/media/hairref/{img.Id}", img.FileName, img.SortOrder);
    }

    public async Task<bool> RemoveImageAsync(Guid imageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var img = await _db.HairLengthReferenceImages.FirstOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        if (img is null) { return false; }
        _db.HairLengthReferenceImages.Remove(img);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static HairLengthCategoryDto Map(HairLengthCategory c, IEnumerable<HairLengthReferenceImage> images) =>
        new(c.Id, c.Name, c.Description, c.SortOrder, c.IsActive,
            images.OrderBy(i => i.SortOrder).Select(i => new HairLengthRefImageDto(i.Id, $"/media/hairref/{i.Id}", i.FileName, i.SortOrder)).ToList());
}
