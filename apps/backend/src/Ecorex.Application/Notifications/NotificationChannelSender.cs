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
            // Resolucion TOLERANTE del idioma (las plantillas WhatsApp suelen quedar en 'es' y la regla en
            // 'es_CO' -o al reves-, lo que fallaba callado): 1) match exacto; 2) idioma BASE (es_CO -> es);
            // 3) cualquier variante activa de esa plantilla. Se ENVIA con el idioma REAL de la hallada.
            var baseQ = _db.WhatsAppTemplates.AsNoTracking().Where(t => t.Name == templateName && t.IsActive);
            var tpl = await baseQ
                .Where(t => string.IsNullOrWhiteSpace(language) || t.Language == language)
                .FirstOrDefaultAsync(cancellationToken);
            if (tpl is null && !string.IsNullOrWhiteSpace(language))
            {
                var baseLang = language.Split('_', '-')[0];
                tpl = await baseQ.Where(t => t.Language == baseLang).FirstOrDefaultAsync(cancellationToken)
                      ?? await baseQ.OrderBy(t => t.Language).FirstOrDefaultAsync(cancellationToken);
                if (tpl is not null)
                {
                    _logger.LogInformation("WhatsApp plantilla '{Template}' (linea {LineId}): idioma '{Req}' no existe, se usa '{Found}'.",
                        templateName, lineId, language, tpl.Language);
                }
            }
            if (tpl is null)
            {
                // solo enviamos plantillas que existen y estan activas en el tenant
                var reason = $"No existe una plantilla activa '{templateName}'" + (string.IsNullOrWhiteSpace(language) ? "." : $" en ningun idioma (se pidio '{language}').");
                _logger.LogWarning("WhatsApp plantilla no enviada (linea {LineId}, {Template}): {Reason}", lineId, templateName, reason);
                return new WhatsAppSendOutcome(false, reason);
            }
            // Siempre se envia con el idioma REAL de la plantilla hallada (si no, Meta la rechaza por idioma).
            var lang = tpl.Language;
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
            // GUARD anti-fallo silencioso de Meta: WhatsApp DESCARTA la plantilla si algun parametro del cuerpo va
            // VACIO (caso tipico: la tarea SIN nombre de contacto -> {{1}} vacio -> YCloud responde "accepted" y luego
            // Meta la marca "failed" SIN codigo ni detalle). En vez de mandar un mensaje condenado a no llegar, NO se
            // envia y se devuelve un motivo claro que NOMBRA el dato faltante, para que el emisor lo deje en la
            // bitacora ("Fallos de envio") y el usuario sepa exactamente que completar antes de reintentar.
            var emptyParams = FindEmptyTemplateParams(tpl.VariablesJson, tokens);
            if (emptyParams.Count > 0)
            {
                var faltan = string.Join(", ", emptyParams);
                var reason = $"La plantilla '{tpl.Name}' exige el/los dato(s) [{faltan}] y no se tiene valor (quedarian vacios y WhatsApp descarta el mensaje). Completa [{faltan}] en la tarea/contacto y reintenta.";
                _logger.LogWarning("WhatsApp plantilla NO enviada (linea {LineId}, {Template} {Lang}): parametro(s) de plantilla vacio(s) [{Faltan}].", lineId, tpl.Name, lang, faltan);
                return new WhatsAppSendOutcome(false, reason);
            }
            var res = await _wa.SendTemplateAsync(lineId, phone, tpl.Name, lang, BuildTemplateParams(tpl.VariablesJson, tokens), actorUserId, mediaType, mediaUrl, headerMediaFileNameOverride, attachmentBase64, attachmentMime, attachmentFileName, urlButtons, cancellationToken);
            if (!res.Ok)
            {
                // El motivo de Meta/YCloud (numero invalido, parametros, plantilla no aprobada, ventana, etc.)
                // que antes se descartaba. Sin telefono en claro para no filtrar datos personales al log.
                _logger.LogWarning("WhatsApp plantilla RECHAZADA (linea {LineId}, {Template} {Lang}): {Reason}", lineId, tpl.Name, lang, res.Error ?? "(sin detalle)");
            }
            // Se propaga el id del mensaje del proveedor (YCloud wamid): el emisor del flujo lo estampa en la nota
            // saliente para que el webhook de estado correlacione la ENTREGA (sent/delivered/read/failed) y se vea
            // en la bitacora, no solo los fallos.
            return new WhatsAppSendOutcome(res.Ok, res.Error, res.MessageId);
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
            // Se registra el motivo REAL del proveedor (antes se tragaba el error y solo quedaba un bool false, que
            // dejaba al agente adivinando "error de canal"). Asi un fallo de envio de documento queda rastreable.
            if (!res.Ok)
            {
                _logger.LogWarning(
                    "No se pudo enviar el documento por WhatsApp (linea {LineId}, archivo {FileName}): {Error}",
                    lineId, fileName, res.Error ?? "(sin detalle del proveedor)");
            }
            return res.Ok;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Excepcion enviando documento por WhatsApp (linea {LineId}, archivo {FileName}).", lineId, fileName);
            return false;
        }
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

    // Variables de plantilla que quedarian VACIAS con el mapa de tokens actual (Meta rechaza un parametro vacio).
    // Devuelve el NOMBRE legible de cada una (etiqueta si existe, si no el token) para poder ALERTAR cual falta.
    private static IReadOnlyList<string> FindEmptyTemplateParams(string? variablesJson, IReadOnlyDictionary<string, string> tokenMap)
    {
        var empty = new List<string>();
        if (string.IsNullOrWhiteSpace(variablesJson)) { return empty; }
        try
        {
            using var doc = JsonDocument.Parse(variablesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) { return empty; }
            var index = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                index++;
                if (el.ValueKind != JsonValueKind.Object) { continue; }
                string? token = null;
                string? label = null;
                foreach (var p in el.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) { continue; }
                    if (string.Equals(p.Name, "token", StringComparison.OrdinalIgnoreCase)) { token = p.Value.GetString(); }
                    else if (string.Equals(p.Name, "label", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(p.Name, "name", StringComparison.OrdinalIgnoreCase)) { label = p.Value.GetString(); }
                }
                var key = StripAccents((token ?? "").Trim().ToLowerInvariant());
                var val = tokenMap.TryGetValue(key, out var v) ? v : "";
                if (string.IsNullOrWhiteSpace(val))
                {
                    var display = !string.IsNullOrWhiteSpace(label) ? label!.Trim()
                        : !string.IsNullOrWhiteSpace(token) ? token!.Trim()
                        : $"variable {index}";
                    empty.Add(display);
                }
            }
        }
        catch { /* variables mal formadas: no bloqueamos por esto */ }
        return empty;
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
