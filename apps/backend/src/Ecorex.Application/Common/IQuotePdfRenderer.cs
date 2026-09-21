namespace Ecorex.Application.Common;

/// <summary>Genera un PDF o imagen a partir de una URL (la pagina publica de la cotizacion) usando un motor headless.</summary>
public interface IQuotePdfRenderer
{
    Task<byte[]> RenderUrlToPdfAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Genera una imagen PNG de pagina completa de la URL (para enviar la cotizacion como imagen).</summary>
    Task<byte[]> RenderUrlToImageAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Genera un PDF a partir de HTML CRUDO (sin navegar a una URL): util para producir el documento
    /// EN PROCESO (ej. adjuntar la cotizacion a un WhatsApp) sin depender del puerto/loopback del servidor.</summary>
    Task<byte[]> RenderHtmlToPdfAsync(string html, CancellationToken cancellationToken = default);
}
