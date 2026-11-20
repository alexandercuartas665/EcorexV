namespace Ecorex.Application.Common;

/// <summary>Resultado del envio de un correo (sin exponer detalles sensibles).</summary>
/// <remarks><c>MessageId</c> = id que dio el servidor al aceptar el correo (SendGrid: "queued as X"), si lo dio.</remarks>
public sealed record EmailSendResult(bool Ok, string? Error, string? MessageId = null);

/// <summary>
/// Remitente de UN envio (ADR-0185), p.ej. el remitente comercial de una campana del gestor de contactos.
/// <paramref name="FromName"/> = nombre visible; <paramref name="FromEmail"/> = direccion remitente, que solo se
/// usa si es del MISMO dominio que el remitente configurado en el servidor (el dominio autenticado en
/// SendGrid/Office365): si no, se ignora como From y va como Reply-To, para no caer en spam ni ser rechazado;
/// <paramref name="ReplyTo"/> = a donde llegan las respuestas.
/// <paramref name="CustomArgs"/> = argumentos de correlacion que el proveedor devuelve en sus eventos de entrega
/// (ADR-0188; en SendGrid via cabecera X-SMTPAPI unique_args), p.ej. el id de la ejecucion del motor. Sin datos
/// personales: solo ids.
/// </summary>
public sealed record EmailSenderOverride(string? FromName, string? FromEmail, string? ReplyTo,
    IReadOnlyDictionary<string, string>? CustomArgs = null);

/// <summary>
/// Envio de correo transaccional de la plataforma. La implementacion lee la configuracion
/// SMTP global (cifrada) y nunca loggea credenciales. Si el correo no esta habilitado, devuelve Ok=false.
/// </summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>Envio con remitente propio (ADR-0185). Por defecto ignora el remitente (implementaciones de prueba).</summary>
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, EmailSenderOverride? sender, CancellationToken cancellationToken = default)
        => SendAsync(toEmail, subject, htmlBody, cancellationToken);
}
