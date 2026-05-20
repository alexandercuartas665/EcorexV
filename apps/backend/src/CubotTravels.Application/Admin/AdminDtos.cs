using CubotTravels.Domain.Enums;

namespace CubotTravels.Application.Admin;

// --- Tenants ---
public sealed record CreateTenantRequest(
    string Name,
    string? LegalName = null,
    string? TaxId = null,
    string? Country = null,
    string? Currency = null,
    TenantKind Kind = TenantKind.Standard);

public sealed record ChangeTenantStatusRequest(TenantStatus Status, string? Reason = null);

public sealed record TenantListItem(
    Guid Id,
    string Name,
    TenantStatus Status,
    TenantKind Kind,
    string? Country,
    string? Currency,
    DateTimeOffset CreatedAt);

public sealed record TenantDetail(
    Guid Id,
    string Name,
    string? LegalName,
    string? TaxId,
    string? Country,
    string? Currency,
    TenantStatus Status,
    TenantKind Kind,
    DateTimeOffset CreatedAt);

// --- Plans ---
public sealed record PlanLimitInput(
    string LimitKey,
    long LimitValue,
    string? LimitUnit = null,
    LimitEnforcementMode EnforcementMode = LimitEnforcementMode.Hard);

public sealed record CreatePlanRequest(
    string Name,
    string? Description,
    decimal? MonthlyPrice,
    decimal? YearlyPrice,
    string? Currency,
    IReadOnlyList<PlanLimitInput> Limits);

public sealed record PlanLimitDto(string LimitKey, long LimitValue, string? LimitUnit, LimitEnforcementMode EnforcementMode);

public sealed record PlanDetail(
    Guid Id,
    string Name,
    string? Description,
    decimal? MonthlyPrice,
    decimal? YearlyPrice,
    string? Currency,
    bool IsActive,
    IReadOnlyList<PlanLimitDto> Limits);

// --- Subscriptions ---
public sealed record AssignSubscriptionRequest(
    Guid TenantId,
    Guid PlanId,
    BillingFrequency BillingFrequency,
    DateTimeOffset? StartsAt = null);

public sealed record SubscriptionDetail(
    Guid Id,
    Guid TenantId,
    Guid PlanId,
    SubscriptionStatus Status,
    BillingFrequency BillingFrequency,
    DateTimeOffset StartsAt,
    DateTimeOffset CurrentPeriodEndsAt,
    DateTimeOffset? GracePeriodEndsAt);

// --- Payments ---
public sealed record RegisterPaymentRequest(
    Guid TenantId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    DateTimeOffset BillingPeriodStart,
    DateTimeOffset BillingPeriodEnd,
    string? ProviderReference = null);

public sealed record PaymentDetail(
    Guid Id,
    Guid TenantId,
    Guid SubscriptionId,
    string Provider,
    string? ProviderReference,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    DateTimeOffset BillingPeriodStart,
    DateTimeOffset BillingPeriodEnd,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt);
