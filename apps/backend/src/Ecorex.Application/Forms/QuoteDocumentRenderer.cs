using Ecorex.Application.Common;

namespace Ecorex.Application.Forms;

/// <summary>Documento renderizado listo para adjuntar (ej. la cotizacion por WhatsApp): bytes + nombre + tipo.</summary>
public sealed record QuoteDocument(byte[] Bytes, string FileName, string MimeType);

/// <summary>
/// Renderiza a PDF (en PROCESO, sin depender del loopback del servidor) una respuesta de formulario con la
/// plantilla de impresion del tenant. Reusa <see cref="IFormTemplateRenderService"/> (HTML + nombre amigable)
/// y <see cref="IQuotePdfRenderer.RenderHtmlToPdfAsync"/> (HTML -> PDF con Chromium headless). Pensado para
/// adjuntar la cotizacion de una tarea a un mensaje (Fase 2 de plantillas por Evolution).
/// </summary>
public interface IQuoteDocumentRenderer
{
    /// <summary>PDF de la respuesta indicada (con su plantilla, o la predeterminada del tenant). Null si la
    /// respuesta no existe o no hay plantilla utilizable.</summary>
    Task<QuoteDocument?> RenderResponsePdfAsync(Guid responseId, Guid? templateId = null, CancellationToken cancellationToken = default);
}

public sealed class QuoteDocumentRenderer : IQuoteDocumentRenderer
{
    private readonly IFormTemplateRenderService _templates;
    private readonly IQuotePdfRenderer _pdf;

    public QuoteDocumentRenderer(IFormTemplateRenderService templates, IQuotePdfRenderer pdf)
    {
        _templates = templates;
        _pdf = pdf;
    }

    public async Task<QuoteDocument?> RenderResponsePdfAsync(Guid responseId, Guid? templateId = null, CancellationToken cancellationToken = default)
    {
        var html = await _templates.RenderHtmlAsync(responseId, templateId, cancellationToken);
        if (string.IsNullOrWhiteSpace(html)) { return null; }

        var bytes = await _pdf.RenderHtmlToPdfAsync(html, cancellationToken);
        if (bytes is null || bytes.Length == 0) { return null; }

        // Nombre amigable "NombreFormulario CodigoActividad" (mismo que la impresion); fallback generico.
        var baseName = await _templates.GetDocumentNameAsync(responseId, cancellationToken);
        var fileName = (string.IsNullOrWhiteSpace(baseName) ? "documento" : baseName) + ".pdf";
        return new QuoteDocument(bytes, fileName, "application/pdf");
    }
}
