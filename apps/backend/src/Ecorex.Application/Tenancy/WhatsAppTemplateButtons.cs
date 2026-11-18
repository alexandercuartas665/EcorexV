using System.Text.Json;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Botones de plantillas HSM creadas desde el sistema (ADR-0186): respuesta rapida (QUICK_REPLY), visitar sitio
/// (URL fija https) y llamar (PHONE_NUMBER). Normaliza (tipo en mayusculas, textos recortados, AGRUPADOS: Meta
/// exige que las respuestas rapidas y los botones de accion no se intercalen), serializa con la misma forma que
/// el import de YCloud (camelCase: type/text/url/phoneNumber/hasUrlVariable) y compila el componente BUTTONS.
/// </summary>
public static class WhatsAppTemplateButtons
{
    public const string QuickReply = "QUICK_REPLY";
    public const string Url = "URL";
    public const string Phone = "PHONE_NUMBER";

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Tipo canonico desde lo que escriba el agente/usuario (quick_reply, respuesta, url, web, telefono...).</summary>
    public static string? CanonicalType(string? type) => (type ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '_') switch
    {
        "QUICK_REPLY" or "RESPUESTA" or "RESPUESTA_RAPIDA" or "QUICKREPLY" => QuickReply,
        "URL" or "WEB" or "SITIO" or "SITIO_WEB" or "VISITAR_SITIO" => Url,
        "PHONE_NUMBER" or "PHONE" or "TELEFONO" or "LLAMAR" or "CALL" => Phone,
        var other when other.Length > 0 => other,
        _ => null
    };

    /// <summary>Recorta, descarta vacios y agrupa: respuestas rapidas primero, luego URL, luego telefono.</summary>
    public static IReadOnlyList<WhatsAppTemplateButtonDto> Normalize(IEnumerable<WhatsAppTemplateButtonDto>? buttons)
    {
        if (buttons is null) { return Array.Empty<WhatsAppTemplateButtonDto>(); }
        var list = buttons
            .Select(b => new WhatsAppTemplateButtonDto(
                CanonicalType(b.Type) ?? string.Empty,
                string.IsNullOrWhiteSpace(b.Text) ? null : b.Text.Trim(),
                string.IsNullOrWhiteSpace(b.Url) ? null : b.Url.Trim(),
                string.IsNullOrWhiteSpace(b.PhoneNumber) ? null : b.PhoneNumber.Trim(),
                b.HasUrlVariable))
            .Where(b => b.Type.Length > 0 && b.Text is not null)
            .ToList();
        return list.OrderBy(b => Rank(b.Type)).ToList(); // OrderBy es estable: conserva el orden dentro de cada grupo
    }

    private static int Rank(string type) => type switch { QuickReply => 0, Url => 1, Phone => 2, _ => 3 };

    /// <summary>JSON para <c>WhatsAppTemplate.ButtonsJson</c> (null si no hay botones).</summary>
    public static string? ToJson(IReadOnlyList<WhatsAppTemplateButtonDto> buttons)
        => buttons.Count == 0
            ? null
            : JsonSerializer.Serialize(buttons.Select(b => new
            {
                type = b.Type,
                text = b.Text,
                url = b.Url,
                phoneNumber = b.PhoneNumber,
                hasUrlVariable = b.HasUrlVariable
            }));

    public static IReadOnlyList<WhatsAppTemplateButtonDto> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<WhatsAppTemplateButtonDto>(); }
        try { return JsonSerializer.Deserialize<List<WhatsAppTemplateButtonDto>>(json, ReadOpts) ?? new(); }
        catch (JsonException) { return Array.Empty<WhatsAppTemplateButtonDto>(); }
    }

    /// <summary>
    /// Reglas de Meta para botones creados desde el sistema: max 10 en total, max 2 de sitio web, max 1 de llamar,
    /// texto de 1 a 25 caracteres sin variables ni repetido, URL https sin espacios, telefono en formato
    /// internacional (+57...). Devuelve los problemas (vacio = valido).
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<WhatsAppTemplateButtonDto>? buttons)
    {
        var problems = new List<string>();
        var list = Normalize(buttons);
        if (list.Count == 0) { return problems; }
        if (list.Count > 10) { problems.Add($"Hay {list.Count} botones (Meta permite maximo 10)."); }
        if (list.Count(b => b.Type == Url) > 2) { problems.Add("Maximo 2 botones de sitio web."); }
        if (list.Count(b => b.Type == Phone) > 1) { problems.Add("Maximo 1 boton de llamar."); }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in list)
        {
            var label = $"Boton '{b.Text}'";
            if (b.Type is not (QuickReply or Url or Phone))
            {
                problems.Add($"{label}: tipo '{b.Type}' no soportado (usa respuesta rapida, sitio web o llamar).");
                continue;
            }
            if (b.Text!.Length > 25) { problems.Add($"{label}: el texto tiene {b.Text.Length} caracteres (max 25)."); }
            if (b.Text.Contains("{{", StringComparison.Ordinal)) { problems.Add($"{label}: el texto no admite variables."); }
            if (!seen.Add(b.Text)) { problems.Add($"{label}: texto repetido (cada boton debe ser distinto)."); }
            if (b.Type == Url && !b.HasUrlVariable)
            {
                var url = b.Url ?? string.Empty;
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.Contains(' ')
                    || !Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    problems.Add($"{label}: la URL debe ser https:// completa y sin espacios.");
                }
                else if (url.Contains("{{", StringComparison.Ordinal))
                {
                    problems.Add($"{label}: la URL no admite variables en este editor.");
                }
            }
            if (b.Type == Phone)
            {
                var digits = new string((b.PhoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
                if (!(b.PhoneNumber ?? string.Empty).TrimStart().StartsWith('+') || digits.Length < 8 || digits.Length > 15)
                {
                    problems.Add($"{label}: el telefono debe ir en formato internacional (ej. +573001234567).");
                }
            }
        }
        return problems;
    }

    /// <summary>Componente BUTTONS de Meta (null si no hay botones creables). Los botones URL dinamicos importados
    /// llevan su ejemplo; los creados desde el sistema son URL fija.</summary>
    public static object? MetaComponent(IReadOnlyList<WhatsAppTemplateButtonDto> buttons)
    {
        var items = new List<object>();
        foreach (var b in Normalize(buttons))
        {
            switch (b.Type)
            {
                case QuickReply:
                    items.Add(new { type = QuickReply, text = b.Text });
                    break;
                case Url when !string.IsNullOrWhiteSpace(b.Url):
                    items.Add(b.HasUrlVariable
                        ? new { type = Url, text = b.Text, url = b.Url, example = new[] { b.Url!.Replace("{{1}}", "ejemplo") } }
                        : (object)new { type = Url, text = b.Text, url = b.Url });
                    break;
                case Phone when !string.IsNullOrWhiteSpace(b.PhoneNumber):
                    items.Add(new { type = Phone, text = b.Text, phone_number = b.PhoneNumber });
                    break;
            }
        }
        return items.Count == 0 ? null : new { type = "BUTTONS", buttons = items };
    }
}
