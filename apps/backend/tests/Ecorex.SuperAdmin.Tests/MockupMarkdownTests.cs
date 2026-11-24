using Ecorex.SuperAdmin.Maqueta;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>Cerebro digital (ADR-0183): markdown -> HTML minimo y SANITIZADO (mismo origen que la app).</summary>
public sealed class MockupMarkdownTests
{
    [Fact]
    public void NoPermiteHtmlNiEnlacesPeligrosos()
    {
        var html = MockupMarkdown.ToHtml(
            "<script>alert(1)</script>\n<img src=x onerror=alert(1)>\n[clic](javascript:alert(1)) [[<b>x</b>]] **<i>y</i>**\n[ok](https://ecorex.co/a?b=1&c=\"2\")");

        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<i>y", html);
        Assert.DoesNotContain("href=\"javascript", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("href=\"https://ecorex.co/a?b=1&amp;c=%222%22\"", html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html);
    }

    [Fact]
    public void EnlacesInternos_SalenComoDataKind_YFantasmasMarcados()
    {
        var html = MockupMarkdown.ToHtml("Ver [[Existe]], [[Falta|otra]], [[inicio.html]] y [[T00046]] #ux",
            (kind, key) => kind != "note" || key == "existe");

        Assert.Contains("class=\"mb-wl k-note\" data-kind=\"note\" data-key=\"Existe\"", html);
        Assert.Contains("class=\"mb-wl k-note missing\" data-kind=\"note\" data-key=\"Falta\"", html);
        Assert.Contains(">otra</a>", html);
        Assert.Contains("data-kind=\"page\" data-key=\"inicio.html\"", html);
        Assert.Contains("data-kind=\"task\" data-key=\"T00046\"", html);
        Assert.Contains("<span class=\"mb-tag\" data-kind=\"tag\" data-key=\"ux\">#ux</span>", html);
    }

    [Fact]
    public void EnlaceConPipeEscapado_DeTablaDeObsidian_SaleResuelto()
    {
        var html = MockupMarkdown.ToHtml("| a | [[Modelo de Datos\\|Modelo]] |", (kind, key) => key == "modelo de datos");

        Assert.Contains("class=\"mb-wl k-note\" data-kind=\"note\" data-key=\"Modelo de Datos\"", html);
        Assert.Contains(">Modelo</a>", html);
        Assert.DoesNotContain("missing", html);
    }

    [Fact]
    public void Bloques_EncabezadosListasTareasCodigoYCitas()
    {
        var html = MockupMarkdown.ToHtml("## Titulo\n- uno\n- [x] hecho\n- [ ] pendiente\n1. primero\n> cita\n```\n<b>[[no]]</b>\n```\n---\ntexto **fuerte** y _suave_ y `co<de>`");

        Assert.Contains("<h2>Titulo</h2>", html);
        Assert.Contains("<ul><li>uno</li><li class=\"mb-task done\"><i class=\"fa-regular fa-square-check\"></i> hecho</li>", html);
        Assert.Contains("<li class=\"mb-task\"><i class=\"fa-regular fa-square\"></i> pendiente</li></ul>", html);
        Assert.Contains("<ol><li>primero</li></ol>", html);
        Assert.Contains("<blockquote><p>cita</p></blockquote>", html);
        Assert.Contains("<pre class=\"mb-code\"><code>&lt;b&gt;[[no]]&lt;/b&gt;</code></pre>", html);
        Assert.DoesNotContain("data-key=\"no\"", html);
        Assert.Contains("<hr>", html);
        Assert.Contains("<strong>fuerte</strong>", html);
        Assert.Contains("<em>suave</em>", html);
        Assert.Contains("<code>co&lt;de&gt;</code>", html);
    }

    [Fact]
    public void Adjuntos_ImagenesPdfYFaltantes_SinCargarImagenesExternas()
    {
        static string? Assets(string reference) => reference.Replace("%20", " ").Split('/').Last().ToLowerInvariant() switch
        {
            "foto.png" => "/archivo/11111111-1111-1111-1111-111111111111",
            "mi foto.png" => "/archivo/22222222-2222-2222-2222-222222222222",
            "manual.pdf" => "/archivo/33333333-3333-3333-3333-333333333333",
            "trampa.png" => "javascript:alert(1)",
            _ => null
        };

        var html = MockupMarkdown.ToHtml(
            "![[foto.png|300]] ![[Manual.pdf]] ![[falta.png]] ![[trampa.png]]\n![captura](img/Mi%20Foto.png) ![x](https://evil.test/p.png)",
            null, Assets);

        Assert.Contains("<img class=\"mb-img\" src=\"/archivo/11111111-1111-1111-1111-111111111111\" alt=\"foto.png\" loading=\"lazy\" width=\"300\">", html);
        Assert.Contains("<a class=\"mb-file\" href=\"/archivo/33333333-3333-3333-3333-333333333333\"", html);
        Assert.Contains("<span class=\"mb-embed missing\"", html);
        Assert.Contains("falta.png</span>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("src=\"/archivo/22222222-2222-2222-2222-222222222222\" alt=\"captura\"", html);
        Assert.DoesNotContain("<img class=\"mb-img\" src=\"https://", html);
        Assert.Contains("href=\"https://evil.test/p.png\"", html);
    }

    [Fact]
    public void Frontmatter_YCallout_DeObsidian()
    {
        var html = MockupMarkdown.ToHtml("---\ntags: [a]\n---\n> [!nota] Importante\n> detalle");

        Assert.StartsWith("<pre class=\"mb-props\">tags: [a]</pre>", html);
        Assert.Contains("<div class=\"mb-callout\"><div class=\"mb-callout-title\">Importante</div><p>detalle</p></div>", html);
    }

    [Fact]
    public void Tabla_DeObsidian_ConAlineacion_EnlacesConAlias_YCeldasFaltantes()
    {
        var html = MockupMarkdown.ToHtml(
            "Antes\n| Carpeta | Contenido | N |\n|:---|:---:|---:|\n| 03. Hoja | [[HOJA DE RUTA\\|Hoja]] y **clave** | 1 |\n| `a|b` | x \\| y |\n\nDespues",
            (kind, key) => key == "hoja de ruta");

        Assert.Contains("<p>Antes</p><div class=\"mb-table-wrap\"><table class=\"mb-table\"><thead><tr>", html);
        Assert.Contains("<th style=\"text-align:left\">Carpeta</th><th style=\"text-align:center\">Contenido</th><th style=\"text-align:right\">N</th>", html);
        Assert.Contains("<td style=\"text-align:left\">03. Hoja</td>", html);
        Assert.Contains("data-key=\"HOJA DE RUTA\"", html);
        Assert.Contains(">Hoja</a> y <strong>clave</strong></td>", html);
        // El '|' dentro de codigo y el "\|" escapado no parten la celda; la fila corta se completa con celdas vacias.
        Assert.Contains("<td style=\"text-align:left\"><code>a|b</code></td><td style=\"text-align:center\">x | y</td><td style=\"text-align:right\"></td>", html);
        Assert.Contains("</tbody></table></div><p>Despues</p>", html);
        Assert.DoesNotContain("|---", html);
    }

    [Fact]
    public void Tabla_NoPermiteHtml_YSinSeparadorNoEsTabla()
    {
        var html = MockupMarkdown.ToHtml("| <script>x</script> | b |\n|---|---|\n| <img src=x onerror=alert(1)> | [m](javascript:alert(1)) |");
        Assert.Contains("<th>&lt;script&gt;x&lt;/script&gt;</th>", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("href=\"javascript", html);

        var plano = MockupMarkdown.ToHtml("a | b\nsin separador");
        Assert.DoesNotContain("<table", plano);
        Assert.Contains("<p>a | b<br>sin separador</p>", plano);
    }
}
