using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

/// <summary>
/// Gestion del equipo de plataforma: usuarios con PlatformRole (Super Admins, Soporte,
/// Finanzas, Tecnico, Auditor, Analista). Operaciones permitidas solo a SuperAdmin.
/// </summary>
public interface IPlatformOperatorService
{
    Task<IReadOnlyList<PlatformOperatorDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea un nuevo operador con email + clave asignada manualmente. Auditoria queda con actorUserId.</summary>
    Task<(PlatformOperatorDto? Created, string? Error)> CreateAsync(CreatePlatformOperatorRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza display name, rol y estado. No cambia email ni clave (esos tienen endpoints separados).</summary>
    Task<PlatformOperatorDto?> UpdateAsync(Guid operatorId, UpdatePlatformOperatorRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Cambia la clave de un operador. Usa el hasher del sistema.</summary>
    Task<bool> ChangePasswordAsync(ChangeOperatorPasswordRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Elimina un operador. No permite eliminar al actorUserId (a si mismo).</summary>
    Task<(bool Deleted, string? Error)> DeleteAsync(Guid operatorId, Guid actorUserId, CancellationToken cancellationToken = default);
}
