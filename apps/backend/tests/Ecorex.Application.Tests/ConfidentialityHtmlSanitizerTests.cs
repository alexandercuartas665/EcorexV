using Ecorex.Application.Confidentiality;

namespace Ecorex.Application.Tests;

// ADR-0160 - Saneador del HTML de la politica de confidencialidad (se muestra en una pagina PUBLICA).
// Se permite formato basico e imagenes embebidas data:image (base64) en img/src, pero NUNCA script,
// data:text/html, ni esquemas peligrosos en href (XSS almacenado hacia los clientes del tenant).
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
    public void Elimina_script()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<p>ok</p><script>alert(1)</script>");
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>ok</p>", html);
    }

    [Fact]
    public void Conserva_imagen_base64_en_img_src()
    {
        // 1x1 PNG transparente embebido: el caso que el admin pega y antes no se renderizaba.
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var html = ConfidentialityHtmlSanitizer.Sanitize($"<p>logo</p><img src=\"{png}\" alt=\"logo\" />");
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("src=", html);
    }

    [Fact]
    public void Elimina_data_texto_html_en_img_src()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<img src=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\" />");
        Assert.DoesNotContain("data:text/html", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Elimina_data_image_en_href_de_enlace()
    {
        // data: NO se permite en href (solo en src): un <a> con data: queda sin href.
        var html = ConfidentialityHtmlSanitizer.Sanitize("<a href=\"data:image/png;base64,AAAA\">x</a>");
        Assert.DoesNotContain("href=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">x</a>", html);
    }

    [Fact]
    public void Elimina_javascript_en_href()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<a href=\"javascript:evil()\">x</a>");
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Conserva_enlace_https()
    {
        var html = ConfidentialityHtmlSanitizer.Sanitize("<a href=\"https://ejemplo.com\">sitio</a>");
        Assert.Contains("https://ejemplo.com", html);
    }
}
