namespace Ecorex.Application.Tenancy;

/// <summary>Resultado de validar credenciales de un numero Cloud (GET /{phone_number_id}).</summary>
public sealed record CloudCheckResult(bool Ok, string? DisplayPhoneNumber, string? VerifiedName, string? Error);

/// <summary>Resultado de enviar un mensaje por la Cloud API.</summary>
public sealed record CloudSendResult(bool Ok, string? MessageId, string? Error);

/// <summary>
/// Cliente de la API oficial de WhatsApp Cloud de Meta (graph.facebook.com). A diferencia de Evolution,
/// no hay instancia ni QR: cada numero se identifica por phone_number_id + token de acceso. El webhook
/// entrante es a nivel de App de Meta (se configura en el panel), no por numero via API.
/// </summary>
public interface IWhatsAppCloudClient
{
    /// <summary>Valida el token y el numero (GET /{phone_number_id}). Usado al "conectar" una linea Cloud.</summary>
    Task<CloudCheckResult> CheckAsync(string phoneNumberId, string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Envia un mensaje de texto (POST /{phone_number_id}/messages, type=text).</summary>
    Task<CloudSendResult> SendTextAsync(string phoneNumberId, string accessToken, string toPhone, string text, CancellationToken cancellationToken = default);

    /// <summary>Sube el archivo (POST /{phone_number_id}/media) y envia el mensaje por media id. mediaType: image|video|document|audio.</summary>
    Task<CloudSendResult> SendMediaAsync(string phoneNumberId, string accessToken, string toPhone, string mediaType, byte[] bytes, string? mimeType, string? fileName, string? caption, CancellationToken cancellationToken = default);

    /// <summary>Envia una ubicacion (POST /{phone_number_id}/messages, type=location).</summary>
    Task<CloudSendResult> SendLocationAsync(string phoneNumberId, string accessToken, string toPhone, double latitude, double longitude, string? name, string? address, CancellationToken cancellationToken = default);
}
