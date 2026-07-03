namespace Ecorex.Application.Admin;

public enum WompiWebhookResult
{
    Processed,
    Duplicate,
    InvalidSignature,
    NoMatchingPayment,
    Error
}

public interface IWompiWebhookService
{
    /// <summary>
    /// Procesa un evento crudo de Wompi de forma idempotente: valida la firma con el secret de
    /// eventos, descarta reenvios, registra el evento y aplica el cambio al pago/suscripcion.
    /// </summary>
    Task<WompiWebhookResult> ProcessAsync(string rawJson, CancellationToken cancellationToken = default);
}
