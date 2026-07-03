using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class TenantUserService : ITenantUserService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;

    public TenantUserService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    public async Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // El filtro global del DbContext limita por el tenant del contexto.
        return await _db.TenantUsers
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new TenantUserDto(u.Id, u.PlatformUserId, u.Email, u.TenantRole, u.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantUserDto?> InviteAsync(InviteTenantUserRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var platformUser = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (platformUser is null)
        {
            platformUser = new PlatformUser
            {
                Email = email,
                DisplayName = request.DisplayName?.Trim(),
                EmailVerified = false,
                AuthProvider = "local",
                Status = string.IsNullOrEmpty(request.Password) ? PlatformUserStatus.Invited : PlatformUserStatus.Active,
                PasswordHash = string.IsNullOrEmpty(request.Password) ? null : _passwordHasher.Hash(request.Password)
            };
            _db.PlatformUsers.Add(platformUser);
        }

        // Filtro global: solo ve miembros del tenant activo.
        var alreadyMember = await _db.TenantUsers.AnyAsync(tu => tu.PlatformUserId == platformUser.Id, cancellationToken);
        if (alreadyMember)
        {
            return null;
        }

        var tenantUser = new TenantUser
        {
            TenantId = tenantId,
            PlatformUserId = platformUser.Id,
            Email = email,
            TenantRole = request.Role,
            Status = PlatformUserStatus.Active
        };
        _db.TenantUsers.Add(tenantUser);

        _audit.Write(actorUserId, "tenant-user.invite", nameof(TenantUser), tenantUser.Id,
            previousValue: null,
            newValue: new { email, request.Role },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenantUser);
    }

    public async Task<TenantUserDto?> ChangeRoleAsync(Guid tenantUserId, TenantRole role, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantUser = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var previous = tenantUser.TenantRole;
        if (previous != role)
        {
            tenantUser.TenantRole = role;
            _audit.Write(actorUserId, "tenant-user.change-role", nameof(TenantUser), tenantUser.Id,
                previousValue: new { Role = previous },
                newValue: new { Role = role },
                tenantId: tenantUser.TenantId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(tenantUser);
    }

    public async Task<TenantUserDto?> SetStatusAsync(Guid tenantUserId, PlatformUserStatus status, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantUser = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var previous = tenantUser.Status;
        if (previous != status)
        {
            tenantUser.Status = status;
            _audit.Write(actorUserId, "tenant-user.set-status", nameof(TenantUser), tenantUser.Id,
                previousValue: new { Status = previous },
                newValue: new { Status = status },
                tenantId: tenantUser.TenantId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(tenantUser);
    }

    private static TenantUserDto Map(TenantUser u) =>
        new(u.Id, u.PlatformUserId, u.Email, u.TenantRole, u.Status);
}
