using System.Globalization;
using System.Text.Json;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Mensaje entrante crudo de un webhook de YCloud (api.ycloud.com v2), antes de resolver tenant/linea.
/// <see cref="To"/> es el numero de negocio que RECIBIO el mensaje (sin '+'); el endpoint lo mapea a la linea
/// por <c>WhatsAppLine.YCloudPhoneNumberId</c> y de ahi al tenant.</summary>
public sealed record YCloudParsedMessage(string To, string Phone, string? Name, string ExternalId, string Body, DateTimeOffset? SentAt);

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
        var externalId = Str(msg, "id");
        if (string.IsNullOrWhiteSpace(externalId)) { externalId = Guid.NewGuid().ToString("N"); }

        var sentAt = ParseTime(Str(msg, "sendTime") ?? Str(msg, "timestamp"));

        var body = ExtractText(msg);
        if (string.IsNullOrWhiteSpace(body)) { body = "(mensaje no soportado)"; }

        result.Add(new YCloudParsedMessage(to, from, name, externalId!, body!, sentAt));
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
