using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Credenciales de "Iniciar sesion con Google" (OAuth/OIDC). GLOBAL y singleton, configurable
/// por el Super Admin. El Client Secret se guarda cifrado (ISecretProtector) y nunca se expone.
/// Google es proveedor de identidad; la autorizacion sigue siendo de ECOREX.tareas.
/// </summary>
public class GoogleAuthConfig : BaseEntity
{
    /// <summary>Client ID OAuth (termina en .apps.googleusercontent.com). No es secreto.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client Secret cifrado en reposo.</summary>
    public string? ClientSecretEncrypted { get; set; }

    /// <summary>Si el login con Google esta habilitado en la pantalla de ingreso.</summary>
    public bool IsEnabled { get; set; }
}
