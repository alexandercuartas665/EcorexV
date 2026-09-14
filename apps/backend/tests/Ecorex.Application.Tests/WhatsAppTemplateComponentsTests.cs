using System.Text.Json;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Compilacion de plantillas HSM al formato Meta/YCloud (ADR-0029): tokens amigables {{cliente}} -> {{1}}
/// posicionales + arreglo de ejemplos, y armado de componentes HEADER/BODY/FOOTER.
/// </summary>
public class WhatsAppTemplateComponentsTests
{
    [Fact]
    public void CompileBody_ReemplazaTokensPorPosicionales_EnOrden()
    {
        var vars = new List<(string, string?)> { ("cliente", "Juan"), ("empresa", "ACME") };
        var r = WhatsAppTemplateComponents.CompileBody("Hola {{cliente}} de {{ empresa }}, gracias.", vars);
        Assert.Equal("Hola {{1}} de {{2}}, gracias.", r.Text);
        Assert.Equal(new[] { "Juan", "ACME" }, r.Examples);
    }

    [Fact]
    public void CompileBody_VariableDeclaradaNoUsada_NoOcupaPosicion()
    {
        var vars = new List<(string, string?)> { ("cliente", "Juan"), ("nousada", "X") };
        var r = WhatsAppTemplateComponents.CompileBody("Hola {{cliente}}", vars);
        Assert.Equal("Hola {{1}}", r.Text);
        Assert.Single(r.Examples);
        Assert.Equal("Juan", r.Examples[0]);
    }

    [Fact]
    public void CompileBody_SinVariables_DejaTextoTalCual()
    {
        var r = WhatsAppTemplateComponents.CompileBody("Mensaje fijo.", new List<(string, string?)>());
        Assert.Equal("Mensaje fijo.", r.Text);
        Assert.Empty(r.Examples);
    }

    [Fact]
    public void Build_ArmaHeaderBodyFooter_ConEjemplo()
    {
        var t = new WhatsAppTemplate
        {
            Name = "aviso",
            Language = "es",
            Category = WhatsAppTemplateCategory.Utility,
            HeaderType = WhatsAppTemplateHeaderType.Text,
            HeaderText = "Aviso",
            BodyText = "Hola {{cliente}}, tu pedido llego.",
            FooterText = "Gracias",
            VariablesJson = """[{"token":"cliente","example":"Juan"}]"""
        };

        var json = JsonSerializer.Serialize(WhatsAppTemplateComponents.Build(t));
        Assert.Contains("\"type\":\"HEADER\"", json);
        Assert.Contains("\"type\":\"BODY\"", json);
        Assert.Contains("\"type\":\"FOOTER\"", json);
        Assert.Contains("{{1}}", json);
        Assert.Contains("body_text", json);
        Assert.Contains("Juan", json);
    }

    [Fact]
    public void Build_SinHeaderNiFooter_SoloBody()
    {
        var t = new WhatsAppTemplate
        {
            Name = "simple", Language = "es", Category = WhatsAppTemplateCategory.Marketing,
            HeaderType = WhatsAppTemplateHeaderType.None, BodyText = "Texto sin variables."
        };
        var json = JsonSerializer.Serialize(WhatsAppTemplateComponents.Build(t));
        Assert.Contains("\"type\":\"BODY\"", json);
        Assert.DoesNotContain("HEADER", json);
        Assert.DoesNotContain("FOOTER", json);
    }

    [Fact]
    public void Build_HeaderImagen_UsaFormatImageYHeaderUrl()
    {
        var t = new WhatsAppTemplate
        {
            Name = "aviso_img", Language = "es", Category = WhatsAppTemplateCategory.Utility,
            HeaderType = WhatsAppTemplateHeaderType.Image,
            HeaderMediaUrl = "https://cdn.example.com/banner.jpg",
            BodyText = "Tienes la tarea {{numero}}.",
            VariablesJson = """[{"token":"numero","example":"T00042"}]"""
        };
        var json = JsonSerializer.Serialize(WhatsAppTemplateComponents.Build(t));
        Assert.Contains("\"type\":\"HEADER\"", json);
        Assert.Contains("\"format\":\"IMAGE\"", json);
        Assert.Contains("header_url", json);
        Assert.Contains("https://cdn.example.com/banner.jpg", json);
        Assert.Contains("{{1}}", json);
    }

    [Fact]
    public void Build_HeaderImagenSinUrl_NoAgregaHeader()
    {
        var t = new WhatsAppTemplate
        {
            Name = "aviso_img2", Language = "es", Category = WhatsAppTemplateCategory.Utility,
            HeaderType = WhatsAppTemplateHeaderType.Image, HeaderMediaUrl = null,
            BodyText = "Cuerpo."
        };
        var json = JsonSerializer.Serialize(WhatsAppTemplateComponents.Build(t));
        Assert.DoesNotContain("HEADER", json);
    }

    [Fact]
    public void MediaHeaderFormat_MapeaTipos()
    {
        Assert.Equal("IMAGE", WhatsAppTemplateComponents.MediaHeaderFormat(WhatsAppTemplateHeaderType.Image));
        Assert.Equal("DOCUMENT", WhatsAppTemplateComponents.MediaHeaderFormat(WhatsAppTemplateHeaderType.Document));
        Assert.Equal("VIDEO", WhatsAppTemplateComponents.MediaHeaderFormat(WhatsAppTemplateHeaderType.Video));
        Assert.Null(WhatsAppTemplateComponents.MediaHeaderFormat(WhatsAppTemplateHeaderType.Text));
        Assert.Null(WhatsAppTemplateComponents.MediaHeaderFormat(null));
    }

    [Fact]
    public void Build_VariablesJsonPascalCase_GeneraExampleEnBody()
    {
        // La app guarda VariablesJson en PascalCase (System.Text.Json por defecto). El BODY DEBE llevar
        // example o YCloud rechaza con "component of type BODY is missing expected field(s) (example)".
        var t = new WhatsAppTemplate
        {
            Name = "aviso_tarea", Language = "es", Category = WhatsAppTemplateCategory.Utility,
            BodyText = "Se registro la tarea {{numero}} - {{titulo}}. Abrela: {{enlace}}",
            VariablesJson = """[{"Token":"numero","Example":"T00042"},{"Token":"titulo","Example":"Cotizacion"},{"Token":"enlace","Example":"https://x/y"}]"""
        };
        var json = JsonSerializer.Serialize(WhatsAppTemplateComponents.Build(t));
        Assert.Contains("\"type\":\"BODY\"", json);
        Assert.Contains("body_text", json);
        Assert.Contains("{{1}}", json);
        Assert.Contains("{{3}}", json);
        Assert.Contains("T00042", json);
        Assert.Contains("https://x/y", json);
    }

    [Fact]
    public void ParseVariables_ToleraPascalCaseYMinuscula()
    {
        var pascal = WhatsAppTemplateComponents.ParseVariables("""[{"Token":"a","Example":"1"}]""");
        var lower = WhatsAppTemplateComponents.ParseVariables("""[{"token":"a","example":"1"}]""");
        Assert.Equal(("a", "1"), (pascal[0].Token, pascal[0].Example));
        Assert.Equal(("a", "1"), (lower[0].Token, lower[0].Example));
    }

    [Fact]
    public void MetaCategory_EnMayusculas()
        => Assert.Equal("UTILITY", WhatsAppTemplateComponents.MetaCategory(WhatsAppTemplateCategory.Utility));
}
