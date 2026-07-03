namespace Ecorex.Application.Admin;

/// <summary>Archivo del comprobante listo para descargar. TenantId se expone para validar propiedad.</summary>
public sealed record PaymentReceiptFile(byte[] Content, string FileName, Guid TenantId);

public interface IPaymentReceiptService
{
    /// <summary>
    /// Genera el comprobante (PDF) de un pago APROBADO. Devuelve null si el pago no existe o no esta aprobado.
    /// </summary>
    Task<PaymentReceiptFile?> GenerateAsync(Guid paymentId, CancellationToken cancellationToken = default);
}
