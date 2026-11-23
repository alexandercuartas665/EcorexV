using Ecorex.Application.DataContainers;

namespace Ecorex.Application.Tests;

/// <summary>ADR-0203: ver_conector nunca muestra secretos que viajen en el endpoint.</summary>
public sealed class ConnectorSecretMaskTests
{
    [Fact]
    public void Socrata_SinSecretos_QuedaIgual()
    {
        var url = "https://www.datos.gov.co/resource/p6dx-8zbt.json?$order=fecha_de_publicacion_del DESC&$limit=5000";
        var (masked, any) = ConnectorSecretMask.MaskEndpoint(url);
        Assert.Equal(url, masked);
        Assert.False(any);
    }

    [Theory]
    [InlineData("https://api.test/x?$$app_token=ABC123&$limit=10", "https://api.test/x?$$app_token=***&$limit=10")]
    [InlineData("https://api.test/x?api_key=zzz", "https://api.test/x?api_key=***")]
    [InlineData("https://api.test/x?access_token=t&client_secret=s&q=1", "https://api.test/x?access_token=***&client_secret=***&q=1")]
    [InlineData("https://api.test/x?sig=abc#frag", "https://api.test/x?sig=***#frag")]
    [InlineData("https://user:clave@api.test/x?q=1", "https://***@api.test/x?q=1")]
    public void ParametrosSensibles_YUserinfo_SeEnmascaran(string url, string expected)
    {
        var (masked, any) = ConnectorSecretMask.MaskEndpoint(url);
        Assert.Equal(expected, masked);
        Assert.True(any);
    }

    [Theory]
    [InlineData("https://api.test/x?keyword=hola&design=1&page=2")]
    [InlineData("https://api.test/buscar")]
    public void ParametrosNormales_NoSeTocan(string url)
    {
        var (masked, any) = ConnectorSecretMask.MaskEndpoint(url);
        Assert.Equal(url, masked);
        Assert.False(any);
    }

    [Fact]
    public void Vacio_DevuelveVacio()
    {
        Assert.Equal((null, false), ConnectorSecretMask.MaskEndpoint(null));
    }
}
