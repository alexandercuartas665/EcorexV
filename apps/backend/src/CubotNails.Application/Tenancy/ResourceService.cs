using CubotNails.Application.Common;
using CubotNails.Domain.Entities;
using CubotNails.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotNails.Application.Tenancy;

public sealed record ResourceLinkDto(Guid ServiceId, string ServiceName, decimal BasePrice, int DurationMinutes, decimal? PriceOverride, decimal EffectivePrice);
public sealed record ResourceDto(Guid Id, string Name, ResourceKind Kind, string? Color, string? Phone, string? Notes, bool IsActive, IReadOnlyList<ResourceLinkDto> Services, Guid? SedeId = null, string? SedeName = null,
    SchedulingMode SchedulingMode = SchedulingMode.SlotGrid, int BufferMinutes = 0, bool HasPhoto = false);
public sealed record ResourceLinkInput(Guid ServiceId, decimal? PriceOverride);
public sealed record SaveResourceRequest(string Name, ResourceKind Kind, string? Color, string? Phone, string? Notes, IReadOnlyList<ResourceLinkInput> Services, Guid? SedeId = null,
    SchedulingMode SchedulingMode = SchedulingMode.SlotGrid, int BufferMinutes = 0);

/// <summary>
/// Asesores de imagen y estaciones del salon (Resource) con sus servicios habilitados y precios
/// personalizados (ResourceServiceLink). El override de precio solo aplica a recursos Image.
/// </summary>
public interface IResourceService
{
    Task<IReadOnlyList<ResourceDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ResourceDto?> CreateAsync(SaveResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ResourceDto?> UpdateAsync(Guid id, SaveResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Guarda (o reemplaza) la foto del asesor en la BD. Devuelve false si el recurso no existe.</summary>
    Task<bool> SetPhotoAsync(Guid resourceId, byte[] content, string? contentType, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Quita la foto del asesor.</summary>
    Task<bool> RemovePhotoAsync(Guid resourceId, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class ResourceService : IResourceService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public ResourceService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    public async Task<IReadOnlyList<ResourceDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var resources = await _db.Resources.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var links = await _db.ResourceServiceLinks.AsNoTracking().ToListAsync(cancellationToken);
        var svcById = await _db.Services.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var sedeNames = await _db.Sedes.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        // Solo los ids con foto (sin cargar los bytes; Resource se lista en cada turno del agente).
        var withPhoto = (await _db.ResourcePhotos.AsNoTracking().Select(p => p.ResourceId).ToListAsync(cancellationToken)).ToHashSet();
        return resources.Select(r => Map(r, links.Where(l => l.ResourceId == r.Id), svcById, sedeNames, withPhoto)).ToList();
    }

    public async Task<ResourceDto?> CreateAsync(SaveResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        var resource = new Resource
        {
            TenantId = tenantId,
            Name = name,
            Kind = request.Kind,
            Color = Clean(request.Color),
            Phone = Clean(request.Phone),
            Notes = Clean(request.Notes),
            SedeId = request.SedeId,
            SchedulingMode = request.SchedulingMode,
            BufferMinutes = Math.Max(0, request.BufferMinutes),
            IsActive = true
        };
        _db.Resources.Add(resource);
        ApplyLinks(resource, request, tenantId);
        _audit.Write(actorUserId, "resource.create", nameof(Resource), resource.Id, null, new { resource.Name, resource.Kind }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(resource.Id, cancellationToken);
    }

    public async Task<ResourceDto?> UpdateAsync(Guid id, SaveResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var resource = await _db.Resources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (resource is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }

        resource.Name = name;
        resource.Kind = request.Kind;
        resource.Color = Clean(request.Color);
        resource.Phone = Clean(request.Phone);
        resource.Notes = Clean(request.Notes);
        resource.SedeId = request.SedeId;
        resource.SchedulingMode = request.SchedulingMode;
        resource.BufferMinutes = Math.Max(0, request.BufferMinutes);

        var existing = await _db.ResourceServiceLinks.Where(l => l.ResourceId == id).ToListAsync(cancellationToken);
        _db.ResourceServiceLinks.RemoveRange(existing);
        ApplyLinks(resource, request, tenantId);

        _audit.Write(actorUserId, "resource.update", nameof(Resource), resource.Id, null, new { resource.Name, resource.Kind }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetDtoAsync(resource.Id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var resource = await _db.Resources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (resource is null) { return false; }
        var links = await _db.ResourceServiceLinks.Where(l => l.ResourceId == id).ToListAsync(cancellationToken);
        var shifts = await _db.ShiftTemplates.Where(s => s.ResourceId == id).ToListAsync(cancellationToken);
        _db.ResourceServiceLinks.RemoveRange(links);
        _db.ShiftTemplates.RemoveRange(shifts);
        _db.Resources.Remove(resource);
        _audit.Write(actorUserId, "resource.delete", nameof(Resource), resource.Id, new { resource.Name }, null, resource.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ApplyLinks(Resource resource, SaveResourceRequest request, Guid tenantId)
    {
        foreach (var link in request.Services ?? Array.Empty<ResourceLinkInput>())
        {
            // El override de precio solo aplica a asesores de imagen; las estaciones usan el precio base.
            var price = resource.Kind == ResourceKind.Image ? link.PriceOverride : null;
            _db.ResourceServiceLinks.Add(new ResourceServiceLink
            {
                TenantId = tenantId,
                ResourceId = resource.Id,
                ServiceId = link.ServiceId,
                PriceOverride = price
            });
        }
    }

    private async Task<ResourceDto?> GetDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var resource = await _db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (resource is null) { return null; }
        var links = await _db.ResourceServiceLinks.AsNoTracking().Where(l => l.ResourceId == id).ToListAsync(cancellationToken);
        var svcIds = links.Select(l => l.ServiceId).ToList();
        var svcById = await _db.Services.AsNoTracking().Where(s => svcIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var sedeNames = await _db.Sedes.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var withPhoto = await _db.ResourcePhotos.AsNoTracking().AnyAsync(p => p.ResourceId == id, cancellationToken)
            ? new HashSet<Guid> { id } : new HashSet<Guid>();
        return Map(resource, links, svcById, sedeNames, withPhoto);
    }

    private static ResourceDto Map(Resource r, IEnumerable<ResourceServiceLink> links, IReadOnlyDictionary<Guid, Service> svcById, IReadOnlyDictionary<Guid, string> sedeNames, IReadOnlySet<Guid> withPhoto)
    {
        var linkDtos = links
            .Where(l => svcById.ContainsKey(l.ServiceId))
            .Select(l =>
            {
                var s = svcById[l.ServiceId];
                var over = r.Kind == ResourceKind.Image ? l.PriceOverride : null;
                var effective = over ?? s.Price;
                return new ResourceLinkDto(l.ServiceId, s.Name, s.Price, s.DurationMinutes, over, effective);
            })
            .OrderBy(l => l.ServiceName)
            .ToList();
        string? sedeName = r.SedeId is Guid sid && sedeNames.TryGetValue(sid, out var sn) ? sn : null;
        return new ResourceDto(r.Id, r.Name, r.Kind, r.Color, r.Phone, r.Notes, r.IsActive, linkDtos, r.SedeId, sedeName, r.SchedulingMode, r.BufferMinutes, withPhoto.Contains(r.Id));
    }

    public async Task<bool> SetPhotoAsync(Guid resourceId, byte[] content, string? contentType, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return false; }
        if (content is null || content.Length == 0) { return false; }
        var resource = await _db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        if (resource is null) { return false; }

        var existing = await _db.ResourcePhotos.FirstOrDefaultAsync(p => p.ResourceId == resourceId, cancellationToken);
        if (existing is null)
        {
            _db.ResourcePhotos.Add(new ResourcePhoto { TenantId = tenantId, ResourceId = resourceId, Content = content, ContentType = contentType });
        }
        else
        {
            existing.Content = content;
            existing.ContentType = contentType;
        }
        _audit.Write(actorUserId, "resource.photo.set", nameof(Resource), resourceId, null, new { bytes = content.Length }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemovePhotoAsync(Guid resourceId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.ResourcePhotos.FirstOrDefaultAsync(p => p.ResourceId == resourceId, cancellationToken);
        if (existing is null) { return false; }
        _db.ResourcePhotos.Remove(existing);
        _audit.Write(actorUserId, "resource.photo.remove", nameof(Resource), resourceId, null, null, existing.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
