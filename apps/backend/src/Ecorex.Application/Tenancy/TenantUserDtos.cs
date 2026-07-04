using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

public sealed record TenantUserDto(
    Guid Id,
    Guid PlatformUserId,
    string Email,
    TenantRole TenantRole,
    PlatformUserStatus Status,
    // Ola 3 UI (cambio aditivo): nombre legible para los dropdowns de asignado.
    // Sale de PlatformUser.DisplayName; si el usuario no lo tiene configurado queda
    // null y la UI deriva un nombre de la parte local del email (capitalizada).
    string? DisplayName = null);

public sealed record InviteTenantUserRequest(
    string Email,
    TenantRole Role,
    string? Password = null,
    string? DisplayName = null);

public sealed record ChangeTenantUserRoleRequest(TenantRole Role);

public sealed record ChangeTenantUserStatusRequest(PlatformUserStatus Status);
