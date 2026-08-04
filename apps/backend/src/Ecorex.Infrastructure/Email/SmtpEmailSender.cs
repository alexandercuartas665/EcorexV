using System.Net;
using System.Net.Mail;
using Ecorex.Application.Common;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Infrastructure.Email;

/// <summary>
/// Envio de correo via SMTP. Prefiere el servidor PROPIO del tenant activo
/// (<see cref="TenantEmailConfig"/>) cuando esta habilitado, para que cada empresa envie sus correos
/// de atencion desde su cuenta/dominio; si el tenant no tiene config (o no hay tenant en contexto,
/// p.ej. reseteo de clave), cae al servidor GLOBAL de plataforma (<see cref="EmailConfig"/>).
/// Compatible con Office365, Gmail, SendGrid (SMTP), etc. No persiste ni loggea la clave.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EcorexDbContext _db;
    private readonly ISecretProtector _secretProtector;

    public SmtpEmailSender(EcorexDbContext db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    /// <summary>Forma comun a ambas configuraciones (tenant / global) para el envio.</summary>
    private readonly record struct ResolvedSmtp(
        string Host, int Port, string? User, string? PasswordEncrypted, bool UseSsl, string FromEmail, string? FromName);

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
            using var client = new SmtpClient(cfg.Host, cfg.Port)
            {
                EnableSsl = cfg.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                // Sin esto el default de SmtpClient es 100s: un SMTP lento/inalcanzable colgaba la
                // operacion que dispara el correo (p.ej. crear una actividad) hasta 100s. Cap a 15s.
                Timeout = 15000
            };
            if (!string.IsNullOrWhiteSpace(cfg.User))
            {
                client.Credentials = new NetworkCredential(cfg.User, password ?? string.Empty);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(cfg.FromEmail, cfg.FromName ?? cfg.FromEmail),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(toEmail));

            await client.SendMailAsync(message, cancellationToken);
            return new EmailSendResult(true, null);
        }
        catch (Exception ex)
        {
            // No exponer la clave; solo el tipo/mensaje del error de envio.
            return new EmailSendResult(false, $"No se pudo enviar el correo: {ex.Message}");
        }
    }
}
