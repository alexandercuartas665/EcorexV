using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Gestion de asesores (equipo) de la agencia activa (modulo 1.2 extendido). El admin crea
/// asesores por invitacion: se genera un token/link para que el asesor complete su registro
/// (clave + foto). El admin define el alcance de visibilidad de leads del asesor.
/// </summary>
public interface IAdvisorService
{
    Task<IReadOnlyList<AdvisorDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea el asesor en estado invitado y devuelve el token de invitacion. Null si ya es miembro o no hay tenant.</summary>
    Task<AdvisorDto?> CreateAsync(CreateAdvisorRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<AdvisorDto?> UpdateAsync(Guid tenantUserId, UpdateAdvisorRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<AdvisorDto?> SetStatusAsync(Guid tenantUserId, PlatformUserStatus status, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Regenera el token de invitacion (reenviar el link). Null si no existe.</summary>
    Task<AdvisorDto?> ResendInviteAsync(Guid tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Valida una invitacion por token (uso publico, sin tenant). Null si el token no existe.</summary>
    Task<AdvisorInvitationInfo?> GetInvitationAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Completa el registro del asesor (clave + foto). Devuelve el correo para el login, o null si invalida.</summary>
    Task<string?> AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken cancellationToken = default);
}
