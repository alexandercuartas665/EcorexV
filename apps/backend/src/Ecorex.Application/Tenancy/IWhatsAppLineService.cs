using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Gestion de lineas WhatsApp del tenant activo (modulo 1.4). Tenant-scoped. La conexion real
/// con Evolution (QR/sesion) se integrara via el Evolution Connector en una fase posterior.
/// </summary>
public interface IWhatsAppLineService
{
    Task<IReadOnlyList<WhatsAppLineDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Devuelve null si no hay tenant activo.</summary>
    Task<WhatsAppLineDto?> CreateAsync(CreateWhatsAppLineRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<WhatsAppLineDto?> ChangeStatusAsync(Guid lineId, WhatsAppLineStatus status, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Asigna (o desasigna con null) la linea a un usuario del tenant. Devuelve null si la linea no existe o el usuario no pertenece al tenant.</summary>
    Task<WhatsAppLineDto?> AssignAsync(Guid lineId, Guid? tenantUserId, Guid actorUserId, CancellationToken cancellationToken = default);
}
