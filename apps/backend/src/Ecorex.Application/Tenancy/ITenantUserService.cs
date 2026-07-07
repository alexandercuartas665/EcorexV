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

    /// <summary>
    /// El admin del tenant fija una clave nueva para un usuario del tenant (hashea con PBKDF2,
    /// actualiza PlatformUser.PasswordHash y, si estaba Invited, lo pasa a Active). Audita.
    /// Devuelve null si el usuario no existe en el tenant; lanza ArgumentException si la clave
    /// es vacia o tiene menos de 6 caracteres. NUNCA registra la clave en claro.
    /// </summary>
    Task<TenantUserDto?> ResetPasswordAsync(Guid tenantUserId, string newPassword, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edita el DisplayName del PlatformUser vinculado a un usuario del tenant (opcional; null o
    /// vacio lo deja sin nombre). Audita. Devuelve null si el usuario no existe en el tenant.
    /// </summary>
    Task<TenantUserDto?> UpdateProfileAsync(Guid tenantUserId, string? displayName, Guid actorUserId, CancellationToken cancellationToken = default);
}
