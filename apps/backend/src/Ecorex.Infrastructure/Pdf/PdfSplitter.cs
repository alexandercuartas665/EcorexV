using Ecorex.Application.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Ecorex.Infrastructure.Pdf;

/// <summary>
/// Separa un PDF en varios usando PdfSharp: abre el origen en modo Import y copia las paginas indicadas a un
/// documento nuevo por cada grupo. No rerenderiza (conserva el contenido tal cual); no necesita fuentes.
/// </summary>
public sealed class PdfSplitter : IPdfSplitter
{
    public IReadOnlyList<byte[]> Separar(byte[] pdf, IReadOnlyList<IReadOnlyList<int>> gruposDePaginas)
    {
        var result = new List<byte[]>(gruposDePaginas.Count);
        using var inMs = new MemoryStream(pdf, writable: false);
        using var src = PdfReader.Open(inMs, PdfDocumentOpenMode.Import);
        var total = src.PageCount;

        foreach (var grupo in gruposDePaginas)
        {
            using var outDoc = new PdfDocument();
            foreach (var p in grupo)
            {
                if (p < 1 || p > total) { continue; }
                outDoc.AddPage(src.Pages[p - 1]);
            }
            using var outMs = new MemoryStream();
            outDoc.Save(outMs, closeStream: false);
            result.Add(outMs.ToArray());
        }
        return result;
    }
}
