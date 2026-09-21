using System.Globalization;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Notifications;

/// <inheritdoc />
public sealed class NotificationChannelSender : INotificationChannelSender
{
    private readonly IApplicationDbContext _db;
    private readonly IWhatsAppConnectorService _wa;
    private readonly IEmailSender _email;
    private readonly ITelegramClient _telegram;
    private readonly ISecretProtector _secretProtector;

    public NotificationChannelSender(IApplicationDbContext db, IWhatsAppConnectorService wa, IEmailSender email, ITelegramClient telegram, ISecretProtector secretProtector)
    {
        _db = db;
        _wa = wa;
        _email = email;
        _telegram = telegram;
        _secretProtector = secretProtector;
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

    public async Task<bool> SendWhatsAppTemplateAsync(Guid lineId, string phone, string templateName, string? language,
        IReadOnlyDictionary<string, string> tokens, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(templateName)) { return false; }
        try
        {
            var q = _db.WhatsAppTemplates.AsNoTracking().Where(t => t.Name == templateName && t.IsActive);
            if (!string.IsNullOrWhiteSpace(language)) { q = q.Where(t => t.Language == language); }
            var tpl = await q.FirstOrDefaultAsync(cancellationToken);
            if (tpl is null) { return false; } // solo enviamos plantillas que existen
            var lang = string.IsNullOrWhiteSpace(language) ? tpl.Language : language!;
            var (mediaType, mediaUrl) = HeaderMedia(tpl);
            var res = await _wa.SendTemplateAsync(lineId, phone, tpl.Name, lang, BuildTemplateParams(tpl.VariablesJson, tokens), actorUserId, mediaType, mediaUrl, cancellationToken);
            return res.Ok;
        }
        catch { return false; }
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
