using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Codigo de activacion de cuenta de auto-registro. Global. Se guarda el HASH del codigo, no el
/// valor en claro; el codigo original solo viaja en el correo de bienvenida. Un solo uso y con
/// expiracion. Mismo patron que PasswordResetToken.
/// </summary>
public class AccountActivationCode : BaseEntity
{
    public Guid PlatformUserId { get; set; }

    /// <summary>SHA-256 (hex) del codigo enviado por correo. La validacion compara por hash.</summary>
    public string CodeHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Marca de uso: una vez usado, no se puede reutilizar.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}
