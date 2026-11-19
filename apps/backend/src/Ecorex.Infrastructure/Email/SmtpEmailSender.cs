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
    private readonly ITenantContext? _tenant;

    public SmtpEmailSender(EcorexDbContext db, ISecretProtector secretProtector, ILogger<SmtpEmailSender>? logger = null,
        ITenantContext? tenant = null)
    {
        _db = db;
        _secretProtector = secretProtector;
        _logger = logger ?? NullLogger<SmtpEmailSender>.Instance;
        _tenant = tenant;
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

    /// <summary>
    /// Remitente efectivo de un envio (ADR-0185). El nombre visible del remitente propio gana; la direccion propia
    /// solo se usa como From si es del MISMO dominio que la configurada (dominio autenticado en el servidor); si no,
    /// se usa la configurada y la propia pasa a Reply-To (las respuestas llegan al comercial sin romper SPF/DKIM).
    /// </summary>
    public static (string FromName, string FromEmail, string? ReplyTo) ResolveSender(
        string configFromEmail, string? configFromName, EmailSenderOverride? sender)
    {
        var fromEmail = configFromEmail.Trim();
        var fromName = string.IsNullOrWhiteSpace(configFromName) ? fromEmail : configFromName.Trim();
        string? replyTo = null;
        if (sender is null) { return (fromName, fromEmail, replyTo); }

        if (!string.IsNullOrWhiteSpace(sender.FromName)) { fromName = sender.FromName.Trim(); }
        var own = ValidEmail(sender.FromEmail);
        if (own is not null)
        {
            if (string.Equals(Domain(own), Domain(fromEmail), StringComparison.OrdinalIgnoreCase)) { fromEmail = own; }
            else { replyTo = own; }
        }
        var reply = ValidEmail(sender.ReplyTo);
        if (reply is not null) { replyTo = reply; }
        if (replyTo is not null && string.Equals(replyTo, fromEmail, StringComparison.OrdinalIgnoreCase)) { replyTo = null; }
        return (fromName, fromEmail, replyTo);
    }

    private static string Domain(string email) => email[(email.LastIndexOf('@') + 1)..];

    /// <summary>
    /// Cabecera X-SMTPAPI de SendGrid con unique_args (ADR-0188): el tenant del contexto (ecx_tenant) mas los
    /// argumentos del envio (p.ej. ecx_run). JSON compacto sin espacios (la cabecera no debe plegarse).
    /// Null si no hay nada que mandar. Solo se aceptan claves [a-z0-9_] y valores cortos.
    /// </summary>
    public static string? BuildSmtpApiHeader(IReadOnlyDictionary<string, string>? customArgs, Guid? tenantId)
    {
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (tenantId is Guid tid && tid != Guid.Empty) { args["ecx_tenant"] = tid.ToString(); }
        if (customArgs is not null)
        {
            foreach (var (k, v) in customArgs)
            {
                if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v) || v.Length > 100) { continue; }
                if (!k.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) { continue; }
                args[k] = v.Trim();
            }
        }
        if (args.Count == 0) { return null; }
        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["unique_args"] = args });
    }

    /// <summary>Id que el servidor dio al aceptar ("250 Ok: queued as X" en SendGrid), o null.</summary>
    public static string? QueuedMessageId(string? serverResponse)
    {
        if (string.IsNullOrWhiteSpace(serverResponse)) { return null; }
        var m = System.Text.RegularExpressions.Regex.Match(serverResponse, @"queued as\s+([A-Za-z0-9._\-]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) { return null; }
        var id = m.Groups[1].Value.TrimEnd('.');
        return id.Length is > 0 and <= 200 ? id : null;
    }

    private static string? ValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var v = value.Trim();
        return MailboxAddress.TryParse(v, out var mb) && mb.Address.Contains('@') && mb.Address.IndexOf('@') > 0
            && mb.Address.LastIndexOf('@') < mb.Address.Length - 1 ? mb.Address : null;
    }

    public Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => SendAsync(toEmail, subject, htmlBody, sender: null, cancellationToken);

    public async Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, EmailSenderOverride? sender, CancellationToken cancellationToken = default)
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
            var (fromName, fromEmail, replyTo) = ResolveSender(cfg.FromEmail, cfg.FromName, sender);
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            if (replyTo is not null) { message.ReplyTo.Add(MailboxAddress.Parse(replyTo)); }
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();
            // SendGrid: argumentos de correlacion que vuelven en el Event Webhook (ADR-0188). Solo ids, nunca datos
            // personales; otros servidores no reciben la cabecera.
            if (cfg.Host.Contains("sendgrid", StringComparison.OrdinalIgnoreCase)
                && BuildSmtpApiHeader(sender?.CustomArgs, _tenant?.TenantId) is { } smtpApi)
            {
                message.Headers.Add("X-SMTPAPI", smtpApi);
            }

            using var client = new SmtpClient { Timeout = TimeoutMs };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(TimeoutMs * 2));

            await client.ConnectAsync(cfg.Host, cfg.Port, ResolveSocketOptions(cfg.Port, cfg.UseSsl), cts.Token);
            if (!string.IsNullOrWhiteSpace(cfg.User))
            {
                await client.AuthenticateAsync(cfg.User, password ?? string.Empty, cts.Token);
            }
            var response = await client.SendAsync(message, cts.Token);
            await client.DisconnectAsync(true, cts.Token);
            return new EmailSendResult(true, null, QueuedMessageId(response));
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
