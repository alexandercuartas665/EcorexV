namespace Ecorex.Domain.Enums;

/// <summary>Resultado del procesamiento de un evento de webhook de Wompi (Super Admin SaaS sec.8/15.4).</summary>
public enum WebhookProcessingStatus
{
    Received,
    Processed,
    NoMatchingPayment,
    InvalidSignature,
    Duplicate,
    Error
}
