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

    public Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken cancellationToken = default) =>
        ListAsync(includeRemoved: false, cancellationToken);

    public async Task<IReadOnlyList<TenantUserDto>> ListAsync(bool includeRemoved, CancellationToken cancellationToken = default)
    {
        // El filtro global del DbContext limita por el tenant del contexto.
        // DisplayName viene del PlatformUser (join aditivo, ola 3): los dropdowns de
        // asignado muestran el nombre legible en vez del email.
        var query = _db.TenantUsers.AsNoTracking();
        if (!includeRemoved)
        {
            // Baja logica: un usuario eliminado no debe aparecer en ningun selector de asignacion.
            query = query.Where(u => u.Status != PlatformUserStatus.Removed);
        }

        return await query
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

    public async Task<(bool Ok, string? Error)> RemoveAsync(Guid tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (actor, roleError) = await ResolveAdminActorAsync(actorUserId, cancellationToken);
        if (actor is null) { return (false, roleError); }

        // Filtro global: solo alcanza usuarios del tenant activo.
        var target = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (target is null) { return (false, "El usuario no pertenece a esta empresa."); }

        // Salvaguarda 1: nadie se elimina a si mismo (se quedaria operando con una cuenta dada de baja).
        if (target.Id == actor.Id || target.PlatformUserId == actor.PlatformUserId)
        {
            return (false, "No puedes eliminarte a ti mismo.");
        }

        if (target.Status == PlatformUserStatus.Removed)
        {
            return (false, "El usuario ya estaba eliminado.");
        }

        // Salvaguarda 2: la empresa no puede quedarse sin quien la administre.
        if (target.TenantRole is TenantRole.Owner or TenantRole.Admin)
        {
            var targetId = target.Id;
            var otherAdmins = await _db.TenantUsers.CountAsync(tu =>
                tu.Id != targetId
                && (tu.TenantRole == TenantRole.Owner || tu.TenantRole == TenantRole.Admin)
                && tu.Status == PlatformUserStatus.Active,
                cancellationToken);
            if (otherAdmins == 0)
            {
                return (false, "No se puede eliminar al ultimo propietario/administrador activo de la empresa.");
            }
        }

        var previous = target.Status;
        // Baja LOGICA: la fila sobrevive porque de ella cuelgan tareas, notas y auditoria.
        target.Status = PlatformUserStatus.Removed;
        // Una invitacion pendiente de un usuario eliminado no debe poder canjearse.
        target.InvitationToken = null;
        target.InvitationExpiresAt = null;

        _audit.Write(actorUserId, "tenant-user.remove", nameof(TenantUser), target.Id,
            previousValue: new { Status = previous, target.Email, target.TenantRole },
            newValue: new { Status = PlatformUserStatus.Removed },
            tenantId: target.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> RestoreAsync(Guid tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (actor, roleError) = await ResolveAdminActorAsync(actorUserId, cancellationToken);
        if (actor is null) { return (false, roleError); }

        var target = await _db.TenantUsers.FirstOrDefaultAsync(tu => tu.Id == tenantUserId, cancellationToken);
        if (target is null) { return (false, "El usuario no pertenece a esta empresa."); }
        if (target.Status != PlatformUserStatus.Removed) { return (false, "El usuario no esta eliminado."); }

        target.Status = PlatformUserStatus.Active;

        _audit.Write(actorUserId, "tenant-user.restore", nameof(TenantUser), target.Id,
            previousValue: new { Status = PlatformUserStatus.Removed },
            newValue: new { Status = PlatformUserStatus.Active },
            tenantId: target.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    /// <summary>
    /// Candado de rol en el SERVIDOR: la baja de usuarios solo la puede ejecutar un Owner/Admin
    /// del tenant activo. La UI tambien oculta la accion, pero eso es cosmetico; aqui esta el
    /// permiso real (defensa en profundidad, mismo patron que LeadService.PurgeArchivedHistory).
    /// </summary>
    private async Task<(TenantUser? Actor, string? Error)> ResolveAdminActorAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        // El actor se identifica por su PlatformUserId; se prefiere el del contexto de ejecucion.
        var platformUserId = _tenantContext.UserId ?? actorUserId;
        if (platformUserId == Guid.Empty) { return (null, "No hay un usuario autenticado."); }

        var actor = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.PlatformUserId == platformUserId, cancellationToken);
        if (actor is null || actor.TenantRole is not (TenantRole.Owner or TenantRole.Admin))
        {
            return (null, "Solo un administrador de la empresa puede eliminar usuarios.");
        }

        return (actor, null);
    }

    private static TenantUserDto Map(TenantUser u, string? displayName = null) =>
        new(u.Id, u.PlatformUserId, u.Email, u.TenantRole, u.Status, displayName);
}
