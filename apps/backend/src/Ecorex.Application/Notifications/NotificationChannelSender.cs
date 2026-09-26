using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ecorex.Application.Notifications;

/// <inheritdoc />
public sealed class NotificationChannelSender : INotificationChannelSender
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _wa;
    private readonly IEmailSender _email;
    private readonly ITelegramClient _telegram;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<NotificationChannelSender> _logger;

    public NotificationChannelSender(IApplicationDbContext db, IWhatsAppConnectorService wa, IEmailSender email, ITelegramClient telegram, ISecretProtector secretProtector, ILogger<NotificationChannelSender> logger)
    {
        _db = db;
        _wa = wa;
        _email = email;
        _telegram = telegram;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) { return false; }
        try
        {
            var res = await _email.SendAsync(toEmail, subject, htmlBody, cancellationToken);
            return res.Ok;
        }
        catch { return false; }
    }

    public async Task<WhatsAppSendOutcome> SendWhatsAppTemplateAsync(Guid lineId, string phone, string templateName, string? language,
        IReadOnlyDictionary<string, string> tokens, Guid actorUserId,
        string? attachmentBase64 = null, string? attachmentMime = null, string? attachmentFileName = null,
        string? headerMediaTypeOverride = null, string? headerMediaUrlOverride = null,
        string? headerMediaFileNameOverride = null,
        IReadOnlyDictionary<string, string>? decisionLinksByButtonLabel = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(templateName))
        {
            return new WhatsAppSendOutcome(false, "Falta el numero o la plantilla.");
        }
        try
        {
            var q = _db.WhatsAppTemplates.AsNoTracking().Where(t => t.Name == templateName && t.IsActive);
            if (!string.IsNullOrWhiteSpace(language)) { q = q.Where(t => t.Language == language); }
            var tpl = await q.FirstOrDefaultAsync(cancellationToken);
            if (tpl is null)
            {
                // solo enviamos plantillas que existen y estan activas en el tenant
                var reason = $"No existe una plantilla activa '{templateName}'" + (string.IsNullOrWhiteSpace(language) ? "." : $" en idioma '{language}'.");
                _logger.LogWarning("WhatsApp plantilla no enviada (linea {LineId}, {Template}): {Reason}", lineId, templateName, reason);
                return new WhatsAppSendOutcome(false, reason);
            }
            var lang = string.IsNullOrWhiteSpace(language) ? tpl.Language : language!;
            var (mediaType, mediaUrl) = HeaderMedia(tpl);
            // Header de media DINAMICO (p.ej. el PDF de la cotizacion): sobreescribe el header fijo de la plantilla.
            if (!string.IsNullOrWhiteSpace(headerMediaUrlOverride))
            {
                mediaUrl = headerMediaUrlOverride;
                mediaType = string.IsNullOrWhiteSpace(headerMediaTypeOverride)
                    ? (string.IsNullOrWhiteSpace(mediaType) ? "document" : mediaType)
                    : headerMediaTypeOverride!.Trim().ToLowerInvariant();
            }
            // Nombre visible del documento del header: el que aporta la regla (ej. el nombre de la cotizacion). Si
            // no viene, el cliente YCloud lo deriva de la URL para que WhatsApp no lo muestre como "Sin titulo".
            // Botones URL DINAMICOS: si la plantilla trae botones URL con variable y la regla aporta enlaces de
            // decision por etiqueta, se arma un parametro de boton por cada uno (indice + sufijo del enlace).
            var urlButtons = WhatsAppButtonComposer.BuildUrlButtonParams(tpl.ButtonsJson, decisionLinksByButtonLabel);
            var res = await _wa.SendTemplateAsync(lineId, phone, tpl.Name, lang, BuildTemplateParams(tpl.VariablesJson, tokens), actorUserId, mediaType, mediaUrl, headerMediaFileNameOverride, attachmentBase64, attachmentMime, attachmentFileName, urlButtons, cancellationToken);
            if (!res.Ok)
            {
                // El motivo de Meta/YCloud (numero invalido, parametros, plantilla no aprobada, ventana, etc.)
                // que antes se descartaba. Sin telefono en claro para no filtrar datos personales al log.
                _logger.LogWarning("WhatsApp plantilla RECHAZADA (linea {LineId}, {Template} {Lang}): {Reason}", lineId, tpl.Name, lang, res.Error ?? "(sin detalle)");
            }
            return new WhatsAppSendOutcome(res.Ok, res.Error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp plantilla fallo por excepcion (linea {LineId}, {Template})", lineId, templateName);
            return new WhatsAppSendOutcome(false, ex.Message);
        }
    }

    public async Task<bool> SendWhatsAppGroupAsync(Guid lineId, string groupJid, string text, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupJid)) { return false; }
        try
        {
            // Evolution enruta al grupo cuando el jid "...@g.us" viaja en remoteJid (campo "number").
            var res = await _wa.SendTestAsync(lineId, groupJid, text, actorUserId, remoteJid: groupJid, cancellationToken);
            return res.Ok;
        }
        catch { return false; }
    }

    public async Task<bool> SendWhatsAppDocumentAsync(Guid lineId, string phone, string base64, string? mimeType, string? fileName,
        string? caption, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(base64)) { return false; }
        try
        {
            var res = await _wa.SendMediaAsync(lineId, phone, Domain.Enums.MessageMediaType.Document, base64,
                mimeType, fileName, caption, actorUserId, remoteJid: null, cancellationToken);
            return res.Ok;
        }
        catch { return false; }
    }

    public async Task<bool> SendTelegramAsync(string chatId, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chatId)) { return false; }
        try
        {
            var cfg = await _db.TenantTelegramConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            if (cfg is not { IsEnabled: true } || string.IsNullOrWhiteSpace(cfg.BotTokenEncrypted)) { return false; }
            string token;
            try { token = _secretProtector.Unprotect(cfg.BotTokenEncrypted!); }
            catch { return false; }
            var res = await _telegram.SendMessageAsync(token, chatId, text, cancellationToken);
            return res.Ok;
        }
        catch { return false; }
    }

    // Resuelve el header de media de la plantilla ("image"/"document"/"video" + URL) para el envio; (null,null) si es texto o sin header.
    private static (string? Type, string? Url) HeaderMedia(Domain.Entities.WhatsAppTemplate tpl)
    {
        if (string.IsNullOrWhiteSpace(tpl.HeaderMediaUrl)) { return (null, null); }
        return tpl.HeaderType switch
        {
            Domain.Enums.WhatsAppTemplateHeaderType.Image => ("image", tpl.HeaderMediaUrl),
            Domain.Enums.WhatsAppTemplateHeaderType.Document => ("document", tpl.HeaderMediaUrl),
            Domain.Enums.WhatsAppTemplateHeaderType.Video => ("video", tpl.HeaderMediaUrl),
            _ => (null, null)
        };
    }

    // Mapea los parametros posicionales de la plantilla resolviendo cada variable {{token}} por su nombre.
    private static IReadOnlyList<string> BuildTemplateParams(string? variablesJson, IReadOnlyDictionary<string, string> tokenMap)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(variablesJson)) { return result; }
        try
        {
            using var doc = JsonDocument.Parse(variablesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return result; }
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                // VariablesJson se guarda en PascalCase ("Token"); se lee la propiedad sin importar la caja.
                string? token = null;
                if (el.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in el.EnumerateObject())
                    {
                        if (string.Equals(p.Name, "token", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                        {
                            token = p.Value.GetString();
                            break;
                        }
                    }
                }
                var key = StripAccents((token ?? "").Trim().ToLowerInvariant());
                result.Add(tokenMap.TryGetValue(key, out var val) ? val : "");
            }
        }
        catch { /* variables mal formadas: sin parametros */ }
        return result;
    }

    private static string StripAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) { sb.Append(ch); }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
