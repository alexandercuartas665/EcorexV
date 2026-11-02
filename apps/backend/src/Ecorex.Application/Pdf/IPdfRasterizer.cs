namespace Ecorex.Application.Pdf;

/// <summary>Una pagina rasterizada: su numero (1-based) y la imagen JPEG lista para la IA de vision.</summary>
public sealed record RasterPage(int PageNumber, byte[] Image, string Mime);

/// <summary>
/// Convierte un PDF en imagenes, una por pagina, para enviarlas a un modelo de IA de VISION (DeepSeek/otros
/// no aceptan PDF directo). La implementacion vive en Infraestructura (usa PDFium via PDFtoImage).
/// </summary>
public interface IPdfRasterizer
{
    /// <summary>Numero de paginas del PDF.</summary>
    int ContarPaginas(byte[] pdf);

    /// <summary>
    /// Rasteriza cada pagina a JPEG al <paramref name="dpi"/> dado. Si <paramref name="maxPaginas"/> viene,
    /// corta ahi (tope de seguridad para lotes enormes). Las paginas salen en orden, 1-based.
    /// </summary>
    IReadOnlyList<RasterPage> Rasterizar(byte[] pdf, int dpi = 150, int? maxPaginas = null, CancellationToken ct = default);
}
