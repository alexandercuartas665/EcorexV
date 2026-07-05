using System.Net;

namespace Ecorex.Application.Scraping;

/// <summary>
/// Implementacion del GET acotado (ADR-0025). El HttpClient inyectado DEBE tener
/// AllowAutoRedirect=false (lo configura el registro AddHttpClient en Infrastructure):
/// las redirecciones se siguen a mano aqui para re-validar cada salto contra el guard
/// SSRF (un destino publico puede redirigir a http://169.254.169.254/ o a la red interna).
/// </summary>
public sealed class ScrapeHttpFetcher : IScrapeFetcher
{
    private readonly HttpClient _http;
    private readonly ScrapeGuardOptions _options;
    private readonly SsrfUrlGuard.DnsResolver? _resolver;

    /// <param name="resolver">Solo para tests: resolutor DNS determinista.</param>
    public ScrapeHttpFetcher(HttpClient http, ScrapeGuardOptions options, SsrfUrlGuard.DnsResolver? resolver = null)
    {
        _http = http;
        _options = options;
        _resolver = resolver;
    }

    public async Task<ScrapeFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        // Timeout TOTAL de la corrida (todas las redirecciones + descarga), no por request.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.RequestTimeout);

        try
        {
            var currentUrl = url;
            for (var hop = 0; hop <= _options.MaxRedirects; hop++)
            {
                var check = await SsrfUrlGuard.ValidateAsync(currentUrl, _options, _resolver, cts.Token);
                if (!check.Allowed)
                {
                    return ScrapeFetchResult.Fail($"URL bloqueada: {check.Reason}");
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, check.Uri);
                request.Headers.UserAgent.ParseAdd(_options.UserAgent);
                request.Headers.Accept.ParseAdd("application/json, text/html;q=0.9, */*;q=0.5");

                using var response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return ScrapeFetchResult.Fail($"Redireccion HTTP {(int)response.StatusCode} sin encabezado Location.");
                    }
                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(check.Uri!, location).ToString();
                    continue; // el proximo ciclo re-valida el destino contra el guard
                }

                if (!response.IsSuccessStatusCode)
                {
                    return ScrapeFetchResult.Fail($"El servidor respondio HTTP {(int)response.StatusCode}.");
                }

                if (response.Content.Headers.ContentLength is long len && len > _options.MaxResponseBytes)
                {
                    return ScrapeFetchResult.Fail(
                        $"La respuesta ({len / 1024} KB) supera el maximo de {_options.MaxResponseBytes / 1024} KB.");
                }

                return await ReadBoundedAsync(response, cts.Token);
            }

            return ScrapeFetchResult.Fail($"Demasiadas redirecciones (maximo {_options.MaxRedirects}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ScrapeFetchResult.Fail($"Timeout de {_options.RequestTimeout.TotalSeconds:0}s agotado.");
        }
        catch (HttpRequestException ex)
        {
            return ScrapeFetchResult.Fail($"Error de red: {ex.Message}");
        }
    }

    /// <summary>Lee el cuerpo con tope duro de bytes aunque no venga Content-Length (chunked).</summary>
    private async Task<ScrapeFetchResult> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > _options.MaxResponseBytes)
            {
                return ScrapeFetchResult.Fail(
                    $"La respuesta supera el maximo de {_options.MaxResponseBytes / 1024} KB.");
            }
            buffer.Write(chunk, 0, read);
        }

        var charset = response.Content.Headers.ContentType?.CharSet;
        var encoding = System.Text.Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = System.Text.Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // charset desconocido: UTF-8 como fallback razonable.
            }
        }

        return ScrapeFetchResult.Success(encoding.GetString(buffer.ToArray()));
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
            or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
