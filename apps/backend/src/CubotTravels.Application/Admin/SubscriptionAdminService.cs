using CubotTravels.Application.Common;
using CubotTravels.Domain.Entities;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Admin;

public sealed class SubscriptionAdminService : ISubscriptionAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public SubscriptionAdminService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<SubscriptionDetail?> AssignAsync(AssignSubscriptionRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == request.TenantId, cancellationToken);
        var planExists = await _db.SaasPlans.AnyAsync(p => p.Id == request.PlanId, cancellationToken);
        if (!tenantExists || !planExists)
        {
            return null;
        }

        var startsAt = request.StartsAt ?? DateTimeOffset.UtcNow;
        var periodEnd = request.BillingFrequency == BillingFrequency.Yearly
            ? startsAt.AddYears(1)
            : startsAt.AddMonths(1);

        var subscription = new TenantSubscription
        {
            TenantId = request.TenantId,
            PlanId = request.PlanId,
            Status = SubscriptionStatus.Active,
            BillingFrequency = request.BillingFrequency,
            StartsAt = startsAt,
            CurrentPeriodEndsAt = periodEnd
        };

        _db.TenantSubscriptions.Add(subscription);
        _audit.Write(actorUserId, "subscription.assign", nameof(TenantSubscription), subscription.Id,
            previousValue: null,
            newValue: new { subscription.PlanId, subscription.BillingFrequency, subscription.CurrentPeriodEndsAt },
            tenantId: request.TenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDetail>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _db.TenantSubscriptions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.StartsAt)
            .Select(s => new SubscriptionDetail(s.Id, s.TenantId, s.PlanId, s.Status, s.BillingFrequency, s.StartsAt, s.CurrentPeriodEndsAt, s.GracePeriodEndsAt))
            .ToListAsync(cancellationToken);
    }

    private static SubscriptionDetail Map(TenantSubscription s) =>
        new(s.Id, s.TenantId, s.PlanId, s.Status, s.BillingFrequency, s.StartsAt, s.CurrentPeriodEndsAt, s.GracePeriodEndsAt);
}
