using Ecorex.Application.Common;
using Ecorex.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace Ecorex.Infrastructure.Email;

/// <summary>
/// Envio de correo via SMTP con MailKit (ADR-0170). Prefiere el servidor PROPIO del tenant activo
/// (<see cref="TenantEmailConfig"/>) cuando esta habilitado, para que cada empresa envie sus correos
/// de atencion desde su cuenta/dominio; si el tenant no tiene config (o no hay tenant en contexto,
/// p.ej. reseteo de clave), cae al servidor GLOBAL de plataforma (<see cref="EmailConfig"/>).
/// Compatible con Office365, Gmail, SendGrid (SMTP), etc. No persiste ni loggea la clave.
///
/// Por que MailKit y no System.Net.Mail.SmtpClient: SmtpClient solo habla STARTTLS y NO soporta TLS
/// implicito, asi que contra el puerto 465 se quedaba colgado hasta el timeout (caso SendGrid:465 de la
/// config global, 2026-11-15). MailKit elige el modo TLS segun el puerto (<see cref="ResolveSocketOptions"/>).
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    /// <summary>Tope de espera por operacion SMTP (conectar, autenticar, enviar). Un SMTP inalcanzable no
    /// debe colgar la operacion que dispara el correo (p.ej. crear una actividad).</summary>
    private const int TimeoutMs = 15000;

    private readonly EcorexDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(EcorexDbContext db, ISecretProtector secretProtector, ILogger<SmtpEmailSender>? logger = null)
    {
        _db = db;
        _secretProtector = secretProtector;
        _logger = logger ?? NullLogger<SmtpEmailSender>.Instance;
    }

    /// <summary>Forma comun a ambas configuraciones (tenant / global) para el envio.</summary>
    private readonly record struct ResolvedSmtp(
        string Host, int Port, string? User, string? PasswordEncrypted, bool UseSsl, string FromEmail, string? FromName);

    /// <summary>
    /// Modo TLS segun puerto y bandera "Usar SSL/TLS" de la configuracion:
    /// 465 = TLS implicito (SslOnConnect) siempre; 587 con SSL = STARTTLS obligatorio; otro puerto con SSL =
    /// Auto (MailKit decide); sin SSL = STARTTLS si el servidor lo ofrece (nunca credenciales en claro si hay TLS).
    /// </summary>
    public static SecureSocketOptions ResolveSocketOptions(int port, bool useSsl) => port switch
    {
        465 => SecureSocketOptions.SslOnConnect,
        _ when !useSsl => SecureSocketOptions.StartTlsWhenAvailable,
        587 => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.Auto
    };

    /// <summary>
    /// Mensaje legible con la cadena completa de excepciones ("Failure sending mail." solo no dice nada).
    /// Las excepciones de MailKit/sockets no incluyen la clave; aun asi se corta a un largo razonable.
    /// </summary>
    public static string DescribeError(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null && parts.Count < 5; e = e.InnerException)
        {
            var msg = (e.Message ?? string.Empty).Trim();
            if (msg.Length > 0 && !parts.Contains(msg)) { parts.Add(msg); }
        }
        var text = string.Join(" -> ", parts);
        return text.Length <= 500 ? text : text[..500];
    }

    public async Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        // 1) Config del tenant activo (el filtro global la acota; sin tenant en contexto no hay filas).
        //    2) Si no hay/esta deshabilitada, el servidor global de plataforma.
        ResolvedSmtp? resolved = null;

        var tenantCfg = await _db.TenantEmailConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (tenantCfg is { IsEnabled: true } && !string.IsNullOrWhiteSpace(tenantCfg.SmtpHost) && !string.IsNullOrWhiteSpace(tenantCfg.FromEmail))
        {
            resolved = new ResolvedSmtp(tenantCfg.SmtpHost!, tenantCfg.SmtpPort, tenantCfg.SmtpUser,
                tenantCfg.SmtpPasswordEncrypted, tenantCfg.UseSsl, tenantCfg.FromEmail!, tenantCfg.FromName);
        }
        else
        {
            var globalCfg = await _db.EmailConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            if (globalCfg is { IsEnabled: true } && !string.IsNullOrWhiteSpace(globalCfg.SmtpHost) && !string.IsNullOrWhiteSpace(globalCfg.FromEmail))
            {
                resolved = new ResolvedSmtp(globalCfg.SmtpHost!, globalCfg.SmtpPort, globalCfg.SmtpUser,
                    globalCfg.SmtpPasswordEncrypted, globalCfg.UseSsl, globalCfg.FromEmail!, globalCfg.FromName);
            }
        }

        if (resolved is not ResolvedSmtp cfg)
        {
            return new EmailSendResult(false, "El correo saliente no esta configurado/habilitado (ni el del tenant ni el global).");
        }

        string? password = null;
        if (!string.IsNullOrEmpty(cfg.PasswordEncrypted))
        {
            try { password = _secretProtector.Unprotect(cfg.PasswordEncrypted); }
            catch { return new EmailSendResult(false, "La clave SMTP esta cifrada con una version anterior. Vuelve a guardarla."); }
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(cfg.FromName ?? cfg.FromEmail, cfg.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient { Timeout = TimeoutMs };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(TimeoutMs * 2));

            await client.ConnectAsync(cfg.Host, cfg.Port, ResolveSocketOptions(cfg.Port, cfg.UseSsl), cts.Token);
            if (!string.IsNullOrWhiteSpace(cfg.User))
            {
                await client.AuthenticateAsync(cfg.User, password ?? string.Empty, cts.Token);
            }
            await client.SendAsync(message, cts.Token);
            await client.DisconnectAsync(true, cts.Token);
            return new EmailSendResult(true, null);
        }
        catch (Exception ex)
        {
            // Causa REAL (autenticacion, TLS, timeout, destinatario...). Nunca la clave: no viaja en el mensaje.
            var detail = DescribeError(ex);
            // SendGrid suele CERRAR la conexion al fallar el login (no lanza AuthenticationException): se mira
            // tambien el texto del error.
            var authFailed = ex is AuthenticationException || detail.Contains("Authentication", StringComparison.OrdinalIgnoreCase);
            var hint = authFailed && cfg.Host.Contains("sendgrid", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(cfg.User, "apikey", StringComparison.Ordinal)
                ? " (SendGrid exige usuario = apikey y la API key como clave)"
                : string.Empty;
            _logger.LogWarning("SMTP {Host}:{Port} fallo: {Detail}", cfg.Host, cfg.Port, detail);
            return new EmailSendResult(false, $"No se pudo enviar el correo: {detail}{hint}");
        }
    }
}
