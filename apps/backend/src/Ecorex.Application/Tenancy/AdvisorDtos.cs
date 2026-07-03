using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

public sealed record AdvisorDto(
    Guid Id,
    Guid PlatformUserId,
    string Email,
    string? DisplayName,
    string? AvatarUrl,
    TenantRole Role,
    PlatformUserStatus Status,
    LeadVisibility LeadVisibility,
    bool InvitePending,
    string? InvitationToken,
    DateTimeOffset? InvitationExpiresAt);

public sealed record CreateAdvisorRequest(
    string Email,
    string? DisplayName,
    LeadVisibility LeadVisibility = LeadVisibility.OwnOnly,
    TenantRole Role = TenantRole.Advisor);

public sealed record UpdateAdvisorRequest(
    string? DisplayName,
    LeadVisibility LeadVisibility,
    TenantRole Role);

/// <summary>Informacion publica de una invitacion, para la pagina de aceptacion.</summary>
public sealed record AdvisorInvitationInfo(bool Valid, string Email, string TenantName);

public sealed record AcceptInvitationRequest(
    string Token,
    string Password,
    string? DisplayName = null,
    string? AvatarUrl = null);
