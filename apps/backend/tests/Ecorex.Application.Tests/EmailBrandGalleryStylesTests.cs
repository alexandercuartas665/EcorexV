using Ecorex.Application.Gestor;
using Ecorex.Application.Gestor.TemplateAgent;
using Ecorex.Application.Storage;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0176: marca de correo, galeria de imagenes y estilos de diseno del agente de correos. Cubre las piezas
/// deterministas: dimensiones/formato de imagen por cabecera, validacion de colores/URL de la marca, catalogo y
/// etiqueta de estilos, el arnes (marca + estilos + imagenes) y las reglas nuevas del verificador para <img>.
/// </summary>
public class EmailBrandGalleryStylesTests
{
    // ---------- Imagenes: formato y dimensiones por cabecera ----------

    private static byte[] Png(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    private static byte[] Gif(int w, int h)
        => new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', (byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8), 0, 0, 0 };

    // SOI + APP0 (longitud 16) + SOF0 con alto/ancho.
    private static byte[] Jpeg(int w, int h)
    {
        var list = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        list.AddRange(new byte[14]);
        list.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 0x03 });
        list.AddRange(new byte[12]);
        return list.ToArray();
    }

    [Fact]
    public void Dimensiones_png_gif_y_jpeg_se_leen_de_la_cabecera()
    {
        Assert.Equal((1200, 630), ImageDimensions.TryRead(Png(1200, 630)));
        Assert.Equal((300, 250), ImageDimensions.TryRead(Gif(300, 250)));
        Assert.Equal((800, 600), ImageDimensions.TryRead(Jpeg(800, 600)));
    }

    [Fact]
    public void Dimensiones_de_algo_que_no_es_imagen_o_truncado_son_null()
    {
        Assert.Null(ImageDimensions.TryRead("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray()));
        Assert.Null(ImageDimensions.TryRead(Png(10, 10)[..12]));
        Assert.Null(ImageDimensions.TryRead(Array.Empty<byte>()));
    }

    [Fact]
    public void Formato_se_detecta_por_contenido_y_rechaza_svg_y_webp()
    {
        Assert.Equal(("image/png", ".png"), TemplateGalleryService.Sniff(Png(1, 1)));
        Assert.Equal(("image/gif", ".gif"), TemplateGalleryService.Sniff(Gif(1, 1)));
        Assert.Equal(("image/jpeg", ".jpg"), TemplateGalleryService.Sniff(Jpeg(1, 1)));
        Assert.Null(TemplateGalleryService.Sniff("<svg/>"u8.ToArray()));
        Assert.Null(TemplateGalleryService.Sniff("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
    }

    [Fact]
    public void Ruta_publica_de_la_galeria_usa_el_id_del_archivo()
    {
        var id = Guid.Parse("0196f000-0000-7000-8000-000000000abc");
        Assert.Equal("/i/0196f000000070008000000000000abc.png", TemplateGalleryService.PublicPath(id, ".png"));
    }

    // ---------- Marca: validacion ----------

    [Theory]
    [InlineData("#0D2C4B", "#0d2c4b")]
    [InlineData("0d2c4b", "#0d2c4b")]
    [InlineData("#fa0", "#ffaa00")]
    [InlineData("", null)]
    public void Color_de_marca_se_normaliza(string input, string? expected)
    {
        Assert.True(EmailBrandService.TryColor(input, out var c));
        Assert.Equal(expected, c);
    }

    [Theory]
    [InlineData("rojo")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    public void Color_de_marca_invalido_se_rechaza(string input)
        => Assert.False(EmailBrandService.TryColor(input, out _));

    [Fact]
    public void Sitio_web_agrega_https_y_rechaza_basura()
    {
        Assert.True(EmailBrandService.TryUrl("bitcode.com.co", out var u));
        Assert.Equal("https://bitcode.com.co", u);
        Assert.False(EmailBrandService.TryUrl("no es una url", out _));
    }

    // ---------- Estilos ----------

    [Fact]
    public void Catalogo_de_estilos_tiene_claves_unicas_y_el_default()
    {
        Assert.Equal(EmailDesignStyles.All.Count, EmailDesignStyles.All.Select(s => s.Key).Distinct().Count());
        Assert.NotNull(EmailDesignStyles.Find(EmailDesignStyles.DefaultKey));
        Assert.All(EmailDesignStyles.All, s => Assert.Equal(3, s.Muestra.Length));
    }

    [Fact]
    public void Etiqueta_de_estilo_se_lee_y_se_quita_para_mostrar()
    {
        var msg = EmailDesignStyles.TagFor("moderno") + "Crea un correo de bienvenida";
        Assert.Equal("moderno", EmailDesignStyles.ReadTag(msg));
        Assert.Equal("Crea un correo de bienvenida", EmailDesignStyles.StripTag(msg));
        Assert.Null(EmailDesignStyles.ReadTag("[estilo: inventado] hola"));
        Assert.Equal("sin etiqueta", EmailDesignStyles.StripTag("sin etiqueta"));
        Assert.Equal("elegante", EmailDesignStyles.Normalize("ELEGANTE"));
    }

    // ---------- Arnes ----------

    private static EmailBrandInfo Brand(string? prim = "#0d2c4b", string? acc = "#e8833a") => new(
        "BITCODE", "https://app.test/i/abc.png", prim, acc, "moderno", "https://bitcode.com.co", "Lun-Vie 8-6",
        "Cra 7 # 1-2", "Cali", "300 123", "info@bitcode.test");

    [Fact]
    public void Arnes_incluye_marca_contacto_real_estilos_e_imagenes()
    {
        var p = TemplateAgentHarness.EmailSystemPrompt("BITCODE", editingExisting: false, Brand());
        Assert.Contains("#0d2c4b", p);
        Assert.Contains("#e8833a", p);
        Assert.Contains("https://app.test/i/abc.png", p);
        Assert.Contains("El logo es OBLIGATORIO", p); // visto en el E2E: con hero el agente omitia el logo
        Assert.Contains("Cra 7 # 1-2", p);
        Assert.Contains("https://bitcode.com.co", p);
        Assert.Contains("'moderno'", p); // estilo por defecto de la marca
        Assert.All(EmailDesignStyles.All, s => Assert.Contains("* " + s.Key + " (", p));
        Assert.Contains("list_images", p);
        Assert.DoesNotContain("deja un marcador claro", p);
        // Regla de idioma al FINAL (recencia): visto en el E2E, tras un resultado JSON el modelo respondio en frances.
        Assert.StartsWith("IDIOMA:", p.TrimEnd().Split('\n').Last().Trim());
    }

    [Fact]
    public void Arnes_conserva_el_estilo_de_la_plantilla_abierta()
    {
        var p = TemplateAgentHarness.EmailSystemPrompt("BITCODE", editingExisting: true, Brand(), templateStyle: "elegante");
        Assert.Contains("se compuso con el estilo 'elegante'", p);
    }

    [Fact]
    public void Arnes_sin_colores_ni_contacto_no_inventa_marcadores()
    {
        var b = new EmailBrandInfo("ACME", null, null, null, "corporativo", null, null, null, null, null, null);
        var p = TemplateAgentHarness.EmailSystemPrompt("ACME", editingExisting: false, b);
        Assert.Contains("No hay colores de marca configurados", p);
        Assert.Contains("sin marcadores", p);
        Assert.Contains("wordmark", p);
    }

    [Fact]
    public void Arnes_es_solo_ascii()
    {
        var p = TemplateAgentHarness.EmailSystemPrompt("BITCODE", editingExisting: false, Brand());
        Assert.All(p, ch => Assert.True(ch < 128, $"Caracter no ASCII en el arnes: U+{(int)ch:X4}"));
    }

    // ---------- Versiones ----------

    [Fact]
    public void Version_de_correo_se_lee_con_y_sin_estilo()
    {
        var nueva = EmailTemplateAgentToolset.ParseSnapshot("{\"nombre\":\"N\",\"asunto\":\"A\",\"cuerpoHtml\":\"<p>x</p>\",\"estilo\":\"elegante\"}");
        Assert.Equal("<p>x</p>", nueva!.CuerpoHtml);
        Assert.Equal("elegante", nueva.Estilo);
        // Versiones anteriores a ADR-0176 no traen estilo.
        var vieja = EmailTemplateAgentToolset.ParseSnapshot("{\"nombre\":\"N\",\"asunto\":\"A\",\"cuerpoHtml\":\"<p>y</p>\"}");
        Assert.Null(vieja!.Estilo);
        Assert.Null(EmailTemplateAgentToolset.ParseSnapshot("no es json"));
    }

    // ---------- Verificador: imagenes ----------

    private const string Base = "<table role=\"presentation\"><tr><td><p>Hola {nombre}</p>{IMG}<a href=\"{enlace_baja}\">Baja</a></td></tr></table>";

    [Fact]
    public void Verificador_img_sin_src_es_error()
    {
        var r = EmailTemplateVerifier.Verify("Hola", Base.Replace("{IMG}", "<img alt=\"x\">"));
        Assert.Contains(r.Issues, i => i.Severity == "error" && i.Problem.Contains("sin src"));
    }

    [Fact]
    public void Verificador_avisa_base64_ruta_relativa_http_y_svg()
    {
        var imgs = "<img src=\"data:image/png;base64,AAAA\" alt=\"a\">"
            + "<img src=\"/i/abc.png\" alt=\"b\">"
            + "<img src=\"http://x.test/c.png\" alt=\"c\">"
            + "<img src=\"https://x.test/d.svg?v=1\" alt=\"d\">";
        var r = EmailTemplateVerifier.Verify("Hola", Base.Replace("{IMG}", imgs));
        Assert.Equal(0, r.Errors);
        Assert.Contains(r.Issues, i => i.Problem.Contains("base64"));
        Assert.Contains(r.Issues, i => i.Problem.Contains("ruta relativa"));
        Assert.Contains(r.Issues, i => i.Problem.Contains("imagen(es) por http://"));
        Assert.Contains(r.Issues, i => i.Problem.Contains("SVG"));
    }

    [Fact]
    public void Verificador_img_https_de_la_galeria_no_genera_avisos()
    {
        var r = EmailTemplateVerifier.Verify("Hola", Base.Replace("{IMG}", "<img src=\"https://app.test/i/abc.png\" alt=\"Hero\" width=\"600\">"));
        Assert.Equal(0, r.Errors);
        Assert.Equal(0, r.Warnings);
    }
}
