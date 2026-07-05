using System.Net;
using Ecorex.Application.Scraping;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del validador anti-SSRF (ADR-0025). Es la pieza de seguridad INNEGOCIABLE del
/// modulo de extraccion: cualquier caso que se afloje aqui es una via del tenant a la red
/// interna del servidor. DNS inyectado (determinista, sin red).
/// </summary>
public class SsrfUrlGuardTests
{
    private static readonly ScrapeGuardOptions Default = new();
    private static readonly ScrapeGuardOptions DevLoopback = new() { AllowLoopback = true };

    /// <summary>Resolutor que responde las IPs dadas para cualquier host.</summary>
    private static SsrfUrlGuard.DnsResolver Resolves(params string[] ips) =>
        (_, _) => Task.FromResult(ips.Select(IPAddress.Parse).ToArray());

    // ---- Esquemas ----

    [Theory]
    [InlineData("ftp://example.com/archivo.csv")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hola")]
    public async Task Rechaza_EsquemasNoHttp(string url)
    {
        var result = await SsrfUrlGuard.ValidateAsync(url, Default, Resolves("93.184.216.34"));
        Assert.False(result.Allowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-una-url")]
    [InlineData("/ruta/relativa")]
    public async Task Rechaza_UrlsInvalidasORelativas(string url)
    {
        var result = await SsrfUrlGuard.ValidateAsync(url, Default, Resolves("93.184.216.34"));
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Rechaza_CredencialesEmbebidas()
    {
        var result = await SsrfUrlGuard.ValidateAsync(
            "https://admin:secreto@example.com/", Default, Resolves("93.184.216.34"));
        Assert.False(result.Allowed);
        Assert.Contains("credenciales", result.Reason);
    }

    // ---- IPs literales privadas / reservadas (IPv4) ----

    [Theory]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://10.255.255.254/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://172.31.255.254/")]
    [InlineData("http://192.168.0.1/")]
    [InlineData("http://192.168.255.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // metadata cloud
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.8.8.8/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://100.64.0.1/")]   // CGNAT
    [InlineData("http://100.127.1.1/")]  // CGNAT borde alto
    [InlineData("http://192.0.2.10/")]   // TEST-NET-1
    [InlineData("http://198.18.0.1/")]   // benchmarking
    [InlineData("http://224.0.0.1/")]    // multicast
    [InlineData("http://255.255.255.255/")]
    public async Task Rechaza_IPv4PrivadasYReservadas(string url)
    {
        var result = await SsrfUrlGuard.ValidateAsync(url, Default);
        Assert.False(result.Allowed);
    }

    // ---- IPv6 ----

    [Theory]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fd12:3456::1]/")]
    [InlineData("http://[ff02::1]/")]
    [InlineData("http://[::]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]    // IPv4-mapeada privada
    [InlineData("http://[::ffff:127.0.0.1]/")]   // IPv4-mapeada loopback
    public async Task Rechaza_IPv6PrivadasYMapeadas(string url)
    {
        var result = await SsrfUrlGuard.ValidateAsync(url, Default);
        Assert.False(result.Allowed);
    }

    // ---- Resolucion DNS: el hostname NO se toma por su cara bonita ----

    [Fact]
    public async Task Rechaza_HostnameQueResuelveAPrivada()
    {
        var result = await SsrfUrlGuard.ValidateAsync(
            "http://interno.atacante.com/", Default, Resolves("10.0.0.8"));
        Assert.False(result.Allowed);
        Assert.Contains("privada o reservada", result.Reason);
    }

    [Fact]
    public async Task Rechaza_HostnameConMezclaPublicaYPrivada()
    {
        // Registro A doble (publica + privada): fail-closed, BASTA una privada para bloquear.
        var result = await SsrfUrlGuard.ValidateAsync(
            "http://dual.atacante.com/", Default, Resolves("93.184.216.34", "192.168.1.10"));
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Rechaza_HostnameQueNoResuelve()
    {
        SsrfUrlGuard.DnsResolver noResuelve = (_, _) => Task.FromResult(Array.Empty<IPAddress>());
        var result = await SsrfUrlGuard.ValidateAsync("http://fantasma.example/", Default, noResuelve);
        Assert.False(result.Allowed);
    }

    // ---- Puertos ----

    [Theory]
    [InlineData("http://example.com:8080/")]
    [InlineData("http://example.com:22/")]
    [InlineData("http://example.com:6379/")]
    [InlineData("https://example.com:8443/")]
    public async Task Rechaza_PuertosNoEstandarEnHostsPublicos(string url)
    {
        var result = await SsrfUrlGuard.ValidateAsync(url, Default, Resolves("93.184.216.34"));
        Assert.False(result.Allowed);
        Assert.Contains("Puerto", result.Reason);
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("http://example.com:80/datos")]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com:443/api/items?page=1")]
    public async Task Permite_HostsPublicosEnPuertosPorDefecto(string url)
    {
        var result = await SsrfUrlGuard.ValidateAsync(url, Default, Resolves("93.184.216.34"));
        Assert.True(result.Allowed, result.Reason);
        Assert.False(result.IsLoopback);
    }

    // ---- Excepcion loopback (SOLO Development, endpoint demo propio) ----

    [Theory]
    [InlineData("http://localhost:5253/api/demo/scrape-sample")]
    [InlineData("http://127.0.0.1:5299/api/demo/scrape-sample")]
    public async Task Permite_LoopbackConPuertoAlto_SoloConAllowLoopback(string url)
    {
        var dev = await SsrfUrlGuard.ValidateAsync(url, DevLoopback, Resolves("127.0.0.1"));
        Assert.True(dev.Allowed, dev.Reason);
        Assert.True(dev.IsLoopback);

        var prod = await SsrfUrlGuard.ValidateAsync(url, Default, Resolves("127.0.0.1"));
        Assert.False(prod.Allowed);
    }

    [Fact]
    public async Task AllowLoopback_NoAbrePuertaAPrivadasNoLoopback()
    {
        // La excepcion es SOLO loopback: la red privada sigue bloqueada aun en Development.
        var result = await SsrfUrlGuard.ValidateAsync("http://192.168.1.10/", DevLoopback);
        Assert.False(result.Allowed);
    }

    // ---- Clasificador de direcciones (defensa en profundidad, casos borde) ----

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.15.255.255", false)]  // justo ANTES de 172.16/12
    [InlineData("172.16.0.0", true)]
    [InlineData("172.32.0.0", false)]      // justo DESPUES de 172.16/12
    [InlineData("100.63.255.255", false)]  // antes de CGNAT
    [InlineData("100.128.0.0", false)]     // despues de CGNAT
    [InlineData("8.8.8.8", false)]
    [InlineData("93.184.216.34", false)]
    [InlineData("240.0.0.1", true)]        // reservada clase E
    public void IsBlockedAddress_ClasificaLosBordesDeRango(string ip, bool blocked)
    {
        Assert.Equal(blocked, SsrfUrlGuard.IsBlockedAddress(IPAddress.Parse(ip)));
    }
}
