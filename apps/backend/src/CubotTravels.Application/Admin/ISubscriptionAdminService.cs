namespace CubotTravels.Application.Admin;

public interface ISubscriptionAdminService
{
    /// <summary>Devuelve null si el tenant o el plan no existen.</summary>
    Task<SubscriptionDetail?> AssignAsync(AssignSubscriptionRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubscriptionDetail>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
