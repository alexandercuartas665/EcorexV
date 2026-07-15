using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

public sealed class TenantAdminService : ITenantAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly Ecorex.Application.MenuConfig.IMenuProvisioningService _menuProvisioning;

    public TenantAdminService(
        IApplicationDbContext db,
        IAuditWriter audit,
        Ecorex.Application.MenuConfig.IMenuProvisioningService menuProvisioning)
    {
        _db = db;
        _audit = audit;
        _menuProvisioning = menuProvisioning;
    }

    public async Task<TenantDetail> CreateAsync(CreateTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenant = new Tenant
        {
            // Convencion del sistema: el NOMBRE de la empresa cliente va en MAYUSCULA.
            Name = NormalizeName(request.Name),
            LegalName = request.LegalName?.Trim(),
            TaxId = request.TaxId?.Trim(),
            Country = request.Country?.Trim(),
            Currency = request.Currency?.Trim(),
            Status = TenantStatus.Trial,
            Kind = request.Kind
        };

        _db.Tenants.Add(tenant);
        _audit.Write(actorUserId, "tenant.create", nameof(Tenant), tenant.Id,
            previousValue: null,
            newValue: new { tenant.Name, tenant.Status, tenant.Kind },
            tenantId: tenant.Id);

        await _db.SaveChangesAsync(cancellationToken);

        // El tenant nace CON menu: vista "Completo" (por defecto) con el arbol canonico. Sin esto el
        // cliente quedaba sin ninguna vista y sus usuarios no veian nada (bug real detectado en prod).
        await _menuProvisioning.EnsureDefaultMenuAsync(tenant.Id, cancellationToken);

        return Map(tenant);
    }

    /// <summary>Nombre de empresa normalizado a MAYUSCULA (convencion del sistema).</summary>
    internal static string NormalizeName(string name) => name.Trim().ToUpperInvariant();

    public async Task<IReadOnlyList<TenantListItem>> ListAsync(TenantStatus? status = null, string? search = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Tenants.AsNoTracking();

        if (status is TenantStatus s)
        {
            query = query.Where(t => t.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(term));
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TenantListItem(t.Id, t.Name, t.Status, t.Kind, t.Country, t.Currency, t.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return tenant is null ? null : Map(tenant);
    }

    public async Task<TenantDetail?> ChangeStatusAsync(Guid id, ChangeTenantStatusRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var previousStatus = tenant.Status;
        if (previousStatus == request.Status)
        {
            return Map(tenant);
        }

        tenant.Status = request.Status;
        _audit.Write(actorUserId, "tenant.change-status", nameof(Tenant), tenant.Id,
            previousValue: new { Status = previousStatus },
            newValue: new { Status = request.Status },
            tenantId: tenant.Id,
            reason: request.Reason);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenant);
    }

    public async Task<TenantDetail?> UpdateProfileAsync(Guid id, UpdateTenantProfileRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        tenant.Name = request.Name.Trim();
        tenant.LegalName = request.LegalName?.Trim();
        tenant.TaxId = request.TaxId?.Trim();
        tenant.Country = request.Country?.Trim();
        tenant.Currency = request.Currency?.Trim();
        tenant.City = Normalize(request.City);
        tenant.Address = Normalize(request.Address);
        tenant.Phone = Normalize(request.Phone);
        tenant.Email = Normalize(request.Email);
        tenant.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? tenant.LogoUrl : request.LogoUrl.Trim();

        _audit.Write(actorUserId, "tenant.profile.update", nameof(Tenant), tenant.Id,
            previousValue: null,
            newValue: new { tenant.Name, tenant.LegalName, tenant.TaxId, tenant.Country, tenant.Currency, tenant.City, tenant.Address, tenant.Phone, tenant.Email, HasLogo = tenant.LogoUrl is not null },
            tenantId: tenant.Id);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenant);
    }

    public async Task<IReadOnlyList<TenantUserListItem>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        // Ficha de empresa del operador de plataforma (modulo 000072): lectura cross-tenant
        // ACOTADA a este tenant. TenantUser es tenant-scoped, asi que se ignora el filtro
        // global y se filtra explicitamente por el tenant pedido (ADR-0026). El unico acceso
        // cross-tenant permitido es el del operador de plataforma (protegido por policy).
        return await _db.TenantUsers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .Select(u => new TenantUserListItem(u.Id, u.Email, u.TenantRole, u.Status))
            .ToListAsync(cancellationToken);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TenantDetail Map(Tenant t) =>
        new(t.Id, t.Name, t.LegalName, t.TaxId, t.Country, t.Currency, t.Status, t.Kind, t.CreatedAt, t.LogoUrl,
            t.City, t.Address, t.Phone, t.Email);
}
