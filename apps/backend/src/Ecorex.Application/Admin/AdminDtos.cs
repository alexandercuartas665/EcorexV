using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

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
    DateTimeOffset CreatedAt,
    string? LogoUrl = null,
    // Perfil de contacto/domicilio (migracion AddTenantProfile, ADR-0026: ficha 000072).
    string? City = null,
    string? Address = null,
    string? Phone = null,
    string? Email = null);

/// <summary>Actualizacion del perfil de la agencia por su propio administrador (modulo 1.6)
/// y por el operador de plataforma desde la ficha de empresa (modulo 000072, ADR-0026).</summary>
public sealed record UpdateTenantProfileRequest(
    string Name,
    string? LegalName,
    string? TaxId,
    string? Country,
    string? Currency,
    string? LogoUrl,
    // Perfil de contacto/domicilio (ADR-0026).
    string? City = null,
    string? Address = null,
    string? Phone = null,
    string? Email = null);

/// <summary>Un usuario del tenant tal como lo ve el operador de plataforma en la ficha de empresa
/// (modulo 000072, solo lectura). Cross-tenant acotado y auditado (ADR-0026).</summary>
public sealed record TenantUserListItem(
    Guid Id,
    string Email,
    TenantRole TenantRole,
    PlatformUserStatus Status);

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
    DateTimeOffset? GracePeriodEndsAt,
    bool AutoRenew = false,
    string? PaymentMethodLabel = null);

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

// --- Auditoria ---
public sealed record AuditLogListItem(
    DateTimeOffset OccurredAt,
    AuditActorType ActorType,
    Guid ActorUserId,
    string? TenantName,
    string ActionName,
    string EntityName,
    string? Reason);
