using System.Text;
using System.Text.RegularExpressions;

namespace Ecorex.Application.Gestor.TemplateAgent;

/// <summary>Hallazgo del verificador de plantillas. Severity: "error" (bloquea el cierre del agente) o "warning".</summary>
public sealed record TemplateIssue(string Severity, string Where, string Problem, string Fix);

/// <summary>Resultado de verificar una plantilla.</summary>
public sealed record TemplateVerifyResult(int Errors, int Warnings, IReadOnlyList<TemplateIssue> Issues)
{
    /// <summary>Detalle legible de los errores (para reinyectarlo al agente en el cierre obligatorio).</summary>
    public string ErrorDetail()
    {
        var sb = new StringBuilder();
        foreach (var i in Issues.Where(x => x.Severity == "error"))
        {
            sb.Append("- ").Append(i.Where).Append(": ").Append(i.Problem).Append(" -> ").Append(i.Fix).Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>
/// Verificador DETERMINISTA de plantillas de correo (ADR-0174). Lo usa la herramienta verify_email del agente
/// y el cierre obligatorio del turno (el sistema lo corre aunque el modelo no lo llame), y tambien el boton
/// "Verificar" del editor. Reglas: variables conocidas, HTML apto para correo (sin JS/formularios/iframes),
/// enlaces validos y seguros, imagenes con alt, tamano bajo el recorte de Gmail, asunto razonable y
/// {enlace_baja} presente (el motor envia correos masivos/comerciales).
/// </summary>
public static class EmailTemplateVerifier
{
    /// <summary>Gmail recorta el mensaje ("[Mensaje recortado]") por encima de ~102 KB de HTML.</summary>
    public const int GmailClipBytes = 102 * 1024;

    /// <summary>Asunto largo: se corta en la bandeja de entrada (movil ~40, escritorio ~80).</summary>
    public const int SubjectMaxChars = 80;

    private static readonly Regex ImgTag = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AltAttr = new(@"\balt\s*=\s*(""[^""]*""|'[^']*')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnchorHref = new(@"<a\b[^>]*?\bhref\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnchorTag = new(@"<a\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Forbidden = new(@"<\s*(script|form|iframe|object|embed|input|button|select|textarea)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EventAttr = new(@"<[^>]+\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExternalCss = new(@"<link\b[^>]*rel\s*=\s*[""']?stylesheet", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleBlock = new(@"<style\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TemplateVerifyResult Verify(string? asunto, string? cuerpoHtml)
    {
        var issues = new List<TemplateIssue>();
        var subject = asunto ?? string.Empty;
        var body = cuerpoHtml ?? string.Empty;

        // --- Asunto ---
        if (string.IsNullOrWhiteSpace(subject))
        {
            issues.Add(new("error", "asunto", "El asunto esta vacio.", "Escribe un asunto corto y concreto (idealmente con {nombre} o {empresa})."));
        }
        else if (subject.Trim().Length > SubjectMaxChars)
        {
            issues.Add(new("warning", "asunto", $"El asunto tiene {subject.Trim().Length} caracteres; se corta en la bandeja de entrada.", $"Acortalo a menos de {SubjectMaxChars} caracteres."));
        }

        // --- Variables desconocidas (asunto + cuerpo) ---
        var known = EmailTemplateService.Variables.Select(v => v.Token).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (where, text) in new[] { ("asunto", subject), ("cuerpo", body) })
        {
            var unknown = EmailTemplateService.AnyTokenRegex.Matches(text)
                .Select(m => m.Groups[1].Value)
                .Where(t => !known.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var u in unknown)
            {
                issues.Add(new("error", where, $"La variable {{{u}}} no existe; saldria tal cual en el correo.",
                    "Usa solo: " + string.Join(" ", known.Select(k => "{" + k + "}")) + "."));
            }
        }

        // --- Cuerpo ---
        if (string.IsNullOrWhiteSpace(body))
        {
            issues.Add(new("error", "cuerpo", "El cuerpo esta vacio.", "Redacta el contenido del correo en HTML."));
            return Build(issues);
        }

        var bytes = Encoding.UTF8.GetByteCount(body);
        if (bytes > GmailClipBytes)
        {
            issues.Add(new("warning", "cuerpo", $"El HTML pesa {bytes / 1024} KB; Gmail recorta los correos de mas de 102 KB.", "Simplifica el marcado (menos tablas anidadas y estilos repetidos) o quita contenido."));
        }

        if (Forbidden.IsMatch(body))
        {
            issues.Add(new("error", "cuerpo", "Contiene script/formulario/iframe u otro elemento que los clientes de correo bloquean.", "Quitalo: un correo solo admite contenido estatico (texto, tablas, imagenes y enlaces)."));
        }
        if (EventAttr.IsMatch(body))
        {
            issues.Add(new("error", "cuerpo", "Contiene atributos de evento (onclick, onload...).", "Quitalos: JavaScript no corre en correos y marca el mensaje como sospechoso."));
        }
        if (ExternalCss.IsMatch(body))
        {
            issues.Add(new("warning", "cuerpo", "Usa una hoja de estilos externa (<link rel=stylesheet>); la mayoria de clientes la ignora.", "Pasa los estilos a atributos style en linea."));
        }
        else if (StyleBlock.IsMatch(body))
        {
            issues.Add(new("warning", "cuerpo", "Usa un bloque <style>; algunos clientes (Gmail en apps de terceros, Outlook) lo ignoran.", "Prefiere estilos en linea (style=\"...\") en cada elemento."));
        }

        // Imagenes sin alt (accesibilidad y clientes que bloquean imagenes).
        var imgsNoAlt = ImgTag.Matches(body).Count(m => !AltAttr.IsMatch(m.Value));
        if (imgsNoAlt > 0)
        {
            issues.Add(new("warning", "cuerpo", $"{imgsNoAlt} imagen(es) sin texto alternativo (alt).", "Agrega alt=\"...\" con una descripcion corta a cada <img>."));
        }

        // Enlaces: vacios, '#', javascript:, http (no seguro).
        var anchors = AnchorTag.Matches(body).Count;
        var hrefs = AnchorHref.Matches(body).Select(m => (m.Groups[2].Success && m.Groups[2].Length > 0 ? m.Groups[2].Value : m.Groups[3].Value).Trim()).ToList();
        var noHref = anchors - hrefs.Count;
        var empty = hrefs.Count(h => h.Length == 0 || h == "#") + Math.Max(noHref, 0);
        if (empty > 0)
        {
            issues.Add(new("error", "cuerpo", $"{empty} enlace(s) vacio(s) o con '#'.", "Pon la URL real (https://...) o quita el enlace."));
        }
        if (hrefs.Any(h => h.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new("error", "cuerpo", "Hay enlaces javascript:.", "Reemplazalos por una URL https://."));
        }
        var insecure = hrefs.Count(h => h.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (insecure > 0)
        {
            issues.Add(new("warning", "cuerpo", $"{insecure} enlace(s) con http:// (no seguro).", "Usa https:// si el sitio lo soporta."));
        }

        // Baja de correos: el motor envia correos masivos/comerciales -> el enlace de baja es obligatorio.
        if (!body.Contains("{enlace_baja}", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("error", "cuerpo", "Falta el enlace para darse de baja ({enlace_baja}).",
                "Agrega al pie un enlace como <a href=\"{enlace_baja}\">Dejar de recibir estos correos</a>."));
        }

        return Build(issues);
    }

    private static TemplateVerifyResult Build(List<TemplateIssue> issues)
        => new(issues.Count(i => i.Severity == "error"), issues.Count(i => i.Severity == "warning"), issues);
}
