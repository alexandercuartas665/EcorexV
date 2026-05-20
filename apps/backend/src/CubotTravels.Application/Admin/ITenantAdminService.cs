using CubotTravels.Domain.Enums;

namespace CubotTravels.Application.Admin;

public interface ITenantAdminService
{
    Task<TenantDetail> CreateAsync(CreateTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantListItem>> ListAsync(TenantStatus? status = null, string? search = null, CancellationToken cancellationToken = default);
    Task<TenantDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantDetail?> ChangeStatusAsync(Guid id, ChangeTenantStatusRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
