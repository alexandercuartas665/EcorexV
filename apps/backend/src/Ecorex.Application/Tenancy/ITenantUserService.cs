using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Gestion de usuarios dentro del tenant activo (modulo 1.2). Todas las operaciones quedan
/// acotadas al tenant del contexto (filtro global de consulta + estampado en alta).
/// </summary>
public interface ITenantUserService
{
    /// <summary>Usuarios vigentes del tenant (excluye los eliminados logicamente).</summary>
    Task<IReadOnlyList<TenantUserDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Igual que <see cref="ListAsync(CancellationToken)"/> pero permite incluir los usuarios
    /// eliminados (Status = Removed). Solo la pantalla de administracion de usuarios los pide,
    /// para poder restaurarlos; los selectores de asignacion NUNCA deben verlos.
    /// </summary>
    Task<IReadOnlyList<TenantUserDto>> ListAsync(bool includeRemoved, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// "Elimina" un usuario del tenant como BAJA LOGICA (Status = Removed): no borra la fila
    /// porque de ella cuelgan tareas, notas y auditoria. Exige que quien opera sea Owner/Admin
    /// del tenant y aplica dos salvaguardas: nadie se elimina a si mismo y no se puede eliminar
    /// al ultimo propietario/administrador activo. Audita la accion.
    /// Devuelve (false, motivo) si alguna validacion falla.
    /// </summary>
    Task<(bool Ok, string? Error)> RemoveAsync(Guid tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deshace la baja logica: devuelve el usuario a Active. Mismo candado de rol que
    /// <see cref="RemoveAsync"/>. Audita.
    /// </summary>
    Task<(bool Ok, string? Error)> RestoreAsync(Guid tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);
}
