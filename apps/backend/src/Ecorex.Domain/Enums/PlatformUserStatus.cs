namespace Ecorex.Domain.Enums;

/// <summary>Estado de una cuenta de usuario de plataforma (Notas dev sec.1.5).</summary>
public enum PlatformUserStatus
{
    Active,
    Invited,
    Blocked,
    Suspended,
    /// <summary>
    /// Cuenta creada por auto-registro pero todavia no activada (el usuario debe ingresar el
    /// codigo enviado por correo). No puede iniciar sesion hasta cambiar a Active.
    /// </summary>
    PendingActivation
}
