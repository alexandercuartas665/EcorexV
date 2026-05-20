using CubotTravels.Domain.Enums;

namespace CubotTravels.Application.Tenancy;

public sealed record TenantUserDto(
    Guid Id,
    Guid PlatformUserId,
    string Email,
    TenantRole TenantRole,
    PlatformUserStatus Status);

public sealed record InviteTenantUserRequest(
    string Email,
    TenantRole Role,
    string? Password = null,
    string? DisplayName = null);

public sealed record ChangeTenantUserRoleRequest(TenantRole Role);

public sealed record ChangeTenantUserStatusRequest(PlatformUserStatus Status);
