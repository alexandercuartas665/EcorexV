using System.Text.Json;
using Ecorex.Application.Gestor;
using Ecorex.Application.Gestor.TemplateAgent;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0186: botones (respuesta rapida, sitio web, llamar) e imagen de la galeria en plantillas de WhatsApp creadas
/// desde el sistema: normalizacion, reglas de Meta, compilacion del componente BUTTONS, argumentos del agente y la
/// media del encabezado al enviar desde el motor.
/// </summary>
public class WhatsAppTemplateButtonsTests
{
    private static WhatsAppTemplateButtonDto Btn(string type, string text, string? url = null, string? phone = null)
        => new(type, text, url, phone, false);

    [Fact]
    public void Normaliza_tipos_y_agrupa_respuestas_rapidas_primero()
    {
        var list = WhatsAppTemplateButtons.Normalize(new[]
        {
            Btn("sitio_web", " Ver catalogo ", "https://soldarco.com"),
            Btn("respuesta_rapida", "Me interesa"),
            Btn("llamar", "Llamar", phone: "+573001234567"),
            Btn("respuesta_rapida", "Mas informacion"),
            Btn("respuesta_rapida", "   "),
        });
        Assert.Equal(new[] { "QUICK_REPLY", "QUICK_REPLY", "URL", "PHONE_NUMBER" }, list.Select(b => b.Type));
        Assert.Equal(new[] { "Me interesa", "Mas informacion", "Ver catalogo", "Llamar" }, list.Select(b => b.Text));
    }

    [Fact]
    public void Botones_validos_no_tienen_problemas()
        => Assert.Empty(WhatsAppTemplateButtons.Problems(new[]
        {
            Btn("QUICK_REPLY", "Me interesa"), Btn("URL", "Ver catalogo", "https://soldarco.com/catalogo"),
            Btn("PHONE_NUMBER", "Llamar", phone: "+57 300 123 4567"),
        }));

    [Fact]
    public void Reglas_de_meta_para_botones()
    {
        var p = WhatsAppTemplateButtons.Problems(new[]
        {
            Btn("QUICK_REPLY", "Un texto de boton demasiado largo"),
            Btn("QUICK_REPLY", "Si"), Btn("QUICK_REPLY", "si"),
            Btn("URL", "Web 1", "http://inseguro.com"),
            Btn("URL", "Web 2", "https://a.com"), Btn("URL", "Web 3", "https://b.com"),
            Btn("PHONE_NUMBER", "Llamar", phone: "3001234567"),
            Btn("PHONE_NUMBER", "Llamar 2", phone: "+573001234567"),
        });
        Assert.Contains(p, x => x.Contains("max 25"));
        Assert.Contains(p, x => x.Contains("repetido"));
        Assert.Contains(p, x => x.Contains("https://"));
        Assert.Contains(p, x => x.Contains("Maximo 2 botones de sitio web"));
        Assert.Contains(p, x => x.Contains("Maximo 1 boton de llamar"));
        Assert.Contains(p, x => x.Contains("formato internacional"));
    }

    [Fact]
    public void Componente_buttons_de_meta()
    {
        var t = new WhatsAppTemplate
        {
            Name = "promo",
            Language = "es",
            BodyText = "Hola {{nombre}}, mira el catalogo nuevo de este mes.",
            VariablesJson = "[{\"Token\":\"nombre\",\"Example\":\"Ana\"}]",
            HeaderType = WhatsAppTemplateHeaderType.Image,
            HeaderMediaUrl = "https://app.test/i/abc.jpg",
            ButtonsJson = WhatsAppTemplateButtons.ToJson(WhatsAppTemplateButtons.Normalize(new[]
            {
                Btn("URL", "Ver catalogo", "https://soldarco.com"), Btn("QUICK_REPLY", "Me interesa"),
                Btn("PHONE_NUMBER", "Llamar", phone: "+573001234567"),
            })),
        };
        var json = JsonSerializer.Serialize(WhatsAppTemplateComponents.Build(t));
        Assert.Contains("\"type\":\"HEADER\",\"format\":\"IMAGE\"", json);
        Assert.Contains("\"header_url\":[\"https://app.test/i/abc.jpg\"]", json);
        Assert.Contains("\"type\":\"BUTTONS\"", json);
        Assert.Contains("{\"type\":\"QUICK_REPLY\",\"text\":\"Me interesa\"}", json);
        Assert.Contains("{\"type\":\"URL\",\"text\":\"Ver catalogo\",\"url\":\"https://soldarco.com\"}", json);
        Assert.Contains("{\"type\":\"PHONE_NUMBER\",\"text\":\"Llamar\",\"phone_number\":\"\\u002B573001234567\"}", json);
    }

    [Fact]
    public void Sin_botones_no_hay_componente_buttons()
    {
        var t = new WhatsAppTemplate { Name = "x", Language = "es", BodyText = "Hola, gracias por escribirnos hoy." };
        Assert.DoesNotContain("BUTTONS", JsonSerializer.Serialize(WhatsAppTemplateComponents.Build(t)));
    }

    [Fact]
    public void Agente_lee_imagen_y_botones_de_sus_argumentos()
    {
        using var doc = JsonDocument.Parse("""
            {"nombre":"promo_soldaduras","categoria":"Marketing","cuerpo":"Hola {{nombre}}, llego el catalogo nuevo de soldaduras.",
             "variables":[{"token":"nombre","ejemplo":"Ana"}],
             "imagen_url":"https://app.test/i/abc.png",
             "botones":[{"tipo":"sitio_web","texto":"Ver catalogo","url":"https://soldarco.com"},{"tipo":"respuesta_rapida","texto":"Me interesa"}]}
            """);
        var d = WhatsAppTemplateAgentToolset.DraftFromArgs(doc.RootElement);
        Assert.Equal(WhatsAppTemplateHeaderType.Image, d.TipoEncabezado);
        Assert.Equal("https://app.test/i/abc.png", d.EncabezadoMediaUrl);
        Assert.Equal(new[] { "QUICK_REPLY", "URL" }, d.Botones!.Select(b => b.Type));
        Assert.Equal(0, WhatsAppTemplateVerifier.Verify(d).Errors);
        var preview = WhatsAppTemplateAgentToolset.Preview(d);
        Assert.Contains("[Imagen: https://app.test/i/abc.png]", preview);
        Assert.Contains("[ Ver catalogo ] -> https://soldarco.com", preview);
    }

    [Fact]
    public void Sin_lista_de_botones_el_agente_no_los_toca()
    {
        using var doc = JsonDocument.Parse("""{"nombre":"x","cuerpo":"Hola"}""");
        Assert.Null(WhatsAppTemplateAgentToolset.ButtonsFromArgs(doc.RootElement));
    }

    [Theory]
    [InlineData("https://app.test/i/abc.gif")]
    [InlineData("https://app.test/i/abc.webp")]
    public void Verificador_rechaza_imagen_que_no_es_jpg_ni_png(string url)
    {
        var d = new WhatsAppDraft("x", "es", WhatsAppTemplateCategory.Marketing, WhatsAppTemplateHeaderType.Image, null, url,
            "Hola, este es el catalogo nuevo del mes.", null, Array.Empty<WhatsAppTemplateVariable>());
        Assert.Contains(WhatsAppTemplateVerifier.Verify(d).Issues, i => i.Problem.Contains("JPG o PNG"));
    }

    [Fact]
    public void Motor_envia_la_media_del_encabezado()
    {
        Assert.Equal(("image", "https://a/i.png"), ContactWorkflowDispatcher.HeaderMediaFor(WhatsAppTemplateHeaderType.Image, " https://a/i.png "));
        Assert.Equal(("document", "https://a/d.pdf"), ContactWorkflowDispatcher.HeaderMediaFor(WhatsAppTemplateHeaderType.Document, "https://a/d.pdf"));
        Assert.Equal(((string?)null, (string?)null), ContactWorkflowDispatcher.HeaderMediaFor(WhatsAppTemplateHeaderType.Text, "https://a/i.png"));
        Assert.Equal(((string?)null, (string?)null), ContactWorkflowDispatcher.HeaderMediaFor(WhatsAppTemplateHeaderType.Image, null));
    }
}
