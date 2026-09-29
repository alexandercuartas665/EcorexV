using System.Text.Json;
using Ecorex.SuperAdmin.RealTime;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// El parser de Evolution debe reconocer DOCUMENTOS entrantes (documentMessage, y la envoltura
/// documentWithCaptionMessage cuando el usuario adjunta texto), marcarlos MessageType="document" y
/// exponer el nombre ORIGINAL en MediaFileName. Con eso el webhook descarga el PDF (facturas EmCali,
/// etc.), lo ingiere como Document (Gemini lo lee) y crear_tarea lo anexa a la tarea. Basado en el
/// shape real del evento messages.upsert. La instancia es ecorex_{tenant:N}_{linea:N}.
/// </summary>
public class EvolutionWebhookParserDocumentTests
{
    private const string Instance = "ecorex_11111111111111111111111111111111_22222222222222222222222222222222";

    private static ParsedInbound ParseOk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var parsed = EvolutionWebhookParser.Parse(doc.RootElement);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public void Documento_directo_marca_tipo_document_y_captura_el_nombre()
    {
        var json = $$"""
        {
          "event": "messages.upsert",
          "instance": "{{Instance}}",
          "data": {
            "key": { "remoteJid": "573009998877@s.whatsapp.net", "fromMe": false, "id": "EVT-DOC-1" },
            "pushName": "Cliente EmCali",
            "messageTimestamp": 1790000000,
            "message": {
              "documentMessage": {
                "fileName": "Factura_EmCali_928609.pdf",
                "mimetype": "application/pdf"
              }
            }
          }
        }
        """;
        var p = ParseOk(json);
        Assert.Equal("document", p.Payload.MessageType);
        Assert.Equal("Factura_EmCali_928609.pdf", p.Payload.MediaFileName);
        // Sin caption: el body cae al nombre del archivo (no "(mensaje no soportado)").
        Assert.Equal("Factura_EmCali_928609.pdf", p.Payload.Body);
    }

    [Fact]
    public void Documento_envuelto_con_caption_usa_el_caption_como_body()
    {
        var json = $$"""
        {
          "event": "messages.upsert",
          "instance": "{{Instance}}",
          "data": {
            "key": { "remoteJid": "573009998877@s.whatsapp.net", "fromMe": false, "id": "EVT-DOC-2" },
            "message": {
              "documentWithCaptionMessage": {
                "message": {
                  "documentMessage": {
                    "title": "Cotizacion",
                    "fileName": "Cotizacion_123.pdf",
                    "mimetype": "application/pdf",
                    "caption": "adjunto la factura, cuanto seria?"
                  }
                }
              }
            }
          }
        }
        """;
        var p = ParseOk(json);
        Assert.Equal("document", p.Payload.MessageType);
        Assert.Equal("Cotizacion_123.pdf", p.Payload.MediaFileName);
        Assert.Equal("adjunto la factura, cuanto seria?", p.Payload.Body);
    }

    [Fact]
    public void Documento_sin_fileName_cae_a_texto_documento()
    {
        var json = $$"""
        {
          "event": "messages.upsert",
          "instance": "{{Instance}}",
          "data": {
            "key": { "remoteJid": "573009998877@s.whatsapp.net", "fromMe": false, "id": "EVT-DOC-3" },
            "message": { "documentMessage": { "mimetype": "application/pdf" } }
          }
        }
        """;
        var p = ParseOk(json);
        Assert.Equal("document", p.Payload.MessageType);
        Assert.Null(p.Payload.MediaFileName);
        Assert.Equal("(documento)", p.Payload.Body);
    }

    [Fact]
    public void Texto_normal_sigue_siendo_text_sin_MediaFileName()
    {
        var json = $$"""
        {
          "event": "messages.upsert",
          "instance": "{{Instance}}",
          "data": {
            "key": { "remoteJid": "573009998877@s.whatsapp.net", "fromMe": false, "id": "EVT-TXT-1" },
            "message": { "conversation": "hola necesito una cotizacion" }
          }
        }
        """;
        var p = ParseOk(json);
        Assert.Equal("text", p.Payload.MessageType);
        Assert.Null(p.Payload.MediaFileName);
        Assert.Equal("hola necesito una cotizacion", p.Payload.Body);
    }
}
