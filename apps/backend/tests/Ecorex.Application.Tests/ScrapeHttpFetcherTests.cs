using System.Net;
using Ecorex.Application.Scraping;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del GET acotado (ADR-0025) con un HttpMessageHandler falso (sin red) y DNS
/// inyectado: redirecciones re-validadas contra el guard SSRF (el caso clasico es un
/// destino publico que redirige a la red interna), tope de saltos, tope de bytes y
/// veredictos tipados (el fetcher nunca lanza por fallas de red).
/// </summary>
public class ScrapeHttpFetcherTests
{
    private static readonly ScrapeGuardOptions Options = new() { MaxRedirects = 3, MaxResponseBytes = 1024 };

    /// <summary>Todas las pruebas resuelven cualquier hostname a una IP publica.</summary>
    private static readonly SsrfUrlGuard.DnsResolver PublicDns =
        (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> Requested { get; } = [];

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responder(request));
        }
    }

    private static ScrapeHttpFetcher Fetcher(FakeHandler handler, ScrapeGuardOptions? options = null) =>
        new(new HttpClient(handler), options ?? Options, PublicDns);

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task HappyPath_DevuelveElCuerpo_YMandaUserAgentPropio()
    {
        string? userAgent = null;
        var handler = new FakeHandler(req =>
        {
            userAgent = req.Headers.UserAgent.ToString();
            Assert.Equal(HttpMethod.Get, req.Method); // SOLO GET
            return Ok("{\"items\":[1,2,3]}");
        });

        var result = await Fetcher(handler).FetchAsync("https://example.com/api");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("{\"items\":[1,2,3]}", result.Body);
        Assert.Contains("EcorexScraper", userAgent);
    }

    [Fact]
    public async Task Redireccion_AUnDestinoPrivado_SeBloquea()
    {
        // El vector clasico: URL publica que responde 302 hacia la red interna / metadata.
        var handler = new FakeHandler(req =>
            req.RequestUri!.Host == "example.com"
                ? Redirect("http://169.254.169.254/latest/meta-data/")
                : Ok("nunca deberia llegar aqui"));

        var result = await Fetcher(handler).FetchAsync("https://example.com/promo");

        Assert.False(result.Ok);
        Assert.Contains("bloqueada", result.Error);
        // El fetcher NO hizo el request al destino privado.
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task Redireccion_APublico_SeSigue_YSeResuelveRelativa()
    {
        var handler = new FakeHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/vieja" => Redirect("https://example.com/nueva"),
            "/nueva" => new HttpResponseMessage(HttpStatusCode.SeeOther) { Headers = { Location = new Uri("/final", UriKind.Relative) } },
            "/final" => Ok("llegue"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var result = await Fetcher(handler).FetchAsync("https://example.com/vieja");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("llegue", result.Body);
        Assert.Equal(3, handler.Requested.Count);
    }

    [Fact]
    public async Task DemasiadasRedirecciones_Falla()
    {
        var n = 0;
        var handler = new FakeHandler(_ => Redirect($"https://example.com/salto-{++n}"));

        var result = await Fetcher(handler).FetchAsync("https://example.com/");

        Assert.False(result.Ok);
        Assert.Contains("redirecciones", result.Error);
    }

    [Fact]
    public async Task RespuestaMasGrandeQueElTope_Falla()
    {
        var handler = new FakeHandler(_ => Ok(new string('x', 2048))); // tope: 1024

        var result = await Fetcher(handler).FetchAsync("https://example.com/gigante");

        Assert.False(result.Ok);
        Assert.Contains("supera el maximo", result.Error);
    }

    [Fact]
    public async Task ContentLengthDeclaradoMayorAlTope_FallaSinDescargar()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Ok(new string('x', 10));
            response.Content.Headers.ContentLength = 50_000_000;
            return response;
        });

        var result = await Fetcher(handler).FetchAsync("https://example.com/mentiroso");

        Assert.False(result.Ok);
        Assert.Contains("supera el maximo", result.Error);
    }

    [Fact]
    public async Task ErroresHttp_SonVeredictoTipado()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await Fetcher(handler).FetchAsync("https://example.com/roto");

        Assert.False(result.Ok);
        Assert.Contains("500", result.Error);
    }

    [Fact]
    public async Task UrlInicialBloqueada_NoHaceNingunRequest()
    {
        var handler = new FakeHandler(_ => Ok("no"));

        var result = await Fetcher(handler).FetchAsync("http://10.1.2.3/interno");

        Assert.False(result.Ok);
        Assert.Empty(handler.Requested);
    }
}
