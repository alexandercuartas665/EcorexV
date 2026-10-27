using System.Globalization;
using System.Text.Json;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Mensaje entrante crudo de un webhook de YCloud (api.ycloud.com v2), antes de resolver tenant/linea.
/// <see cref="To"/> es el numero de negocio que RECIBIO el mensaje (sin '+'); el endpoint lo mapea a la linea
/// por <c>WhatsAppLine.YCloudPhoneNumberId</c> y de ahi al tenant.</summary>
/// <param name="MediaLink">URL publica de la media entrante (imagen/documento/audio/video) segun la entrega YCloud,
/// o null si el mensaje no trae media. El endpoint la descarga y la persiste como adjunto del mensaje.</param>
/// <param name="MediaMime">MIME de la media (ej. "image/jpeg", "application/pdf"), o null.</param>
/// <param name="MediaKind">Tipo de media: "image" | "document" | "audio" | "video"; null si es texto u otro.</param>
public sealed record YCloudParsedMessage(string To, string Phone, string? Name, string ExternalId, string Body, DateTimeOffset? SentAt,
    string? MediaLink = null, string? MediaMime = null, string? MediaKind = null, string? MediaFileName = null);

/// <summary>Estado de entrega de un mensaje SALIENTE reportado por YCloud (evento whatsapp.message.updated).
/// <see cref="BusinessNumber"/> es el numero de negocio que ENVIO (from), <see cref="RecipientPhone"/> el
/// destinatario (to). Sirve para hacer VISIBLE un "no llego" (failed/undelivered) con el motivo de Meta, que
/// antes se perdia porque el webhook de estado se ignoraba.</summary>
public sealed record YCloudStatusUpdate(
    string BusinessNumber, string RecipientPhone, string Status, string? WabaId,
    string? ErrorCode, string? ErrorMessage, string? Wamid, DateTimeOffset? At,
    // Id INTERNO de YCloud del mensaje (campo "id" del evento). Al ENVIAR, YCloud suele devolver solo su id (el
    // wamid de Meta aun no existe) y es lo que se guarda en Message.ExternalId; por eso la correlacion del estado
    // debe poder cruzar por ESTE id ademas del wamid de Meta.
    string? ProviderId = null,
    // Subarbol de error CRUDO (JSON) del evento fallido, tal cual vino de Meta/YCloud. Se LOGUEA en el fallo para
    // poder ajustar el parser con un payload real cuando la forma del error no sea una de las ya reconocidas.
    string? ErrorRaw = null)
{
    /// <summary>true si la entrega fallo (no llego al cliente): failed / undelivered.</summary>
    public bool IsFailure => !string.IsNullOrWhiteSpace(Status)
        && (Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || Status.Equals("undelivered", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Traduce el payload del webhook de YCloud a mensajes entrantes normalizados. YCloud entrega UN evento por
/// POST (objeto con <c>type</c> y <c>whatsappInboundMessage</c>); por robustez tambien se acepta un array de
/// eventos. Solo procesa eventos de mensaje entrante (type que contiene "inbound_message"); ignora estados de
/// entrega, plantillas, etc. Es tolerante a nombres alternos de campos (customerProfile/contact/profile,
/// timestamp/sendTime) para no romperse ante variaciones menores del proveedor.
/// </summary>
public static class YCloudWebhookParser
{
    public static IReadOnlyList<YCloudParsedMessage> Parse(JsonElement root)
    {
        var result = new List<YCloudParsedMessage>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in root.EnumerateArray()) { ParseEvent(e, result); }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            ParseEvent(root, result);
        }
        return result;
    }

    /// <summary>Extrae los ESTADOS de entrega de mensajes salientes (eventos de estado de YCloud). Tolerante a
    /// un objeto o un array de eventos. Ignora los eventos de mensaje entrante (los maneja <see cref="Parse"/>).</summary>
    public static IReadOnlyList<YCloudStatusUpdate> ParseStatuses(JsonElement root)
    {
        var result = new List<YCloudStatusUpdate>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in root.EnumerateArray()) { ParseStatus(e, result); }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            ParseStatus(root, result);
        }
        return result;
    }

    private static void ParseStatus(JsonElement evt, List<YCloudStatusUpdate> result)
    {
        if (evt.ValueKind != JsonValueKind.Object) { return; }

        // Si trae 'type' y es de mensaje ENTRANTE, no es un estado -> se ignora aqui (lo maneja Parse()).
        if (evt.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var t = typeEl.GetString() ?? "";
            if (t.Contains("inbound_message", StringComparison.OrdinalIgnoreCase)) { return; }
        }

        // El estado viene en whatsappMessage (evento whatsapp.message.updated); fallback al propio objeto.
        var msg = evt.TryGetProperty("whatsappMessage", out var wm) && wm.ValueKind == JsonValueKind.Object
            ? wm
            : evt;

        var status = Str(msg, "status");
        if (string.IsNullOrWhiteSpace(status)) { return; } // sin 'status' no es un evento de estado

        // Saliente: from = negocio, to = destinatario. (Entrante seria al reves, pero esos ya se filtraron.)
        var business = Digits(Str(msg, "from"));
        var recipient = Digits(Str(msg, "to"));
        var wabaId = Str(msg, "wabaId") ?? Str(msg, "wabaID") ?? Str(msg, "waba_id");
        // wamid de Meta y el id interno de YCloud por SEPARADO: la correlacion cruza por cualquiera de los dos.
        var wamid = Str(msg, "wamid");
        var providerId = Str(msg, "id");

        // error: objeto { code, message/title/detail } o arreglo 'errors'. El detalle de Meta suele venir MAS
        // anidado (error.errorData.details / error_data.details; errors[0].error_data.details; href). code puede
        // venir numerico o string. Ademas se guarda el subarbol CRUDO para diagnosticar formas no reconocidas.
        string? errCode = null, errMsg = null, errRaw = null;
        if (msg.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
        {
            errRaw = err.GetRawText();
            (errCode, errMsg) = ReadError(err);
        }
        else if (msg.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
        {
            errRaw = errs.GetRawText();
            (errCode, errMsg) = ReadError(errs[0]);
        }

        var at = ParseTime(Str(msg, "updateTime") ?? Str(msg, "sendTime") ?? Str(msg, "timestamp"));
        result.Add(new YCloudStatusUpdate(business, recipient, status!, wabaId, errCode, errMsg, wamid, at, providerId, errRaw));
    }

    // Lee (codigo, mensaje) de un nodo de error tolerando las variantes de Meta/YCloud: el mensaje puede venir
    // directo (message/title/detail) o anidado (error_data.details / errorData.details), y como ultimo recurso el
    // href. El codigo numerico puede estar en el nodo o anidado en error_data/errorData. Un nodo string se toma
    // como el mensaje. Asi, aunque la forma cambie, se captura lo mejor disponible (y el crudo queda en ErrorRaw).
    private static (string? code, string? msg) ReadError(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) { return (null, e.GetString()); }
        if (e.ValueKind != JsonValueKind.Object) { return (null, null); }
        var code = NumOrStr(e, "code")
            ?? NumOrStrPath(e, "error_data", "code")
            ?? NumOrStrPath(e, "errorData", "code");
        var msg = Str(e, "message") ?? Str(e, "title") ?? Str(e, "detail")
            ?? Str(e, "error_data", "details") ?? Str(e, "errorData", "details")
            ?? Str(e, "error_data", "detail") ?? Str(e, "errorData", "detail")
            ?? Str(e, "href");
        return (code, msg);
    }

    // Lee un campo que puede venir como numero o string (ej. codigo de error de Meta).
    private static string? NumOrStr(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) { return null; }
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }

    // Como NumOrStr pero descendiendo por una ruta de objetos (ej. error_data.code).
    private static string? NumOrStrPath(JsonElement el, params string[] path)
    {
        var cur = el;
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(path[i], out var next)) { return null; }
            cur = next;
        }
        return NumOrStr(cur, path[^1]);
    }

    private static void ParseEvent(JsonElement evt, List<YCloudParsedMessage> result)
    {
        if (evt.ValueKind != JsonValueKind.Object) { return; }

        // Filtra por tipo si viene: solo mensajes entrantes (whatsapp.inbound_message.received). Si no hay
        // 'type', se intenta igual (tolerante). Un evento de estado/plantilla se ignora.
        if (evt.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var t = typeEl.GetString() ?? "";
            if (t.Length > 0 && !t.Contains("inbound_message", StringComparison.OrdinalIgnoreCase)) { return; }
        }

        // El mensaje viene en whatsappInboundMessage; si no esta, se usa el propio objeto (tolerancia).
        var msg = evt.TryGetProperty("whatsappInboundMessage", out var wim) && wim.ValueKind == JsonValueKind.Object
            ? wim
            : evt;

        var to = Digits(Str(msg, "to"));
        var from = Digits(Str(msg, "from"));
        if (to.Length == 0 || from.Length == 0) { return; }

        var name = Str(msg, "customerProfile", "name") ?? Str(msg, "contact", "name") ?? Str(msg, "profile", "name");
        // ExternalId = WAMID de WhatsApp ("wamid.HBgM...=="), NO el id interno de YCloud (24-hex). El id interno
        // rompe las reacciones: WhatsApp responde 131009 "Invalid message_id" (la reaccion referencia por wamid).
        // Fallback al id interno si el evento no trae wamid (sigue sirviendo para dedup por ExternalId).
        var externalId = Str(msg, "wamid") ?? Str(msg, "id");
        if (string.IsNullOrWhiteSpace(externalId)) { externalId = Guid.NewGuid().ToString("N"); }

        var sentAt = ParseTime(Str(msg, "sendTime") ?? Str(msg, "timestamp"));

        var body = ExtractText(msg);
        if (string.IsNullOrWhiteSpace(body)) { body = "(mensaje no soportado)"; }

        // Media entrante (imagen/documento/audio/video): YCloud entrega la media como URL publica + mime.
        // El link puede venir como 'link' o 'url', y el mime como 'mime_type' o 'mimeType'. Si no hay link,
        // MediaLink queda null y el endpoint ingiere el mensaje como texto (con el caption/"(<tipo>)").
        string? mediaLink = null, mediaMime = null, mediaKind = null, mediaFileName = null;
        var type = Str(msg, "type");
        if (type is "image" or "document" or "audio" or "video")
        {
            mediaKind = type;
            mediaLink = Str(msg, type!, "link") ?? Str(msg, type!, "url");
            mediaMime = Str(msg, type!, "mime_type") ?? Str(msg, type!, "mimeType");
            // Nombre ORIGINAL del archivo: los documentos de WhatsApp traen 'filename'. Imagenes/audio/video
            // normalmente no lo traen (queda null y se cae al nombre almacenado / fallback).
            mediaFileName = Str(msg, type!, "filename") ?? Str(msg, type!, "fileName");
        }

        result.Add(new YCloudParsedMessage(to, from, name, externalId!, body!, sentAt, mediaLink, mediaMime, mediaKind, mediaFileName));
    }

    private static string? ExtractText(JsonElement msg)
    {
        var type = Str(msg, "type");
        switch (type)
        {
            case "text":
                return Str(msg, "text", "body");
            case "image":
            case "video":
            case "audio":
            case "document":
                // Media entrante (fase 2: descargar por id/link). Por ahora se capta el caption si viene.
                return Str(msg, type!, "caption") ?? $"({type})";
            case "button":
                return Str(msg, "button", "text");
            case "interactive":
                return Str(msg, "interactive", "button_reply", "title")
                    ?? Str(msg, "interactive", "list_reply", "title");
            case "reaction":
                return Str(msg, "reaction", "emoji");
            default:
                // Ultimo recurso: si hay un text.body directo, usarlo.
                return Str(msg, "text", "body");
        }
    }

    // ---- helpers de lectura tolerante ----

    private static string? Str(JsonElement el, params string[] path)
    {
        var cur = el;
        foreach (var p in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out var next)) { return null; }
            cur = next;
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() : null;
    }

    private static string Digits(string? s) => string.IsNullOrEmpty(s) ? "" : new string(s.Where(char.IsDigit).ToArray());

    // YCloud usa ISO8601 (ej. "2026-09-04T14:00:00Z"); por si acaso, acepta tambien unix (segundos) numerico.
    private static DateTimeOffset? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)) { return dto; }
        if (long.TryParse(s, out var secs)) { return DateTimeOffset.FromUnixTimeSeconds(secs); }
        return null;
    }
}
