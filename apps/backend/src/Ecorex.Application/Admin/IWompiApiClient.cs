namespace Ecorex.Application.Admin;

public sealed record WompiAcceptance(bool Ok, string? AcceptanceToken, string? Error);
public sealed record WompiPaymentSourceResult(bool Ok, long? Id, string? Label, string? Error);
public sealed record WompiChargeResult(bool Ok, string? TransactionId, string? Status, string? Error);

/// <summary>
/// Cliente de la API de Wompi (sandbox/produccion segun la config maestra). Se usa para el
/// debito automatico: tokenizacion de la tarjeta ocurre en el navegador; aqui se crea la
/// fuente de pago (servidor, con llave privada) y se cobra en cada corte.
/// </summary>
public interface IWompiApiClient
{
    /// <summary>Token de aceptacion del comercio, requerido para crear fuentes de pago/transacciones.</summary>
    Task<WompiAcceptance> GetAcceptanceTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Crea una fuente de pago tipo tarjeta a partir de un token de tarjeta (tok_...).</summary>
    Task<WompiPaymentSourceResult> CreateCardPaymentSourceAsync(string cardToken, string customerEmail, string acceptanceToken, CancellationToken cancellationToken = default);

    /// <summary>Cobra contra una fuente de pago guardada (debito recurrente, servidor a servidor).</summary>
    Task<WompiChargeResult> ChargePaymentSourceAsync(long paymentSourceId, long amountInCents, string currency, string reference, string customerEmail, CancellationToken cancellationToken = default);
}
