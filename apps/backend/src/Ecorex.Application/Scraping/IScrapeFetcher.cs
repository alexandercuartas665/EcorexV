namespace Ecorex.Application.Scraping;

/// <summary>Resultado del GET acotado del ejecutor (nunca lanza por fallo de red: veredicto tipado).</summary>
/// <param name="Ok">true si hubo 2xx dentro de los limites.</param>
/// <param name="Body">Cuerpo de la respuesta (solo Ok).</param>
/// <param name="Error">Motivo del fallo (solo !Ok). Sin datos sensibles.</param>
public sealed record ScrapeFetchResult(bool Ok, string? Body, string? Error)
{
    public static ScrapeFetchResult Success(string body) => new(true, body, null);
    public static ScrapeFetchResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// GET acotado y seguro para el modulo de extraccion (ADR-0025): SOLO GET, User-Agent propio,
/// timeout total, tope de bytes y guard SSRF re-aplicado en CADA redireccion.
/// </summary>
public interface IScrapeFetcher
{
    Task<ScrapeFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}
