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
        var body = ExtractText(data);
        if (string.IsNullOrWhiteSpace(body)) { body = isImage ? "(imagen)" : "(mensaje no soportado)"; }

        DateTimeOffset? sentAt = null;
        if (data.TryGetProperty("messageTimestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var secs))
        {
            sentAt = DateTimeOffset.FromUnixTimeSeconds(secs);
        }

        // Para imagenes marcamos MessageType="image": el webhook descargara la media por el id del mensaje
        // (externalId = key.id) y la ingerira como adjunto, para que el agente pueda analizarla.
        return new ParsedInbound(tenantId.Value,
            new IngestMessageRequest(phone, name2, externalId, body!, isImage ? "image" : "text", sentAt, lineId));
    }

    private static bool IsImageMessage(JsonElement data) =>
        data.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object
        && msg.TryGetProperty("imageMessage", out var im) && im.ValueKind == JsonValueKind.Object;

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
