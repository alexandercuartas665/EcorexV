using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

/// <summary>
/// Armado de los parametros de BOTONES URL DINAMICOS al enviar una plantilla HSM: casar cada boton URL con
/// variable (por el texto del boton == etiqueta de la regla) con su enlace de decision, y calcular el sufijo
/// (URL del enlace menos el prefijo fijo del boton). Los botones fijos NO llevan parametro.
/// </summary>
public class WhatsAppButtonComposerTests
{
    private const string TwoDynamicButtons = """
        [
          { "type": "URL", "text": "1. Aprobar Cotización", "url": "https://app2.bitcode.com.co/{{button1}}", "hasUrlVariable": true },
          { "type": "URL", "text": "2. Rechazar Cotización", "url": "https://app2.bitcode.com.co/{{button2}}", "hasUrlVariable": true }
        ]
        """;

    [Fact]
    public void CasaPorTexto_YCalculaElSufijo()
    {
        var enlaces = new Dictionary<string, string>
        {
            ["1. Aprobar Cotización"] = "https://app2.bitcode.com.co/d/tok-aprobar",
            ["2. Rechazar Cotización"] = "https://app2.bitcode.com.co/d/tok-rechazar"
        };

        var res = WhatsAppButtonComposer.BuildUrlButtonParams(TwoDynamicButtons, enlaces);

        Assert.NotNull(res);
        Assert.Equal(2, res!.Count);
        // Indice = posicion del boton; sufijo = enlace menos el prefijo fijo ("https://app2.bitcode.com.co/").
        Assert.Equal(0, res[0].Index);
        Assert.Equal("d/tok-aprobar", res[0].Text);
        Assert.Equal(1, res[1].Index);
        Assert.Equal("d/tok-rechazar", res[1].Text);
    }

    [Fact]
    public void BotonSinEnlaceNoProduceParametro()
    {
        var enlaces = new Dictionary<string, string>
        {
            ["1. Aprobar Cotización"] = "https://app2.bitcode.com.co/d/tok-aprobar"
        };

        var res = WhatsAppButtonComposer.BuildUrlButtonParams(TwoDynamicButtons, enlaces);

        Assert.NotNull(res);
        Assert.Single(res!);
        Assert.Equal(0, res![0].Index);
        Assert.Equal("d/tok-aprobar", res[0].Text);
    }

    [Fact]
    public void BotonesFijos_NoProducenParametros()
    {
        var fijos = """
            [
              { "type": "URL", "text": "Aprobar", "url": "https://app2.bitcode.com.co/", "hasUrlVariable": false },
              { "type": "QUICK_REPLY", "text": "Rechazar" }
            ]
            """;
        var enlaces = new Dictionary<string, string> { ["Aprobar"] = "https://app2.bitcode.com.co/d/x" };

        Assert.Null(WhatsAppButtonComposer.BuildUrlButtonParams(fijos, enlaces));
    }

    [Fact]
    public void IndiceRespetaLaPosicion_ConBotonFijoAntes()
    {
        // Boton 0 fijo (quick reply) + boton 1 dinamico: el parametro debe ir al indice 1.
        var mixto = """
            [
              { "type": "QUICK_REPLY", "text": "Menu" },
              { "type": "URL", "text": "Aprobar", "url": "https://app2.bitcode.com.co/d/{{1}}", "hasUrlVariable": true }
            ]
            """;
        var enlaces = new Dictionary<string, string> { ["Aprobar"] = "https://app2.bitcode.com.co/d/tok" };

        var res = WhatsAppButtonComposer.BuildUrlButtonParams(mixto, enlaces);

        Assert.NotNull(res);
        Assert.Single(res!);
        Assert.Equal(1, res![0].Index);     // posicion real del boton dinamico
        Assert.Equal("tok", res[0].Text);   // prefijo "https://app2.bitcode.com.co/d/"
    }

    [Fact]
    public void SinBotonesOSinEnlaces_DevuelveNull()
    {
        var enlaces = new Dictionary<string, string> { ["Aprobar"] = "https://x/d/tok" };
        Assert.Null(WhatsAppButtonComposer.BuildUrlButtonParams(null, enlaces));
        Assert.Null(WhatsAppButtonComposer.BuildUrlButtonParams(TwoDynamicButtons, null));
        Assert.Null(WhatsAppButtonComposer.BuildUrlButtonParams(TwoDynamicButtons, new Dictionary<string, string>()));
    }

    [Fact]
    public void TestButtonParams_RellenaCadaBotonDinamicoConElPlaceholder()
    {
        var res = WhatsAppButtonComposer.BuildTestButtonParams(TwoDynamicButtons, "prueba");

        Assert.NotNull(res);
        Assert.Equal(2, res!.Count);
        Assert.Equal(0, res[0].Index);
        Assert.Equal("prueba", res[0].Text);
        Assert.Equal(1, res[1].Index);
        Assert.Equal("prueba", res[1].Text);
    }

    [Fact]
    public void TestButtonParams_SinBotonesDinamicos_DevuelveNull()
    {
        var fijos = """[ { "type": "URL", "text": "Sitio", "url": "https://bitcode.com.co" } ]""";
        Assert.Null(WhatsAppButtonComposer.BuildTestButtonParams(fijos, "prueba"));
        Assert.Null(WhatsAppButtonComposer.BuildTestButtonParams(null, "prueba"));
    }
}
