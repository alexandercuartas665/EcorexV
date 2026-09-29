using System.Text.Json;
using Ecorex.Application.Tenancy;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Mensaje entrante normalizado a partir del webhook crudo de Evolution.</summary>
public sealed record ParsedInbound(Guid TenantId, IngestMessageRequest Payload);

/// <summary>
/// Traduce el payload crudo del webhook de Evolution (evento messages.upsert) a nuestro
/// formato de ingesta. El tenant se deduce del nombre de instancia ecorex_{tenant}_{linea}.
/// Devuelve null si el evento no es un mensaje entrante de texto procesable.
/// </summary>
public static class EvolutionWebhookParser
{
    public static ParsedInbound? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) { return null; }

        // Evento: solo messages.upsert (entrante).
        if (root.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String)
        {
            var name = ev.GetString()!.Replace("_", ".").ToLowerInvariant();
            if (name != "messages.upsert") { return null; }
        }

        if (!root.TryGetProperty("instance", out var instEl) || instEl.ValueKind != JsonValueKind.String) { return null; }
        var instance = instEl.GetString()!;
        var tenantId = TenantFromInstance(instance);
        if (tenantId is null) { return null; }
        var lineId = LineFromInstance(instance);

        if (!root.TryGetProperty("data", out var data)) { return null; }
        if (data.ValueKind == JsonValueKind.Array)
        {
            data = data.EnumerateArray().FirstOrDefault();
        }
        if (data.ValueKind != JsonValueKind.Object) { return null; }

        // Reacciones (emoji sobre un mensaje): WhatsApp las envia como messages.upsert con
        // reactionMessage. NO son un mensaje de texto del cliente: no deben persistirse como mensaje
        // (saldria "(mensaje no soportado)") ni disparar al agente. Las ignoramos por completo.
        if (data.TryGetProperty("message", out var reactProbe) && reactProbe.ValueKind == JsonValueKind.Object
            && reactProbe.TryGetProperty("reactionMessage", out _))
        {
            return null;
        }

        if (!data.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.Object) { return null; }

        // Ignorar los mensajes salientes (eco de lo que enviamos nosotros).
        if (key.TryGetProperty("fromMe", out var fromMe) && fromMe.ValueKind == JsonValueKind.True) { return null; }

        if (!key.TryGetProperty("remoteJid", out var jidEl) || jidEl.ValueKind != JsonValueKind.String) { return null; }
        var jid = jidEl.GetString()!;
        if (jid.Contains("@g.us")) { return null; } // grupos no soportados
        var phone = new string(jid.TakeWhile(c => c != '@').Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(phone)) { return null; }

        var externalId = key.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()!
            : Guid.NewGuid().ToString("N");

        var name2 = data.TryGetProperty("pushName", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null;
        var isImage = IsImageMessage(data);
        var isAudio = IsAudioMessage(data);
        var isDocument = TryGetDocumentMessage(data, out var documentMessage);
        // Nombre ORIGINAL del archivo (documentMessage.fileName o .title): se usa como MediaFileName para que el
        // adjunto de la tarea sea legible y el TurnText muestre "[archivo adjunto: Factura...pdf]".
        var docFileName = isDocument ? ExtractDocFileName(documentMessage) : null;
        var body = ExtractText(data);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = isImage ? "(imagen)"
                : isAudio ? "(nota de voz)"
                : isDocument ? (docFileName ?? "(documento)")
                : "(mensaje no soportado)";
        }

        DateTimeOffset? sentAt = null;
        if (data.TryGetProperty("messageTimestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var secs))
        {
            sentAt = DateTimeOffset.FromUnixTimeSeconds(secs);
        }

        // Para imagen/audio/documento marcamos el MessageType: el webhook descargara la media por el id del
        // mensaje (externalId = key.id) y la ingerira como adjunto, para que el agente pueda analizarla
        // (el documento lo LEE Gemini nativo; el audio se transcribe).
        var messageType = isImage ? "image" : isAudio ? "audio" : isDocument ? "document" : "text";
        return new ParsedInbound(tenantId.Value,
            new IngestMessageRequest(phone, name2, externalId, body!, messageType, sentAt, lineId,
                RemoteJid: jid, MediaFileName: isDocument ? docFileName : null));
    }

    private static bool IsImageMessage(JsonElement data) =>
        data.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object
        && msg.TryGetProperty("imageMessage", out var im) && im.ValueKind == JsonValueKind.Object;

    // WhatsApp manda TANTO las notas de voz (ptt=true) COMO los audios normales dentro de audioMessage;
    // con detectar audioMessage basta para ambos.
    private static bool IsAudioMessage(JsonElement data) =>
        data.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object
        && msg.TryGetProperty("audioMessage", out var am) && am.ValueKind == JsonValueKind.Object;

    // Documento (PDF/Excel/CSV...). WhatsApp lo envia como documentMessage directo, o envuelto en
    // documentWithCaptionMessage (cuando trae texto): se resuelve el documentMessage en ambos casos.
    private static bool TryGetDocumentMessage(JsonElement data, out JsonElement documentMessage)
    {
        documentMessage = default;
        if (!data.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) { return false; }
        if (msg.TryGetProperty("documentMessage", out var dm) && dm.ValueKind == JsonValueKind.Object)
        {
            documentMessage = dm;
            return true;
        }
        // Envoltura con caption: message.documentWithCaptionMessage.message.documentMessage
        if (msg.TryGetProperty("documentWithCaptionMessage", out var wrap) && wrap.ValueKind == JsonValueKind.Object
            && wrap.TryGetProperty("message", out var inner) && inner.ValueKind == JsonValueKind.Object
            && inner.TryGetProperty("documentMessage", out var dm2) && dm2.ValueKind == JsonValueKind.Object)
        {
            documentMessage = dm2;
            return true;
        }
        return false;
    }

    // Nombre original del archivo: fileName y, si falta, title.
    private static string? ExtractDocFileName(JsonElement documentMessage)
    {
        if (documentMessage.TryGetProperty("fileName", out var fn) && fn.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(fn.GetString())) { return fn.GetString(); }
        if (documentMessage.TryGetProperty("title", out var ti) && ti.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(ti.GetString())) { return ti.GetString(); }
        return null;
    }

    private static string? ExtractText(JsonElement data)
    {
        if (!data.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) { return null; }
        if (msg.TryGetProperty("conversation", out var c) && c.ValueKind == JsonValueKind.String) { return c.GetString(); }
        if (msg.TryGetProperty("extendedTextMessage", out var ext) && ext.ValueKind == JsonValueKind.Object
            && ext.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) { return t.GetString(); }
        if (msg.TryGetProperty("imageMessage", out var im) && im.ValueKind == JsonValueKind.Object)
        {
            return im.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String ? cap.GetString() : "(imagen)";
        }
        // Documento con texto del usuario: se devuelve el caption; sin caption -> null (el fallback usa el fileName).
        if (TryGetDocumentMessage(data, out var docMsg)
            && docMsg.TryGetProperty("caption", out var dcap) && dcap.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(dcap.GetString()))
        {
            return dcap.GetString();
        }
        return null;
    }

    // Instancia ecorex_{tenant:N}_{linea:N} -> tenant Guid.
    private static Guid? TenantFromInstance(string instance)
    {
        const string prefix = "ecorex_";
        if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return null; }
        var rest = instance[prefix.Length..];
        var sep = rest.IndexOf('_');
        var tenantPart = sep > 0 ? rest[..sep] : rest;
        return Guid.TryParseExact(tenantPart, "N", out var id) ? id : null;
    }

    // Instancia ecorex_{tenant:N}_{linea:N} -> linea Guid (WhatsAppLine.Id). Null si no viene.
    private static Guid? LineFromInstance(string instance)
    {
        const string prefix = "ecorex_";
        if (!instance.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return null; }
        var rest = instance[prefix.Length..];
        var sep = rest.IndexOf('_');
        if (sep <= 0 || sep + 1 >= rest.Length) { return null; }
        var linePart = rest[(sep + 1)..];
        return Guid.TryParseExact(linePart, "N", out var id) ? id : null;
    }
}
