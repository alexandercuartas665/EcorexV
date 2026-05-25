namespace CubotTravels.Application.Common;

/// <summary>Genera un PDF a partir de una URL (la pagina publica de la cotizacion) usando un motor headless.</summary>
public interface IQuotePdfRenderer
{
    Task<byte[]> RenderUrlToPdfAsync(string url, CancellationToken cancellationToken = default);
}
