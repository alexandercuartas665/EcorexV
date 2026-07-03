using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

public sealed record OnboardTenantRequest(
    string TenantName,
    string AdminEmail,
    string AdminPassword,
    string? AdminDisplayName = null,
    string? Country = null,
    string? Currency = null,
    Guid? PlanId = null,
    BillingFrequency BillingFrequency = BillingFrequency.Monthly,
    // Cuando viene un subject de Google, el admin se crea sin clave (login via Google).
    string? GoogleSubject = null);

public sealed record OnboardingResult(
    Guid TenantId,
    string TenantName,
    Guid AdminUserId,
    string AdminEmail,
    Guid? SubscriptionId);

public sealed record OnboardingOutcome(bool Success, OnboardingResult? Result, string? Error);

public interface IOnboardingService
{
    Task<OnboardingOutcome> OnboardAsync(OnboardTenantRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
