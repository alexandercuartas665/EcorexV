namespace Ecorex.Application.Pdf;

/// <summary>
/// Separa un PDF en varios PDFs nuevos a partir de LISTAS de paginas (1-based). Cada lista produce un PDF con
/// esas paginas, en ese orden. Se usa con el plan de corte que devuelve el agente (un paquete por nota de pago).
/// La implementacion vive en Infraestructura (PdfSharp: importa paginas, no rerenderiza).
/// </summary>
public interface IPdfSplitter
{
    /// <summary>
    /// Devuelve un PDF (bytes) por cada grupo de paginas de <paramref name="gruposDePaginas"/>, en el mismo orden.
    /// Los numeros de pagina son 1-based y deben existir en el documento origen.
    /// </summary>
    IReadOnlyList<byte[]> Separar(byte[] pdf, IReadOnlyList<IReadOnlyList<int>> gruposDePaginas);
}
