using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record SedeDto(Guid Id, string Name, string City, string? Address, string? Phone, bool IsActive, int ResourceCount);
public sealed record SaveSedeRequest(string Name, string City, string? Address, string? Phone);

/// <summary>Sedes (locales) del salon. Tenant-scoped CRUD.</summary>
public interface ISedeService
{
    Task<IReadOnlyList<SedeDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<SedeDto?> CreateAsync(SaveSedeRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<SedeDto?> UpdateAsync(Guid id, SaveSedeRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class SedeService : ISedeService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public SedeService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    public async Task<IReadOnlyList<SedeDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        var sedes = await _db.Sedes.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.City).ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
        var counts = await _db.Resources.AsNoTracking()
            .Where(r => r.SedeId != null)
            .GroupBy(r => r.SedeId!.Value)
            .Select(g => new { SedeId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return sedes.Select(s => new SedeDto(s.Id, s.Name, s.City, s.Address, s.Phone, s.IsActive,
            counts.FirstOrDefault(c => c.SedeId == s.Id)?.Count ?? 0)).ToList();
    }

    public async Task<SedeDto?> CreateAsync(SaveSedeRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        var city = (request.City ?? string.Empty).Trim();
        if (name.Length == 0 || city.Length == 0) { return null; }

        var sede = new Sede
        {
            TenantId = tenantId,
            Name = name,
            City = city,
            Address = Clean(request.Address),
            Phone = Clean(request.Phone),
            IsActive = true
        };
        _db.Sedes.Add(sede);
        _audit.Write(actorUserId, "sede.create", nameof(Sede), sede.Id, null, new { sede.Name, sede.City }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new SedeDto(sede.Id, sede.Name, sede.City, sede.Address, sede.Phone, sede.IsActive, 0);
    }

    public async Task<SedeDto?> UpdateAsync(Guid id, SaveSedeRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var sede = await _db.Sedes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sede is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        var city = (request.City ?? string.Empty).Trim();
        if (name.Length == 0 || city.Length == 0) { return null; }

        sede.Name = name;
        sede.City = city;
        sede.Address = Clean(request.Address);
        sede.Phone = Clean(request.Phone);
        _audit.Write(actorUserId, "sede.update", nameof(Sede), sede.Id, null, new { sede.Name, sede.City }, sede.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.Resources.CountAsync(r => r.SedeId == sede.Id, cancellationToken);
        return new SedeDto(sede.Id, sede.Name, sede.City, sede.Address, sede.Phone, sede.IsActive, count);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var sede = await _db.Sedes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sede is null) { return false; }
        sede.IsActive = isActive;
        _audit.Write(actorUserId, "sede.set-active", nameof(Sede), sede.Id, null, new { isActive }, sede.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var sede = await _db.Sedes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sede is null) { return false; }
        // Desvincula los asesores de esta sede (no se borran).
        var linked = await _db.Resources.Where(r => r.SedeId == id).ToListAsync(cancellationToken);
        foreach (var r in linked) { r.SedeId = null; }
        _db.Sedes.Remove(sede);
        _audit.Write(actorUserId, "sede.delete", nameof(Sede), sede.Id, new { sede.Name }, null, sede.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
