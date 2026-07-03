using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Token de reseteo de contrasena (autogestion). Global. Se guarda el HASH del token, no el
/// valor en claro; el valor original solo viaja en el enlace enviado por correo. Un solo uso
/// y con expiracion.
/// </summary>
public class PasswordResetToken : BaseEntity
{
    public Guid PlatformUserId { get; set; }

    /// <summary>SHA-256 (hex) del token enviado por correo. La busqueda se hace por este hash.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Marca de uso: una vez usado, no se puede reutilizar.</summary>
    public DateTimeOffset? UsedAt { get; set; }
}
