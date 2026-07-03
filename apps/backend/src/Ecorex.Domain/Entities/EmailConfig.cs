using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Servidor de correo saliente de la plataforma (Super Admin SaaS). GLOBAL y singleton.
/// Sirve para enviar correos transaccionales (reseteo de contrasena, etc.). Compatible con
/// cualquier SMTP (Gmail, SendGrid via SMTP, Mailgun, etc.). La clave se guarda cifrada
/// (ISecretProtector) y nunca se expone ni se loggea.
/// </summary>
public class EmailConfig : BaseEntity
{
    /// <summary>Host SMTP (p.ej. smtp.sendgrid.net, smtp.gmail.com).</summary>
    public string? SmtpHost { get; set; }

    /// <summary>Puerto SMTP (587 STARTTLS, 465 SSL).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Usuario SMTP (en SendGrid es la palabra "apikey").</summary>
    public string? SmtpUser { get; set; }

    /// <summary>Clave/secreto SMTP cifrado en reposo.</summary>
    public string? SmtpPasswordEncrypted { get; set; }

    /// <summary>Usar SSL/TLS al conectar.</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>Direccion remitente (From).</summary>
    public string? FromEmail { get; set; }

    /// <summary>Nombre visible del remitente.</summary>
    public string? FromName { get; set; }

    /// <summary>Si esta habilitado el envio de correo.</summary>
    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastValidatedAt { get; set; }
}
