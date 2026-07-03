namespace Ecorex.Application.Common;

/// <summary>Datos para renderizar un comprobante de pago (recibo). No es factura electronica DIAN.</summary>
public sealed record ReceiptData(
    string ReceiptNumber,
    DateTimeOffset IssuedAt,
    string TenantName,
    string? LegalName,
    string? TaxId,
    string? Country,
    string PlanName,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    decimal Amount,
    string Currency,
    string StatusLabel,
    string Provider,
    string? ProviderReference);

/// <summary>Genera el PDF de un comprobante de pago. Implementacion en Infrastructure (QuestPDF).</summary>
public interface IReceiptPdfRenderer
{
    byte[] Render(ReceiptData data);
}
