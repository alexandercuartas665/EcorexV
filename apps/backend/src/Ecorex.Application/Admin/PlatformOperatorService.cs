using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

/// <summary>
/// Implementacion del modulo "Equipo plataforma": CRUD de PlatformUsers con PlatformRole asignado.
/// Solo SuperAdmin debe llamar a estas operaciones (la politica se aplica en la capa Web/Api).
/// </summary>
public sealed class PlatformOperatorService : IPlatformOperatorService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditWriter _audit;

    public PlatformOperatorService(IApplicationDbContext db, IPasswordHasher passwordHasher, IAuditWriter audit)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PlatformOperatorDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters porque PlatformUser es global (no tenant-scoped) y queremos verlos todos.
        return await _db.PlatformUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.PlatformRole != null)
            .OrderBy(u => u.PlatformRole)
            .ThenBy(u => u.Email)
            .Select(u => new PlatformOperatorDto(
                u.Id,
                u.Email,
                u.DisplayName,
                u.PlatformRole!.Value,
                u.Status,
                u.EmailVerified,
                u.AuthProvider,
                u.LastLoginAt,
                u.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<(PlatformOperatorDto? Created, string? Error)> CreateAsync(CreatePlatformOperatorRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return (null, "El correo es obligatorio.");
        }
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            return (null, "La clave debe tener al menos 6 caracteres.");
        }

        if (await _db.PlatformUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken))
        {
            return (null, "Ya existe un usuario con ese correo.");
        }

        var op = new PlatformUser
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            EmailVerified = true, // creado por admin: confiable
            Status = PlatformUserStatus.Active,
            AuthProvider = "local",
            PasswordHash = _passwordHasher.Hash(request.Password),
            PlatformRole = request.Role
        };
        _db.PlatformUsers.Add(op);

        _audit.Write(actorUserId, "platform_operator.create", nameof(PlatformUser), op.Id,
            previousValue: null,
            newValue: new { op.Email, op.DisplayName, Role = request.Role.ToString() });

        await _db.SaveChangesAsync(cancellationToken);

        return (new PlatformOperatorDto(op.Id, op.Email, op.DisplayName, request.Role, op.Status,
            op.EmailVerified, op.AuthProvider, op.LastLoginAt, op.CreatedAt), null);
    }

    public async Task<PlatformOperatorDto?> UpdateAsync(Guid operatorId, UpdatePlatformOperatorRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var op = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == operatorId, cancellationToken);
        if (op is null || op.PlatformRole is null) { return null; }

        var previous = new { op.DisplayName, Role = op.PlatformRole?.ToString(), Status = op.Status.ToString() };

        op.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        op.PlatformRole = request.Role;
        op.Status = request.Status;

        _audit.Write(actorUserId, "platform_operator.update", nameof(PlatformUser), op.Id,
            previousValue: previous,
            newValue: new { op.DisplayName, Role = request.Role.ToString(), Status = request.Status.ToString() });

        await _db.SaveChangesAsync(cancellationToken);

        return new PlatformOperatorDto(op.Id, op.Email, op.DisplayName, op.PlatformRole.Value, op.Status,
            op.EmailVerified, op.AuthProvider, op.LastLoginAt, op.CreatedAt);
    }

    public async Task<bool> ChangePasswordAsync(ChangeOperatorPasswordRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6) { return false; }
        var op = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.OperatorId, cancellationToken);
        if (op is null || op.PlatformRole is null) { return false; }

        op.PasswordHash = _passwordHasher.Hash(request.NewPassword);

        _audit.Write(actorUserId, "platform_operator.change_password", nameof(PlatformUser), op.Id,
            previousValue: null,
            newValue: new { op.Email });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(bool Deleted, string? Error)> DeleteAsync(Guid operatorId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (operatorId == actorUserId)
        {
            return (false, "No puedes eliminarte a ti mismo.");
        }

        var op = await _db.PlatformUsers.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == operatorId, cancellationToken);
        if (op is null || op.PlatformRole is null)
        {
            return (false, "Operador no encontrado.");
        }

        // Verifica que no sea el ultimo SuperAdmin activo.
        if (op.PlatformRole == PlatformRole.SuperAdmin)
        {
            var otherSuperAdmins = await _db.PlatformUsers.IgnoreQueryFilters()
                .CountAsync(u => u.Id != op.Id && u.PlatformRole == PlatformRole.SuperAdmin && u.Status == PlatformUserStatus.Active, cancellationToken);
            if (otherSuperAdmins == 0)
            {
                return (false, "No se puede eliminar el ultimo Super Admin activo.");
            }
        }

        // Si el operador pertenece a algun tenant (TenantUser), no se elimina; solo se quita el PlatformRole.
        // El usuario seguira siendo miembro de las agencias donde fue invitado.
        var hasTenantMembership = await _db.TenantUsers.IgnoreQueryFilters().AnyAsync(tu => tu.PlatformUserId == op.Id, cancellationToken);
        if (hasTenantMembership)
        {
            op.PlatformRole = null;
            _audit.Write(actorUserId, "platform_operator.revoke_role", nameof(PlatformUser), op.Id,
                previousValue: new { Role = op.PlatformRole?.ToString() },
                newValue: null);
        }
        else
        {
            _db.PlatformUsers.Remove(op);
            _audit.Write(actorUserId, "platform_operator.delete", nameof(PlatformUser), op.Id,
                previousValue: new { op.Email, Role = op.PlatformRole?.ToString() },
                newValue: null);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }
}
