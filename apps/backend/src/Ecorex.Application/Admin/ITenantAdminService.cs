using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

public interface ITenantAdminService
{
    Task<TenantDetail> CreateAsync(CreateTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantListItem>> ListAsync(TenantStatus? status = null, string? search = null, CancellationToken cancellationToken = default);
    Task<TenantDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantDetail?> ChangeStatusAsync(Guid id, ChangeTenantStatusRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza el perfil de la agencia (nombre, datos fiscales, logo). Devuelve null si no existe.</summary>
    Task<TenantDetail?> UpdateProfileAsync(Guid id, UpdateTenantProfileRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
