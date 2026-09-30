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

    /// <summary>Usuario/correo del buzon.</summary>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>App-password cifrada (ISecretProtector). Nunca se devuelve en claro.</summary>
    public string? PasswordCifrada { get; set; }

    public bool Activo { get; set; } = true;

    /// <summary>Ultima validacion exitosa (boton "Probar").</summary>
    public DateTimeOffset? UltimaValidacion { get; set; }
}
