namespace Ecorex.Application.Common;

/// <summary>Resultado del envio de un correo (sin exponer detalles sensibles).</summary>
public sealed record EmailSendResult(bool Ok, string? Error);

/// <summary>
/// Envio de correo transaccional de la plataforma. La implementacion lee la configuracion
/// SMTP global (cifrada) y nunca loggea credenciales. Si el correo no esta habilitado, devuelve Ok=false.
/// </summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
