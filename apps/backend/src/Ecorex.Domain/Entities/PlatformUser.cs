using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Usuario de plataforma (identidad). Puede operar uno o varios tenants via TenantUser
/// y/o ser operador del SaaS via PlatformRole. Entidad global. Ver Notas dev sec.1.5.
/// </summary>
public class PlatformUser : BaseEntity
{
    public string Email { get; set; } = null!;
    public bool EmailVerified { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? GoogleSubject { get; set; }
    public string AuthProvider { get; set; } = "local";

    /// <summary>Hash PBKDF2 de la clave para login local. Null si el usuario solo usa proveedor externo.</summary>
    public string? PasswordHash { get; set; }
    public PlatformUserStatus Status { get; set; } = PlatformUserStatus.Invited;
    public PlatformRole? PlatformRole { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
