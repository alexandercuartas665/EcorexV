using Ecorex.Application.Pdf;
using PDFtoImage;

namespace Ecorex.Infrastructure.Pdf;

/// <summary>
/// Rasterizador de PDF con PDFium (via PDFtoImage): cada pagina sale como JPEG para la IA de vision. El binario
/// de PDFium y SkiaSharp viene con el paquete; en Linux (prod) requiere el native asset de SkiaSharp (ver
/// PROGRESO.md). NOTA de rendimiento: hoy se renderiza pagina a pagina; para lotes muy grandes conviene cargar
/// el documento una sola vez (ToImages) en una mejora futura.
/// </summary>
public sealed class PdfRasterizer : IPdfRasterizer
{
    public int ContarPaginas(byte[] pdf) => Conversion.GetPageCount(pdf);

    public IReadOnlyList<RasterPage> Rasterizar(byte[] pdf, int dpi = 150, int? maxPaginas = null, CancellationToken ct = default)
    {
        var total = Conversion.GetPageCount(pdf);
        var n = maxPaginas is int m && m < total ? m : total;
        var opts = new RenderOptions(Dpi: dpi, WithAspectRatio: true);

        var pages = new List<RasterPage>(n);
        for (var i = 0; i < n; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var outMs = new MemoryStream();
            Conversion.SaveJpeg(outMs, pdf, new Index(i), options: opts);
            pages.Add(new RasterPage(i + 1, outMs.ToArray(), "image/jpeg"));
        }
        return pages;
    }
}
