using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Configuracion de un buzon para LEER OTPs/tokens que llegan por correo durante una extraccion
/// (modulo Extraccion de datos). Lectura por IMAP (MailKit) con app-password (Gmail / Microsoft-O365).
/// Tenant-scoped; la clave va cifrada (ISecretProtector). El servidor lee el buzon, no el agente navegador.
/// Ideal: un buzon DEDICADO a OTPs. Un buzon por tenant (reusable por los flujos).
/// </summary>
public sealed class OtpMailboxConfig : TenantEntity
{
    /// <summary>Etiqueta ("Buzon OTP Azure").</summary>
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Proveedor: "Microsoft" (outlook.office365.com) / "Gmail" (imap.gmail.com) / "Imap" (manual).</summary>
    public string Proveedor { get; set; } = "Microsoft";

    /// <summary>Host IMAP (p.ej. outlook.office365.com / imap.gmail.com).</summary>
    public string Host { get; set; } = "outlook.office365.com";

    /// <summary>Puerto IMAP (993 SSL).</summary>
    public int Puerto { get; set; } = 993;

    public bool UsarSsl { get; set; } = true;

    /// <summary>Usuario/correo del buzon. En OAuth2 es el BUZON al que se accede (app-only).</summary>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>Modo de autenticacion: "Basic" (IMAP usuario+app-password, Gmail / tenants viejos) u
    /// "OAuth2" (Microsoft 365 moderno: client-credentials app-only; Microsoft desactivo basic-auth).</summary>
    public string AuthMode { get; set; } = "Basic";

    /// <summary>OAuth2: Directorio (tenant) de Azure AD -GUID o dominio- de la app registrada.</summary>
    public string? OauthTenantId { get; set; }

    /// <summary>OAuth2: Application (client) ID de la app registrada en Azure AD. No es secreto.</summary>
    public string? OauthClientId { get; set; }

    /// <summary>Secreto cifrado (ISecretProtector). En "Basic" es la app-password del buzon; en "OAuth2"
    /// es el CLIENT SECRET de la app de Azure AD. Nunca se devuelve en claro.</summary>
    public string? PasswordCifrada { get; set; }

    public bool Activo { get; set; } = true;

    /// <summary>Ultima validacion exitosa (boton "Probar").</summary>
    public DateTimeOffset? UltimaValidacion { get; set; }
}
