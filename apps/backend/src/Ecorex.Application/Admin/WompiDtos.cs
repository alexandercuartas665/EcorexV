using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

/// <summary>Vista de la configuracion Wompi para la consola. Las llaves sensibles van enmascaradas.</summary>
public sealed record WompiConfigDto(
    WompiEnvironment Environment,
    string? PublicKey,
    string? PrivateKeyMasked,
    string? EventsSecretMasked,
    string? IntegritySecretMasked,
    string? WebhookEndpoint,
    string Currency,
    int MaxRetries,
    WompiIntegrationStatus Status,
    DateTimeOffset? LastValidatedAt,
    bool HasPrivateKey,
    bool HasEventsSecret,
    bool HasIntegritySecret);

/// <summary>
/// Alta/edicion de la config Wompi. PrivateKey y EventsSecret son opcionales: si vienen vacios
/// se conserva el valor cifrado actual (no se re-cifra ni se borra).
/// </summary>
public sealed record SaveWompiConfigRequest(
    WompiEnvironment Environment,
    string? PublicKey,
    string? PrivateKey,
    string? EventsSecret,
    string? IntegritySecret,
    string? WebhookEndpoint,
    string Currency,
    int MaxRetries);

public sealed record WompiValidationResult(bool Ok, string Message);

/// <summary>Resultado de generar un checkout: la URL de pago de Wompi y la referencia creada.</summary>
public sealed record WompiCheckoutResult(bool Ok, string? CheckoutUrl, string? Reference, string? Error);
