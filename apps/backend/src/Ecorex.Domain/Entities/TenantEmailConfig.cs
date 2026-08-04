using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Servidor de correo saliente PROPIO de un tenant (agencia). A diferencia de <see cref="EmailConfig"/>
/// (global de plataforma, para correos transaccionales del SaaS), este permite que cada empresa envie
/// sus correos de atencion a clientes desde SU cuenta/dominio. Tenant-scoped (filtro global); un
/// registro por tenant. La clave se guarda cifrada (ISecretProtector) y nunca se expone ni se loggea.
///
/// El envio (SmtpEmailSender) prefiere esta config cuando el tenant activo la tiene habilitada; si no,
/// cae al servidor global de la plataforma.
/// </summary>
public class TenantEmailConfig : TenantEntity
{
    /// <summary>Host SMTP (p.ej. smtp.office365.com, smtp.gmail.com).</summary>
    public string? SmtpHost { get; set; }

    /// <summary>Puerto SMTP (587 STARTTLS, 465 SSL).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Usuario SMTP.</summary>
    public string? SmtpUser { get; set; }

    /// <summary>Clave/secreto SMTP cifrado en reposo.</summary>
    public string? SmtpPasswordEncrypted { get; set; }

    /// <summary>Usar SSL/TLS al conectar.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>Direccion remitente (From).</summary>
    public string? FromEmail { get; set; }

    /// <summary>Nombre visible del remitente.</summary>
    public string? FromName { get; set; }

    /// <summary>Si esta habilitado el envio de correo con esta config del tenant.</summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastValidatedAt { get; set; }
}
