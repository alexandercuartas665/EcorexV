using Ecorex.Application.Confidentiality;

namespace Ecorex.Application.Tests;

// ADR-0160 - Saneador del HTML de la politica de confidencialidad. El admin pega un DOCUMENTO HTML
// COMPLETO con su CSS; se CONSERVA estilo/estructura (se pinta en un iframe con sandbox) y solo se
// quita lo activo/peligroso: script, manejadores de eventos, esquemas javascript:/vbscript:, y vectores
// historicos en CSS.
public sealed class ConfidentialityHtmlSanitizerTests
{
    [Fact]
    public void Conserva_formato_basico()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<h2>Titulo</h2><p>Hola <strong>mundo</strong></p>");
        Assert.Contains("<h2>Titulo</h2>", html);
        Assert.Contains("<strong>mundo</strong>", html);
    }

    [Fact]
    public void Conserva_el_CSS_en_style_y_las_clases()
    {
        // El bug reportado: al guardar se borraba el <style>. Ahora se conserva (se rinde en iframe sandbox).
        var html = ConfidentialityHtmlSanitizer.Sanitize(
            "<style>.doc{color:red}</style><div class=\"doc\">Politica</div>");
        Assert.Contains("<style>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".doc{color:red}", html);
        Assert.Contains("class=\"doc\"", html);
    }

    [Fact]
    public void Conserva_un_documento_HTML_completo_pegado()
    {
        const string doc = "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"UTF-8\">" +
                           "<title>Politica</title><style>body{font-family:Arial}.c{max-width:900px}</style></head>" +
                           "<body><div class=\"c\"><h1>Politica de datos</h1><p>Contenido</p></div></body></html>";
        var html = ConfidentialityHtmlSanitizer.Sanitize(doc);
        Assert.Contains("font-family:Arial", html);          // el CSS del <head> sobrevive
        Assert.Contains("class=\"c\"", html);
        Assert.Contains("Politica de datos", html);
    }

    [Fact]
    public void Elimina_script()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<p>ok</p><script>alert(1)</script>");
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>ok</p>", html);
    }

    [Fact]
    public void Elimina_manejadores_de_eventos()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<p onclick=\"evil()\" onmouseover=\"x()\">texto</p>");
        Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("texto", html);
    }

    [Fact]
    public void Conserva_imagen_base64_en_img_src()
    {
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var html = ConfidentialityHtmlSanitizer.Sanitize($"<p>logo</p><img src=\"{png}\" alt=\"logo\" />");
        Assert.Contains("data:image/png;base64,", html);
    }

    [Fact]
    public void Elimina_javascript_en_href()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<a href=\"javascript:evil()\">x</a>");
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">x</a>", html);
    }

    [Fact]
    public void Neutraliza_vectores_en_css_inline()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<div style=\"width:expression(alert(1));color:blue\">x</div>");
        Assert.DoesNotContain("expression(", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("color:blue", html);
    }

    [Fact]
    public void Conserva_enlace_https()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<a href=\"https://ejemplo.com\">sitio</a>");
        Assert.Contains("https://ejemplo.com", html);
    }
}
