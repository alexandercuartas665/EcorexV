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
        // DisplayName viene del PlatformUser (join aditivo, ola 3): los dropdowns de
        // asignado muestran el nombre legible en vez del email.
        return await _db.TenantUsers
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Join(_db.PlatformUsers.AsNoTracking(),
                tu => tu.PlatformUserId, pu => pu.Id,
                (tu, pu) => new TenantUserDto(tu.Id, tu.PlatformUserId, tu.Email, tu.TenantRole, tu.Status, pu.DisplayName))
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

    public async Task<TenantUserDto?> ResetPasswordAsync(Guid tenantUserId, string newPassword, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            throw new ArgumentException("La clave debe tener al menos 6 caracteres.", nameof(newPassword));
        }

        // Filtro global: solo alcanza usuarios del tenant activo.
        var tenantUser = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var platformUser = await _db.PlatformUsers.FirstOrDefaultAsync(pu => pu.Id == tenantUser.PlatformUserId, cancellationToken);
        if (platformUser is null)
        {
            return null;
        }

        platformUser.PasswordHash = _passwordHasher.Hash(newPassword);
        // Un usuario invitado que ya recibe clave del admin queda activo (puede iniciar sesion).
        var reactivated = false;
        if (platformUser.Status == PlatformUserStatus.Invited)
        {
            platformUser.Status = PlatformUserStatus.Active;
            reactivated = true;
        }
        if (tenantUser.Status == PlatformUserStatus.Invited)
        {
            tenantUser.Status = PlatformUserStatus.Active;
            reactivated = true;
        }

        // Auditoria SIN la clave (solo el hecho y si reactivo la cuenta).
        _audit.Write(actorUserId, "tenant-user.reset-password", nameof(TenantUser), tenantUser.Id,
            previousValue: null,
            newValue: new { Reactivated = reactivated },
            tenantId: tenantUser.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(tenantUser, platformUser.DisplayName);
    }

    public async Task<TenantUserDto?> UpdateProfileAsync(Guid tenantUserId, string? displayName, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantUser = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (tenantUser is null)
        {
            return null;
        }

        var platformUser = await _db.PlatformUsers.FirstOrDefaultAsync(pu => pu.Id == tenantUser.PlatformUserId, cancellationToken);
        if (platformUser is null)
        {
            return null;
        }

        var normalized = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        var previous = platformUser.DisplayName;
        if (previous != normalized)
        {
            platformUser.DisplayName = normalized;
            _audit.Write(actorUserId, "tenant-user.update-profile", nameof(TenantUser), tenantUser.Id,
                previousValue: new { DisplayName = previous },
                newValue: new { DisplayName = normalized },
                tenantId: tenantUser.TenantId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Map(tenantUser, normalized);
    }

    private static TenantUserDto Map(TenantUser u, string? displayName = null) =>
        new(u.Id, u.PlatformUserId, u.Email, u.TenantRole, u.Status, displayName);
}
