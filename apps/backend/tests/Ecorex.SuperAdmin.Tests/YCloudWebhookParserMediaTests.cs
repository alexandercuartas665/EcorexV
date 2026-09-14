using System.Text.Json;
using Ecorex.SuperAdmin.RealTime;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// El parser de YCloud debe extraer la MEDIA entrante (link + mime + tipo) de imagenes/documentos/etc.,
/// para que el endpoint la descargue y la persista como adjunto (paridad con Evolution) y crear_tarea la
/// anexe a la tarea. Un mensaje de texto no trae media. Basado en el shape real de whatsappInboundMessage.
/// </summary>
public class YCloudWebhookParserMediaTests
{
    private static YCloudParsedMessage ParseOne(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = YCloudWebhookParser.Parse(doc.RootElement);
        return Assert.Single(list);
    }

    [Fact]
    public void Imagen_entrante_trae_link_mime_y_kind()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "wamid.IMG1",
            "to": "573001112233",
            "from": "573009998877",
            "type": "image",
            "sendTime": "2026-09-07T14:00:00Z",
            "customerProfile": { "name": "Cliente Prueba" },
            "image": { "link": "https://media.ycloud.com/abc.jpg", "mime_type": "image/jpeg", "caption": "mira esto" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.Equal("image", m.MediaKind);
        Assert.Equal("https://media.ycloud.com/abc.jpg", m.MediaLink);
        Assert.Equal("image/jpeg", m.MediaMime);
        Assert.Equal("mira esto", m.Body);   // caption
        Assert.Equal("573009998877", m.Phone);
        Assert.Equal("573001112233", m.To);
    }

    [Fact]
    public void Documento_entrante_trae_media_y_body_por_defecto()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "wamid.DOC1",
            "to": "573001112233",
            "from": "573009998877",
            "type": "document",
            "document": { "url": "https://media.ycloud.com/plano.pdf", "mimeType": "application/pdf" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.Equal("document", m.MediaKind);
        Assert.Equal("https://media.ycloud.com/plano.pdf", m.MediaLink);   // acepta 'url'
        Assert.Equal("application/pdf", m.MediaMime);                       // acepta 'mimeType'
        Assert.Equal("(document)", m.Body);                                 // sin caption
    }

    [Fact]
    public void Documento_entrante_con_filename_captura_el_nombre_original()
    {
        // Los documentos de WhatsApp traen el NOMBRE ORIGINAL en 'filename'; el parser lo expone en
        // MediaFileName para que el agente pueda registrarlo (ej. columna 'archivo').
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "wamid.DOC2",
            "to": "573001112233",
            "from": "573009998877",
            "type": "document",
            "document": { "link": "https://media.ycloud.com/x.xlsx", "mime_type": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "filename": "Productos y precios.xlsx" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.Equal("document", m.MediaKind);
        Assert.Equal("Productos y precios.xlsx", m.MediaFileName);
    }

    [Fact]
    public void Imagen_entrante_sin_filename_deja_MediaFileName_null()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "wamid.IMG2", "to": "573001112233", "from": "573009998877", "type": "image",
            "image": { "link": "https://media.ycloud.com/abc.jpg", "mime_type": "image/jpeg" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.Equal("image", m.MediaKind);
        Assert.Null(m.MediaFileName);
    }

    [Fact]
    public void Texto_entrante_no_trae_media()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "wamid.TXT1",
            "to": "573001112233",
            "from": "573009998877",
            "type": "text",
            "text": { "body": "hola necesito una cotizacion" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.Null(m.MediaKind);
        Assert.Null(m.MediaLink);
        Assert.Null(m.MediaMime);
        Assert.Equal("hola necesito una cotizacion", m.Body);
    }
}
