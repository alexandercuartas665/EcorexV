using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Scheduling;

/// <summary>Destinatario resuelto de una programacion (el encargado). Puede no tener correo o WhatsApp.</summary>
public sealed record ScheduledJobRecipient(Guid TenantUserId, string? Email, string? WhatsAppPhone);

/// <summary>Resultado de entregar por UN canal. <see cref="Delivered"/> false => la ejecucion es un fallo.</summary>
public sealed record ChannelSendResult(bool Delivered, string Detail)
{
    public static ChannelSendResult Ok(string detail) => new(true, detail);
    public static ChannelSendResult Fail(string detail) => new(false, detail);
}

/// <summary>
/// Entrega por un canal externo (ola P4). Es una ALLOW-LIST TIPADA resuelta por DI: cada canal soportado
/// tiene su implementacion registrada; un canal sin sender simplemente NO se entrega y queda registrado
/// como tal en la bitacora. NO hay reflexion ni invocacion dinamica (el legacy cayo en RCE por ahi).
/// </summary>
public interface IScheduledJobChannelSender
{
    /// <summary>Canal que atiende este sender.</summary>
    ScheduledJobChannelType Channel { get; }

    Task<ChannelSendResult> SendAsync(
        ScheduledJobRecipient recipient, string title, string body, CancellationToken cancellationToken = default);
}

/// <summary>Correo real (SMTP de la plataforma) al correo del encargado.</summary>
public sealed class EmailChannelSender : IScheduledJobChannelSender
{
    private readonly IEmailSender _email;

    public EmailChannelSender(IEmailSender email) => _email = email;

    public ScheduledJobChannelType Channel => ScheduledJobChannelType.Email;

    public async Task<ChannelSendResult> SendAsync(
        ScheduledJobRecipient recipient, string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipient.Email))
        {
            return ChannelSendResult.Fail("Correo: el encargado no tiene direccion de correo.");
        }

        var html = $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>";
        var result = await _email.SendAsync(recipient.Email, title, html, cancellationToken);
        return result.Ok
            ? ChannelSendResult.Ok($"Correo: enviado a {recipient.Email}.")
            : ChannelSendResult.Fail($"Correo: fallo el envio ({result.Error}).");
    }
}

/// <summary>
/// WhatsApp real por las LINEAS que ya tiene el tenant (Evolution/Cloud). Envia desde una linea CONECTADA
/// al numero del encargado. En este modelo el numero de un usuario es el <c>PhoneNumber</c> de la linea
/// que tiene asignada (WhatsAppLine.AssignedToTenantUserId): si no tiene linea asignada, no hay a donde
/// enviarle y se registra asi (en vez de fingir la entrega).
/// </summary>
public sealed class WhatsAppChannelSender : IScheduledJobChannelSender
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _whatsApp;
    private readonly ITenantContext _tenantContext;

    public WhatsAppChannelSender(
        IApplicationDbContext db, IWhatsAppConnectorService whatsApp, ITenantContext tenantContext)
    {
        _db = db;
        _whatsApp = whatsApp;
        _tenantContext = tenantContext;
    }

    public ScheduledJobChannelType Channel => ScheduledJobChannelType.WhatsApp;

    public async Task<ChannelSendResult> SendAsync(
        ScheduledJobRecipient recipient, string title, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipient.WhatsAppPhone))
        {
            return ChannelSendResult.Fail(
                "WhatsApp: el encargado no tiene numero (asignale una linea de WhatsApp).");
        }

        // Linea EMISORA: cualquiera conectada del tenant.
        var line = await _db.WhatsAppLines
            .Where(l => l.Status == WhatsAppLineStatus.Connected)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new { l.Id, l.PhoneNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (line is null)
        {
            return ChannelSendResult.Fail("WhatsApp: el tenant no tiene ninguna linea conectada.");
        }

        var text = $"*{title}*\n{body}";
        var actor = _tenantContext.UserId ?? Guid.Empty;
        var result = await _whatsApp.SendTestAsync(line.Id, recipient.WhatsAppPhone, text, actor, cancellationToken);
        return result.Ok
            ? ChannelSendResult.Ok($"WhatsApp: enviado a {recipient.WhatsAppPhone}.")
            : ChannelSendResult.Fail($"WhatsApp: fallo el envio ({result.Error}).");
    }
}
