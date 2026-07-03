using System.Text.Json;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Mensaje entrante crudo de un webhook de Meta Cloud, antes de resolver tenant/linea.</summary>
public sealed record MetaParsedMessage(string PhoneNumberId, string Phone, string? Name, string ExternalId, string Body, DateTimeOffset? SentAt);

/// <summary>
/// Traduce el payload del webhook de WhatsApp Cloud de Meta a mensajes entrantes normalizados. A diferencia
/// de Evolution, el numero se identifica por value.metadata.phone_number_id (el endpoint mapea ese id a la
/// linea y de ahi al tenant). Ignora los eventos de estado (statuses) y los mensajes propios.
/// </summary>
public static class MetaWebhookParser
{
    public static IReadOnlyList<MetaParsedMessage> Parse(JsonElement root)
    {
        var result = new List<MetaParsedMessage>();
        if (root.ValueKind != JsonValueKind.Object) { return result; }
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) { return result; }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) { continue; }
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) { continue; }

                // phone_number_id identifica el numero (linea).
                string? phoneNumberId = null;
                if (value.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty("phone_number_id", out var pnid) && pnid.ValueKind == JsonValueKind.String)
                {
                    phoneNumberId = pnid.GetString();
                }
                if (string.IsNullOrWhiteSpace(phoneNumberId)) { continue; }

                // Nombre del contacto (opcional).
                string? contactName = null;
                if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0)
                {
                    var c0 = contacts[0];
                    if (c0.TryGetProperty("profile", out var prof) && prof.ValueKind == JsonValueKind.Object
                        && prof.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                    {
                        contactName = nm.GetString();
                    }
                }

                if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array) { continue; } // statuses u otros: ignorar

                foreach (var m in messages.EnumerateArray())
                {
                    var from = m.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                    if (string.IsNullOrWhiteSpace(from)) { continue; }
                    var phone = new string(from!.Where(char.IsDigit).ToArray());
                    if (string.IsNullOrEmpty(phone)) { continue; }

                    var externalId = m.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? idEl.GetString()!
                        : Guid.NewGuid().ToString("N");

                    DateTimeOffset? sentAt = null;
                    if (m.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                        && long.TryParse(ts.GetString(), out var secs))
                    {
                        sentAt = DateTimeOffset.FromUnixTimeSeconds(secs);
                    }

                    var body = ExtractText(m);
                    if (string.IsNullOrWhiteSpace(body)) { body = "(mensaje no soportado)"; }

                    result.Add(new MetaParsedMessage(phoneNumberId!, phone, contactName, externalId, body!, sentAt));
                }
            }
        }
        return result;
    }

    private static string? ExtractText(JsonElement m)
    {
        var type = m.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        switch (type)
        {
            case "text":
                return m.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.Object
                    && txt.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            case "image":
            case "video":
            case "document":
            case "audio":
                // Media entrante (fase 2: descargar por media id). Por ahora se capta el caption si viene.
                if (m.TryGetProperty(type!, out var media) && media.ValueKind == JsonValueKind.Object
                    && media.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String)
                {
                    return cap.GetString();
                }
                return $"({type})";
            case "interactive":
                // Respuestas de botones/listas: tomar el titulo o id seleccionado.
                if (m.TryGetProperty("interactive", out var it) && it.ValueKind == JsonValueKind.Object)
                {
                    if (it.TryGetProperty("button_reply", out var br) && br.TryGetProperty("title", out var bt) && bt.ValueKind == JsonValueKind.String) { return bt.GetString(); }
                    if (it.TryGetProperty("list_reply", out var lr) && lr.TryGetProperty("title", out var lt) && lt.ValueKind == JsonValueKind.String) { return lt.GetString(); }
                }
                return null;
            default:
                return null;
        }
    }
}
