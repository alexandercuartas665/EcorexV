using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

/// <summary>
/// Operador de plataforma (PlatformUser con PlatformRole != null): super admins, soporte,
/// finanzas, tecnicos, auditores y analistas. NO incluye a los usuarios "regulares" de
/// agencias (esos viven en TenantUserService). Modulo Equipo plataforma.
/// </summary>
public sealed record PlatformOperatorDto(
    Guid Id,
    string Email,
    string? DisplayName,
    PlatformRole Role,
    PlatformUserStatus Status,
    bool EmailVerified,
    string AuthProvider,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

public sealed record CreatePlatformOperatorRequest(
    string Email,
    string? DisplayName,
    PlatformRole Role,
    string Password);

public sealed record UpdatePlatformOperatorRequest(
    string? DisplayName,
    PlatformRole Role,
    PlatformUserStatus Status);

public sealed record ChangeOperatorPasswordRequest(
    Guid OperatorId,
    string NewPassword);
