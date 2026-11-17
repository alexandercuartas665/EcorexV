using System.Text.RegularExpressions;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>Borrador de plantilla de WhatsApp a verificar (mismos campos que guarda WhatsAppTemplateService).</summary>
public sealed record WhatsAppDraft(
    string? Nombre, string? Idioma, WhatsAppTemplateCategory Categoria,
    WhatsAppTemplateHeaderType? TipoEncabezado, string? Encabezado, string? EncabezadoMediaUrl,
    string? Cuerpo, string? Pie, IReadOnlyList<WhatsAppTemplateVariable> Variables);

/// <summary>
/// Verificador DETERMINISTA de plantillas HSM de WhatsApp (ADR-0174, fase 2): anticipa los rechazos tipicos de
/// Meta antes de someter (que es irreversible). Errores: nombre no snake_case/largo, idioma invalido, cuerpo vacio o
/// &gt; 1024, encabezado/pie &gt; 60 o con variables, variable sin ejemplo, cuerpo que empieza/termina en variable,
/// variables pegadas, demasiadas variables para el texto ("Params Words Ratio"), categoria Authentication (requiere
/// boton OTP) e imagen de encabezado sin URL https. Avisos: variable declarada sin usar, lenguaje promocional en
/// Utility (Meta la recategoriza a Marketing), saltos de linea excesivos.
/// </summary>
public static class WhatsAppTemplateVerifier
{
    private static readonly Regex Token = new(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex NameRx = new(@"^[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex LangRx = new(@"^[a-z]{2,3}(_[A-Z]{2})?$", RegexOptions.Compiled);
    private static readonly Regex Adjacent = new(@"\}\}\s*\{\{", RegexOptions.Compiled);
    private static readonly string[] PromoWords =
        { "descuento", "promocion", "oferta", "gratis", "rebaja", "black friday", "cupon", "2x1", "aprovecha", "compra ya", "ultimas unidades" };

    public static TemplateVerifyResult Verify(WhatsAppDraft d)
    {
        var issues = new List<TemplateIssue>();
        var body = d.Cuerpo ?? string.Empty;

        // --- Nombre / idioma / categoria ---
        var name = (d.Nombre ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new("error", "nombre", "Falta el nombre tecnico.", "Usa snake_case en minusculas, p.ej. presentacion_gerentes."));
        }
        else if (!NameRx.IsMatch(name))
        {
            issues.Add(new("error", "nombre", $"'{name}' no es valido para Meta (solo minusculas, numeros y guion bajo).",
                $"Usa '{WhatsAppTemplateCalculations.NormalizeName(name)}'."));
        }
        else if (name.Length > 512)
        {
            issues.Add(new("error", "nombre", "El nombre supera 512 caracteres.", "Acortalo."));
        }
        if (!LangRx.IsMatch((d.Idioma ?? string.Empty).Trim()))
        {
            issues.Add(new("error", "idioma", $"Codigo de idioma '{d.Idioma}' no valido.", "Usa es, es_CO, es_MX, en_US..."));
        }
        if (d.Categoria == WhatsAppTemplateCategory.Authentication)
        {
            issues.Add(new("error", "categoria", "Authentication exige un boton de codigo (OTP) que este editor no arma.", "Usa Utility o Marketing."));
        }

        // --- Encabezado / pie ---
        if (d.TipoEncabezado == WhatsAppTemplateHeaderType.Text)
        {
            var h = (d.Encabezado ?? string.Empty).Trim();
            if (h.Length == 0) { issues.Add(new("error", "encabezado", "Encabezado de texto vacio.", "Escribe el encabezado o quitalo.")); }
            if (h.Length > 60) { issues.Add(new("error", "encabezado", $"El encabezado tiene {h.Length} caracteres (max 60).", "Acortalo.")); }
            if (Token.IsMatch(h)) { issues.Add(new("error", "encabezado", "El encabezado tiene variables (no soportado aqui).", "Deja las variables solo en el cuerpo.")); }
        }
        else if (d.TipoEncabezado is WhatsAppTemplateHeaderType.Image or WhatsAppTemplateHeaderType.Document or WhatsAppTemplateHeaderType.Video)
        {
            var url = (d.EncabezadoMediaUrl ?? string.Empty).Trim();
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new("error", "encabezado", "El encabezado de imagen/documento necesita una URL https publica de ejemplo.", "Sube la imagen en el editor o quita el encabezado."));
            }
        }
        var pie = (d.Pie ?? string.Empty).Trim();
        if (pie.Length > 60) { issues.Add(new("error", "pie", $"El pie tiene {pie.Length} caracteres (max 60).", "Acortalo.")); }
        if (Token.IsMatch(pie)) { issues.Add(new("error", "pie", "El pie no admite variables.", "Quita las variables del pie.")); }

        // --- Cuerpo ---
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            issues.Add(new("error", "cuerpo", "El cuerpo esta vacio.", "Redacta el mensaje."));
            return Build(issues);
        }
        if (trimmed.Length > 1024)
        {
            issues.Add(new("error", "cuerpo", $"El cuerpo tiene {trimmed.Length} caracteres (max 1024).", "Acortalo."));
        }

        var used = Token.Matches(trimmed).Select(m => m.Groups[1].Value).ToList();
        var distinct = used.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var declared = d.Variables ?? Array.Empty<WhatsAppTemplateVariable>();
        foreach (var t in distinct)
        {
            var v = declared.FirstOrDefault(x => string.Equals(x.Token?.Trim(), t, StringComparison.OrdinalIgnoreCase));
            if (v is null || string.IsNullOrWhiteSpace(v.Example))
            {
                issues.Add(new("error", "variables", $"La variable {{{{{t}}}}} no tiene ejemplo.", "Meta exige un ejemplo realista por variable (p.ej. cliente = Juan Perez)."));
            }
        }
        foreach (var v in declared.Where(v => !string.IsNullOrWhiteSpace(v.Token) && !distinct.Contains(v.Token.Trim(), StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add(new("warning", "variables", $"La variable '{v.Token}' esta declarada pero no se usa en el cuerpo.", "Quitala o usala."));
        }

        if (distinct.Count > 0)
        {
            if (Regex.IsMatch(trimmed, @"^\{\{") || Regex.IsMatch(trimmed, @"\}\}[\s\.\!\?]*$"))
            {
                issues.Add(new("error", "cuerpo", "El cuerpo empieza o termina con una variable (Meta lo rechaza).", "Agrega texto fijo antes de la primera y despues de la ultima variable."));
            }
            if (Adjacent.IsMatch(trimmed))
            {
                issues.Add(new("error", "cuerpo", "Hay variables seguidas sin texto entre ellas.", "Separalas con palabras."));
            }
            // "Params Words Ratio Exceeds Limit": Meta pide bastante texto fijo por variable. Umbral conservador:
            // al menos 3 palabras fijas por cada aparicion de variable (+1).
            var words = Token.Replace(trimmed, " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words < (3 * used.Count) + 1)
            {
                issues.Add(new("error", "cuerpo", $"Demasiadas variables ({used.Count}) para {words} palabras de texto fijo (Meta: 'Params Words Ratio').",
                    "Agrega mas texto fijo o usa menos variables."));
            }
        }

        if (d.Categoria == WhatsAppTemplateCategory.Utility)
        {
            var lower = trimmed.ToLowerInvariant();
            var promo = PromoWords.Where(w => lower.Contains(w)).ToList();
            if (promo.Count > 0)
            {
                issues.Add(new("warning", "categoria", $"Lenguaje promocional en Utility ({string.Join(", ", promo)}): Meta la recategoriza a Marketing o la rechaza.",
                    "Usa la categoria Marketing o quita el tono promocional."));
            }
        }
        if (trimmed.Contains("\n\n\n", StringComparison.Ordinal))
        {
            issues.Add(new("warning", "cuerpo", "Hay mas de dos saltos de linea seguidos.", "Deja como maximo una linea en blanco."));
        }

        return Build(issues);
    }

    private static TemplateVerifyResult Build(List<TemplateIssue> issues)
        => new(issues.Count(i => i.Severity == "error"), issues.Count(i => i.Severity == "warning"), issues);
}
