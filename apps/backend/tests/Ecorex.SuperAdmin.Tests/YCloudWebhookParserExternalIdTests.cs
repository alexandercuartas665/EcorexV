using System.Text.Json;
using Ecorex.SuperAdmin.RealTime;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// El ExternalId de un mensaje entrante de YCloud debe ser el WAMID de WhatsApp ("wamid....") y NO el id
/// interno de YCloud (24-hex). La reaccion (emoji) referencia el mensaje por su wamid; usar el id interno
/// hacia que WhatsApp respondiera 131009 "Invalid message_id". Fallback al id interno si no viene wamid.
/// </summary>
public class YCloudWebhookParserExternalIdTests
{
    private static YCloudParsedMessage ParseOne(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = YCloudWebhookParser.Parse(doc.RootElement);
        return Assert.Single(list);
    }

    [Fact]
    public void ExternalId_es_el_wamid_cuando_viene()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "6aa028192ec95856314f368d",
            "wamid": "wamid.HBgMNTczMDA5OTk4ODc3FQIAEhgU",
            "to": "573001112233",
            "from": "573009998877",
            "type": "text",
            "text": { "body": "hola" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.Equal("wamid.HBgMNTczMDA5OTk4ODc3FQIAEhgU", m.ExternalId);
    }

    [Fact]
    public void ExternalId_cae_al_id_interno_si_no_hay_wamid()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "id": "6aa028192ec95856314f368d",
            "to": "573001112233",
            "from": "573009998877",
            "type": "text",
            "text": { "body": "hola" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.Equal("6aa028192ec95856314f368d", m.ExternalId);
    }

    [Fact]
    public void ExternalId_genera_guid_si_faltan_ambos()
    {
        const string json = """
        {
          "type": "whatsapp.inbound_message.received",
          "whatsappInboundMessage": {
            "to": "573001112233",
            "from": "573009998877",
            "type": "text",
            "text": { "body": "hola" }
          }
        }
        """;
        var m = ParseOne(json);
        Assert.False(string.IsNullOrWhiteSpace(m.ExternalId));
        Assert.Matches("^[0-9a-f]{32}$", m.ExternalId);
    }
}
