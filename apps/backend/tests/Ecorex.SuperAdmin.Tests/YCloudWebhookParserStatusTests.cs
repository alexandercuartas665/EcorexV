using System.Text.Json;
using Ecorex.SuperAdmin.RealTime;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// El parser de YCloud debe extraer los ESTADOS de entrega de mensajes SALIENTES (evento
/// whatsapp.message.updated). Antes se ignoraban por completo, asi que un "no llego" (failed/undelivered)
/// quedaba invisible; con esto el endpoint puede dejar una nota con el motivo de Meta. Basado en el shape real.
/// </summary>
public class YCloudWebhookParserStatusTests
{
    private static YCloudStatusUpdate ParseOne(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = YCloudWebhookParser.ParseStatuses(doc.RootElement);
        return Assert.Single(list);
    }

    [Fact]
    public void Estado_failed_trae_negocio_destinatario_y_error()
    {
        const string json = """
        {
          "type": "whatsapp.message.updated",
          "whatsappMessage": {
            "id": "abc123",
            "wamid": "wamid.OUT1",
            "wabaId": "1529785452178589",
            "from": "573171990738",
            "to": "573216726882",
            "status": "failed",
            "error": { "code": 131053, "message": "Media download error" },
            "updateTime": "2026-10-01T21:35:20Z"
          }
        }
        """;
        var s = ParseOne(json);
        Assert.True(s.IsFailure);
        Assert.Equal("573171990738", s.BusinessNumber);
        Assert.Equal("573216726882", s.RecipientPhone);
        Assert.Equal("failed", s.Status);
        Assert.Equal("131053", s.ErrorCode);
        Assert.Equal("Media download error", s.ErrorMessage);
        Assert.Equal("1529785452178589", s.WabaId);
    }

    [Fact]
    public void Estado_undelivered_cuenta_como_fallo()
    {
        const string json = """
        { "whatsappMessage": { "from": "573171990738", "to": "573216726882", "status": "undelivered" } }
        """;
        var s = ParseOne(json);
        Assert.True(s.IsFailure);
    }

    [Fact]
    public void Estado_delivered_no_es_fallo()
    {
        const string json = """
        { "whatsappMessage": { "from": "573171990738", "to": "573216726882", "status": "delivered" } }
        """;
        var s = ParseOne(json);
        Assert.False(s.IsFailure);
        Assert.Equal("delivered", s.Status);
    }

    [Fact]
    public void Error_en_arreglo_errors_tambien_se_lee()
    {
        const string json = """
        {
          "whatsappMessage": {
            "from": "573171990738", "to": "573216726882", "status": "failed",
            "errors": [ { "code": 131026, "title": "Message undeliverable" } ]
          }
        }
        """;
        var s = ParseOne(json);
        Assert.Equal("131026", s.ErrorCode);
        Assert.Equal("Message undeliverable", s.ErrorMessage);
    }

    [Fact]
    public void Un_mensaje_entrante_no_produce_estados()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "wamid.IN1", "to": "573171990738", "from": "573216726882",
            "type": "text", "text": { "body": "hola" }
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var list = YCloudWebhookParser.ParseStatuses(doc.RootElement);
        Assert.Empty(list);
    }
}
