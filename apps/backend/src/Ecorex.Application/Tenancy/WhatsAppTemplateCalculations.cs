using System.Text.RegularExpressions;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Logica pura del modulo de plantillas HSM (sin BD): normalizacion del nombre tecnico,
/// extraccion de variables {{token}} del cuerpo, validacion de la solicitud y reglas de
/// transicion de estado. Se prueba de forma unitaria (mismo enfoque que InventoryCalculations).
/// </summary>
public static partial class WhatsAppTemplateCalculations
{
    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    /// <summary>
    /// Normaliza el nombre tecnico al formato que exige Meta: minusculas, guion_bajo, sin acentos
    /// ni simbolos. Colapsa separadores repetidos. Devuelve "plantilla" si queda vacio.
    /// </summary>
    public static string NormalizeName(string? raw)
    {
        var lower = (raw ?? string.Empty).Trim().ToLowerInvariant();
        var chars = new List<char>(lower.Length);
        foreach (var c in lower)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') { chars.Add(c); }
            else if (c is ' ' or '-' or '_') { chars.Add('_'); }
        }
        var name = new string(chars.ToArray()).Trim('_');
        while (name.Contains("__")) { name = name.Replace("__", "_"); }
        return string.IsNullOrWhiteSpace(name) ? "plantilla" : name;
    }

    /// <summary>Extrae los tokens distintos {{token}} del cuerpo, en orden de aparicion y sin repetir.</summary>
    public static IReadOnlyList<string> ExtractTokens(string? body)
    {
        var order = new List<string>();
        if (string.IsNullOrEmpty(body)) { return order; }
        foreach (Match m in TokenRegex().Matches(body))
        {
            var token = m.Groups[1].Value;
            if (!order.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase)))
            {
                order.Add(token);
            }
        }
        return order;
    }

    /// <summary>
    /// Valida la solicitud de guardado. Devuelve el mensaje de error o null si es valida.
    /// </summary>
    public static string? ValidateSave(SaveWhatsAppTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) { return "El nombre es obligatorio."; }
        if (NormalizeName(request.Name).Length > 512) { return "El nombre es demasiado largo."; }
        if (string.IsNullOrWhiteSpace(request.Language)) { return "El idioma es obligatorio."; }
        if (request.Language.Trim().Length > 12) { return "El codigo de idioma no puede superar 12 caracteres."; }
        if (string.IsNullOrWhiteSpace(request.BodyText)) { return "El cuerpo es obligatorio."; }
        if (request.BodyText.Trim().Length > 1024) { return "El cuerpo no puede superar 1024 caracteres (limite de Meta)."; }
        if (request.HeaderText is { } h && h.Trim().Length > 60) { return "El encabezado no puede superar 60 caracteres."; }
        if (request.FooterText is { } f && f.Trim().Length > 60) { return "El pie no puede superar 60 caracteres."; }
        if (request.WhatsAppLineId == Guid.Empty) { return "Elige la linea por la que se someteria (define la WABA)."; }
        return null;
    }

    /// <summary>True si la plantilla se puede editar (solo en Draft o Rejected; el resto es inmutable).</summary>
    public static bool CanEdit(WhatsAppTemplateStatus status)
        => status is WhatsAppTemplateStatus.Draft or WhatsAppTemplateStatus.Rejected;

    /// <summary>True si la plantilla se puede someter (solo desde Draft o Rejected).</summary>
    public static bool CanSubmit(WhatsAppTemplateStatus status)
        => status is WhatsAppTemplateStatus.Draft or WhatsAppTemplateStatus.Rejected;
}
