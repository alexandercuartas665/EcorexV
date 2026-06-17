using CubotNails.Application.Common;
using CubotNails.Domain.Entities;
using CubotNails.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Application.Tenancy;

public sealed record ServiceImageDto(Guid Id, string Url, string? FileName, int SortOrder);
/// <summary>Precio y duracion de un servicio segun el largo de cabello. Sirve para leer y para guardar.</summary>
public sealed record ServicePriceTierDto(HairLength Length, decimal Price, int DurationMinutes);
public sealed record ServiceDto(Guid Id, string Name, int DurationMinutes, decimal Price, string? Category, string? Color, bool IsActive, string? Description = null, IReadOnlyList<ServiceImageDto>? Images = null, IReadOnlyList<ServicePriceTierDto>? PriceTiers = null);
public sealed record SaveServiceRequest(string Name, int DurationMinutes, decimal Price, string? Category, string? Color, string? Description = null, IReadOnlyList<ServicePriceTierDto>? PriceTiers = null);

/// <summary>Catalogo de servicios del salon (Servicios). Tenant-scoped CRUD. Las imagenes (archivos en
/// wwwroot/uploads/services) las sube la UI; aqui solo se guarda la URL.</summary>
public interface IServiceCatalogService
{
    Task<IReadOnlyList<ServiceDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ServiceDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ServiceDto?> CreateAsync(SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ServiceDto?> UpdateAsync(Guid id, SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ServiceImageDto?> AddImageAsync(Guid serviceId, string url, string? fileName, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> RemoveImageAsync(Guid imageId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class ServiceCatalogService : IServiceCatalogService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public ServiceCatalogService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    public async Task<IReadOnlyList<ServiceDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var services = await _db.Services.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
        if (services.Count == 0) { return new List<ServiceDto>(); }
        var ids = services.Select(s => s.Id).ToList();
        var images = await _db.ServiceImages.AsNoTracking()
            .Where(i => ids.Contains(i.ServiceId)).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var byService = images.GroupBy(i => i.ServiceId).ToDictionary(g => g.Key, g => (IEnumerable<ServiceImage>)g);
        var tiers = await _db.ServicePriceTiers.AsNoTracking()
            .Where(t => ids.Contains(t.ServiceId)).ToListAsync(cancellationToken);
        var tiersByService = tiers.GroupBy(t => t.ServiceId).ToDictionary(g => g.Key, g => (IEnumerable<ServicePriceTier>)g);
        return services.Select(s => Map(s,
            byService.TryGetValue(s.Id, out var im) ? im : Enumerable.Empty<ServiceImage>(),
            tiersByService.TryGetValue(s.Id, out var ti) ? ti : Enumerable.Empty<ServicePriceTier>())).ToList();
    }

    public async Task<ServiceDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var s = await _db.Services.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (s is null) { return null; }
        var images = await _db.ServiceImages.AsNoTracking()
            .Where(i => i.ServiceId == id).OrderBy(i => i.SortOrder).ToListAsync(cancellationToken);
        var tiers = await _db.ServicePriceTiers.AsNoTracking()
            .Where(t => t.ServiceId == id).ToListAsync(cancellationToken);
        return Map(s, images, tiers);
    }

    public async Task<ServiceDto?> CreateAsync(SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        var entity = new Service
        {
            TenantId = tenantId,
            Name = name,
            Description = Clean(request.Description),
            DurationMinutes = Math.Max(0, request.DurationMinutes),
            Price = Math.Max(0m, request.Price),
            Category = Clean(request.Category),
            Color = Clean(request.Color),
            IsActive = true
        };
        _db.Services.Add(entity);
        _audit.Write(actorUserId, "service.create", nameof(Service), entity.Id, null, new { entity.Name, entity.Price }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        await SaveTiersAsync(entity.Id, tenantId, request.PriceTiers, cancellationToken);
        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<ServiceDto?> UpdateAsync(Guid id, SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        entity.Name = name;
        entity.Description = Clean(request.Description);
        entity.DurationMinutes = Math.Max(0, request.DurationMinutes);
        entity.Price = Math.Max(0m, request.Price);
        entity.Category = Clean(request.Category);
        entity.Color = Clean(request.Color);
        _audit.Write(actorUserId, "service.update", nameof(Service), entity.Id, null, new { entity.Name, entity.Price }, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        await SaveTiersAsync(entity.Id, entity.TenantId, request.PriceTiers, cancellationToken);
        return await GetAsync(entity.Id, cancellationToken);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null) { return false; }
        entity.IsActive = isActive;
        _audit.Write(actorUserId, "service.set-active", nameof(Service), entity.Id, null, new { isActive }, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null) { return false; }
        var links = await _db.ResourceServiceLinks.Where(l => l.ServiceId == id).ToListAsync(cancellationToken);
        _db.ResourceServiceLinks.RemoveRange(links);
        _db.ServiceImages.RemoveRange(await _db.ServiceImages.Where(i => i.ServiceId == id).ToListAsync(cancellationToken));
        _db.ServicePriceTiers.RemoveRange(await _db.ServicePriceTiers.Where(t => t.ServiceId == id).ToListAsync(cancellationToken));
        _db.Services.Remove(entity);
        _audit.Write(actorUserId, "service.delete", nameof(Service), entity.Id, new { entity.Name }, null, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ServiceImageDto?> AddImageAsync(Guid serviceId, string url, string? fileName, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (!await _db.Services.AnyAsync(s => s.Id == serviceId, cancellationToken)) { return null; }
        var next = (await _db.ServiceImages.Where(i => i.ServiceId == serviceId).Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var img = new ServiceImage { TenantId = tenantId, ServiceId = serviceId, Url = url.Trim(), FileName = fileName, SortOrder = next };
        _db.ServiceImages.Add(img);
        await _db.SaveChangesAsync(cancellationToken);
        return new ServiceImageDto(img.Id, img.Url, img.FileName, img.SortOrder);
    }

    public async Task<bool> RemoveImageAsync(Guid imageId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var img = await _db.ServiceImages.FirstOrDefaultAsync(i => i.Id == imageId, cancellationToken);
        if (img is null) { return false; }
        _db.ServiceImages.Remove(img);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Reemplaza las tarifas por largo del servicio por las indicadas (solo las que tengan precio o duracion).
    private async Task SaveTiersAsync(Guid serviceId, Guid tenantId, IReadOnlyList<ServicePriceTierDto>? tiers, CancellationToken ct)
    {
        var existing = await _db.ServicePriceTiers.Where(t => t.ServiceId == serviceId).ToListAsync(ct);
        if (existing.Count > 0) { _db.ServicePriceTiers.RemoveRange(existing); }
        if (tiers is not null)
        {
            foreach (var t in tiers.GroupBy(x => x.Length).Select(g => g.First()))
            {
                if (t.Price <= 0 && t.DurationMinutes <= 0) { continue; }
                _db.ServicePriceTiers.Add(new ServicePriceTier
                {
                    TenantId = tenantId,
                    ServiceId = serviceId,
                    Length = t.Length,
                    Price = Math.Max(0m, t.Price),
                    DurationMinutes = Math.Max(0, t.DurationMinutes)
                });
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static ServiceDto Map(Service s, IEnumerable<ServiceImage>? images = null, IEnumerable<ServicePriceTier>? tiers = null) =>
        new(s.Id, s.Name, s.DurationMinutes, s.Price, s.Category, s.Color, s.IsActive, s.Description,
            (images ?? Enumerable.Empty<ServiceImage>()).OrderBy(i => i.SortOrder)
                .Select(i => new ServiceImageDto(i.Id, i.Url, i.FileName, i.SortOrder)).ToList(),
            (tiers ?? Enumerable.Empty<ServicePriceTier>()).OrderBy(t => t.Length)
                .Select(t => new ServicePriceTierDto(t.Length, t.Price, t.DurationMinutes)).ToList());
}
