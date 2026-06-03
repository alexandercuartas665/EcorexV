using CubotNails.Application.Common;
using CubotNails.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Application.Tenancy;

public sealed record ServiceDto(Guid Id, string Name, int DurationMinutes, decimal Price, string? Category, string? Color, bool IsActive);
public sealed record SaveServiceRequest(string Name, int DurationMinutes, decimal Price, string? Category, string? Color);

/// <summary>Catalogo de servicios del salon (Servicios). Tenant-scoped CRUD.</summary>
public interface IServiceCatalogService
{
    Task<IReadOnlyList<ServiceDto>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<ServiceDto?> CreateAsync(SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ServiceDto?> UpdateAsync(Guid id, SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
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
        => await _db.Services.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.Name, s.DurationMinutes, s.Price, s.Category, s.Color, s.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<ServiceDto?> CreateAsync(SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        var entity = new Service
        {
            TenantId = tenantId,
            Name = name,
            DurationMinutes = Math.Max(0, request.DurationMinutes),
            Price = Math.Max(0m, request.Price),
            Category = Clean(request.Category),
            Color = Clean(request.Color),
            IsActive = true
        };
        _db.Services.Add(entity);
        _audit.Write(actorUserId, "service.create", nameof(Service), entity.Id, null, new { entity.Name, entity.Price }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<ServiceDto?> UpdateAsync(Guid id, SaveServiceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        entity.Name = name;
        entity.DurationMinutes = Math.Max(0, request.DurationMinutes);
        entity.Price = Math.Max(0m, request.Price);
        entity.Category = Clean(request.Category);
        entity.Color = Clean(request.Color);
        _audit.Write(actorUserId, "service.update", nameof(Service), entity.Id, null, new { entity.Name, entity.Price }, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
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
        _db.Services.Remove(entity);
        _audit.Write(actorUserId, "service.delete", nameof(Service), entity.Id, new { entity.Name }, null, entity.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static ServiceDto Map(Service s) => new(s.Id, s.Name, s.DurationMinutes, s.Price, s.Category, s.Color, s.IsActive);
}
