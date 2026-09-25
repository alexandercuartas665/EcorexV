using System.Text.Json;
using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del parseo de plantillas de YCloud (formato Meta) que alimenta el import de plantillas de
/// WhatsApp. Valida que se extraigan cabecera/cuerpo/pie, categoria, estado y los ejemplos de las
/// variables POSICIONALES ({{1}}...), sin red.
/// </summary>
public class YCloudTemplateParserTests
{
    private static JsonElement Item(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseItem_PlantillaCompleta_ExtraeTodo()
    {
        // Forma tipica que YCloud devuelve en items[] (equivalente a Meta).
        var it = Item("""
        {
          "name": "confirmacion_cita",
          "language": "es",
          "status": "APPROVED",
          "id": "wa-tpl-123",
          "category": "UTILITY",
          "components": [
            { "type": "HEADER", "format": "TEXT", "text": "Hola {{1}}" },
            { "type": "BODY", "text": "Tu cita es el {{1}} a las {{2}}.",
              "example": { "body_text": [ [ "15 de julio", "3:00 pm" ] ] } },
            { "type": "FOOTER", "text": "Gracias por preferirnos" }
          ]
        }
        """);

        var t = YCloudTemplateParser.ParseItem(it);

        Assert.NotNull(t);
        Assert.Equal("confirmacion_cita", t!.Name);
        Assert.Equal("es", t.Language);
        Assert.Equal("APPROVED", t.Status);
        Assert.Equal("wa-tpl-123", t.Id);
        Assert.Equal("UTILITY", t.Category);
        Assert.Equal("TEXT", t.HeaderFormat);
        Assert.Equal("Hola {{1}}", t.HeaderText);
        Assert.Equal("Tu cita es el {{1}} a las {{2}}.", t.BodyText);
        Assert.Equal("Gracias por preferirnos", t.FooterText);
        Assert.NotNull(t.VariableExamples);
        Assert.Equal(new[] { "15 de julio", "3:00 pm" }, t.VariableExamples!);
    }

    [Fact]
    public void ParseItem_HeaderNoTexto_NoTraeHeaderText()
    {
        var it = Item("""
        {
          "name": "promo_imagen", "language": "es", "status": "PENDING", "category": "MARKETING",
          "components": [
            { "type": "HEADER", "format": "IMAGE" },
            { "type": "BODY", "text": "Oferta especial para ti." }
          ]
        }
        """);

        var t = YCloudTemplateParser.ParseItem(it);

        Assert.NotNull(t);
        Assert.Equal("IMAGE", t!.HeaderFormat);
        Assert.Null(t.HeaderText);
        Assert.Equal("Oferta especial para ti.", t.BodyText);
        Assert.Null(t.VariableExamples); // sin ejemplos
    }

    [Fact]
    public void ParseItem_SinComponents_CuerpoNull()
    {
        var it = Item("""{ "name": "solo_cabecera", "language": "en", "status": "APPROVED" }""");
        var t = YCloudTemplateParser.ParseItem(it);
        Assert.NotNull(t);
        Assert.Null(t!.BodyText);
        Assert.Null(t.HeaderFormat);
    }

    [Fact]
    public void ParseItem_SinNombre_DevuelveNull()
    {
        var it = Item("""{ "language": "es", "status": "APPROVED" }""");
        Assert.Null(YCloudTemplateParser.ParseItem(it));
    }

    [Fact]
    public void ParseItem_ConBotones_ExtraeUrlQuickReplyYTelefono()
    {
        var it = Item("""
        {
          "name": "entrega_cotizacion_botones", "language": "es_CO", "status": "APPROVED", "category": "UTILITY",
          "components": [
            { "type": "BODY", "text": "Hola {{1}}, aqui esta {{2}}." },
            { "type": "BUTTONS", "buttons": [
              { "type": "URL", "text": "Aprobar", "url": "https://app2.bitcode.com.co/d/{{1}}", "example": ["abc123"] },
              { "type": "URL", "text": "Sitio", "url": "https://bitcode.com.co" },
              { "type": "QUICK_REPLY", "text": "Rechazar" },
              { "type": "PHONE_NUMBER", "text": "Llamar", "phone_number": "+573001112233" }
            ]}
          ]
        }
        """);

        var t = YCloudTemplateParser.ParseItem(it);

        Assert.NotNull(t);
        Assert.NotNull(t!.Buttons);
        Assert.Equal(4, t.Buttons!.Count);

        // URL con variable {{1}} -> dinamico.
        Assert.Equal("URL", t.Buttons[0].Type);
        Assert.Equal("Aprobar", t.Buttons[0].Text);
        Assert.Equal("https://app2.bitcode.com.co/d/{{1}}", t.Buttons[0].Url);
        Assert.True(t.Buttons[0].HasUrlVariable);

        // URL fija -> NO dinamico.
        Assert.Equal("URL", t.Buttons[1].Type);
        Assert.False(t.Buttons[1].HasUrlVariable);

        // Quick reply y telefono.
        Assert.Equal("QUICK_REPLY", t.Buttons[2].Type);
        Assert.Equal("PHONE_NUMBER", t.Buttons[3].Type);
        Assert.Equal("+573001112233", t.Buttons[3].PhoneNumber);
    }

    [Fact]
    public void ParseItem_SinBotones_ButtonsNull()
    {
        var it = Item("""
        { "name": "sin_botones", "language": "es", "status": "APPROVED",
          "components": [ { "type": "BODY", "text": "Hola." } ] }
        """);
        var t = YCloudTemplateParser.ParseItem(it);
        Assert.NotNull(t);
        Assert.Null(t!.Buttons);
    }
}
