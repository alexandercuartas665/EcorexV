using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Gestion de usuarios dentro del tenant activo (modulo 1.2). Todas las operaciones quedan
/// acotadas al tenant del contexto (filtro global de consulta + estampado en alta).
/// </summary>
public interface ITenantUserService
{
    Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo o si el usuario ya es miembro del tenant.</summary>
    Task<TenantUserDto?> InviteAsync(InviteTenantUserRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<TenantUserDto?> ChangeRoleAsync(Guid tenantUserId, TenantRole role, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<TenantUserDto?> SetStatusAsync(Guid tenantUserId, PlatformUserStatus status, Guid actorUserId, CancellationToken cancellationToken = default);
}
