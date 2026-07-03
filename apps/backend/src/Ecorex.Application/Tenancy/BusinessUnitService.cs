using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record BusinessUnitDto(Guid Id, string Name, string Color, BusinessUnitModalKind ModalKind, int SortOrder, bool IsActive);
public sealed record SaveBusinessUnitRequest(string Name, string Color, BusinessUnitModalKind ModalKind);

/// <summary>
/// Unidades / canales de negocio (capa 2): clasifican los leads del pipeline, les dan color y deciden
/// que modal abre su tarjeta. Tenant-scoped CRUD + siembra por defecto.
/// </summary>
public interface IBusinessUnitService
{
    Task EnsureDefaultsAsync(Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessUnitDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<BusinessUnitDto?> CreateAsync(SaveBusinessUnitRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<BusinessUnitDto?> UpdateAsync(Guid id, SaveBusinessUnitRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class BusinessUnitService : IBusinessUnitService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditWriter _audit;

    public BusinessUnitService(IApplicationDbContext db, ITenantContext tenant, IAuditWriter audit)
    {
        _db = db; _tenant = tenant; _audit = audit;
    }

    // Unidad de negocio por defecto (generica); cada tenant configura las suyas.
    private static readonly (string Name, string Color, BusinessUnitModalKind Kind)[] Defaults =
    {
        ("General", "#2563eb", BusinessUnitModalKind.Generic)
    };

    public async Task EnsureDefaultsAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return; }
        if (await _db.BusinessUnits.AnyAsync(cancellationToken)) { return; }
        var order = 0;
        foreach (var (name, color, kind) in Defaults)
        {
            _db.BusinessUnits.Add(new BusinessUnit { TenantId = tenantId, Name = name, Color = color, ModalKind = kind, SortOrder = order++, IsActive = true });
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessUnitDto>> ListAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
        => await _db.BusinessUnits.AsNoTracking()
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new BusinessUnitDto(u.Id, u.Name, u.Color, u.ModalKind, u.SortOrder, u.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<BusinessUnitDto?> CreateAsync(SaveBusinessUnitRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }
        var next = (await _db.BusinessUnits.Select(u => (int?)u.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var u = new BusinessUnit
        {
            TenantId = tenantId,
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? "#A03DC9" : request.Color.Trim(),
            ModalKind = request.ModalKind,
            SortOrder = next,
            IsActive = true
        };
        _db.BusinessUnits.Add(u);
        _audit.Write(actorUserId, "business-unit.create", nameof(BusinessUnit), u.Id, null, new { u.Name }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new BusinessUnitDto(u.Id, u.Name, u.Color, u.ModalKind, u.SortOrder, u.IsActive);
    }

    public async Task<BusinessUnitDto?> UpdateAsync(Guid id, SaveBusinessUnitRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var u = await _db.BusinessUnits.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (u is null) { return null; }
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) { return null; }
        u.Name = name;
        u.Color = string.IsNullOrWhiteSpace(request.Color) ? u.Color : request.Color.Trim();
        u.ModalKind = request.ModalKind;
        _audit.Write(actorUserId, "business-unit.update", nameof(BusinessUnit), u.Id, null, new { u.Name }, u.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return new BusinessUnitDto(u.Id, u.Name, u.Color, u.ModalKind, u.SortOrder, u.IsActive);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool isActive, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var u = await _db.BusinessUnits.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (u is null) { return false; }
        u.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var u = await _db.BusinessUnits.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (u is null) { return false; }
        // Desvincula los leads de esta unidad (no se borran).
        var leads = await _db.Leads.Where(l => l.BusinessUnitId == id).ToListAsync(cancellationToken);
        foreach (var l in leads) { l.BusinessUnitId = null; }
        _db.BusinessUnits.Remove(u);
        _audit.Write(actorUserId, "business-unit.delete", nameof(BusinessUnit), u.Id, new { u.Name }, null, u.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
