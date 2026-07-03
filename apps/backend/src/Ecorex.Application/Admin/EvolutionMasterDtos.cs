using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

public sealed record EvolutionMasterDto(
    string? BaseUrl,
    string? ApiKeyMasked,
    bool HasApiKey,
    EvolutionIntegrationStatus Status,
    DateTimeOffset? LastValidatedAt);

public sealed record SaveEvolutionMasterRequest(string? BaseUrl, string? ApiKey);

public sealed record EvolutionValidationResult(bool Ok, string Message);

/// <summary>Resultado de comprobar la conexion contra un servidor Evolution API.</summary>
public sealed record EvolutionPingResult(bool Reachable, bool Authenticated, int? StatusCode, string? Detail);

/// <summary>Resultado de operaciones sobre una instancia (crear/conectar). QrBase64 es el codigo QR a escanear.</summary>
public sealed record EvolutionInstanceResult(bool Ok, string? QrBase64, string? State, string? PhoneNumber, string? Error);

public sealed record EvolutionSendResult(bool Ok, string? Error, string? MessageId = null);

/// <summary>Resultado de descargar la media ENTRANTE de un mensaje (base64 + mime).</summary>
public sealed record EvolutionMediaResult(bool Ok, string? Base64, string? Mime, string? Error);

/// <summary>Cliente HTTP del servidor Evolution API. Implementacion en Infrastructure.</summary>
public interface IEvolutionApiClient
{
    /// <summary>Comprueba que el servidor responde y que la API key es valida (GET /instance/fetchInstances).</summary>
    Task<EvolutionPingResult> CheckAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>Crea la instancia y devuelve el QR para escanear (POST /instance/create).</summary>
    Task<EvolutionInstanceResult> CreateInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);

    /// <summary>Solicita (re)conectar y devuelve un QR fresco (GET /instance/connect/{instance}).</summary>
    Task<EvolutionInstanceResult> ConnectAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);

    /// <summary>Estado de conexion: "open" | "connecting" | "close" (GET /instance/connectionState/{instance}). Null si error.</summary>
    Task<EvolutionInstanceResult> GetStateAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);

    /// <summary>Cierra sesion y elimina la instancia (DELETE /instance/delete/{instance}).</summary>
    Task<bool> DeleteInstanceAsync(string baseUrl, string apiKey, string instanceName, CancellationToken cancellationToken = default);

    /// <summary>Envia un mensaje de texto (POST /message/sendText/{instance}).</summary>
    Task<EvolutionSendResult> SendTextAsync(string baseUrl, string apiKey, string instanceName, string phone, string text, CancellationToken cancellationToken = default);

    /// <summary>Envia imagen/video/documento en base64 con pie opcional (POST /message/sendMedia/{instance}). mediatype: image|video|document.</summary>
    Task<EvolutionSendResult> SendMediaAsync(string baseUrl, string apiKey, string instanceName, string phone, string mediatype, string base64, string? mimeType, string? fileName, string? caption, CancellationToken cancellationToken = default);

    /// <summary>Envia una nota de voz en base64 (POST /message/sendWhatsAppAudio/{instance}).</summary>
    Task<EvolutionSendResult> SendAudioAsync(string baseUrl, string apiKey, string instanceName, string phone, string base64, CancellationToken cancellationToken = default);

    /// <summary>Envia una ubicacion (POST /message/sendLocation/{instance}).</summary>
    Task<EvolutionSendResult> SendLocationAsync(string baseUrl, string apiKey, string instanceName, string phone, double latitude, double longitude, string? name, string? address, CancellationToken cancellationToken = default);

    /// <summary>Configura el webhook entrante de la instancia (POST /webhook/set/{instance}) para recibir mensajes.</summary>
    Task<EvolutionSendResult> SetWebhookAsync(string baseUrl, string apiKey, string instanceName, string webhookUrl, string token, CancellationToken cancellationToken = default);

    /// <summary>Elimina un mensaje PARA TODOS (DELETE /chat/deleteMessageForEveryone/{instance}). remoteJid = numero@s.whatsapp.net.</summary>
    Task<EvolutionSendResult> DeleteMessageForEveryoneAsync(string baseUrl, string apiKey, string instanceName, string remoteJid, string messageId, bool fromMe, CancellationToken cancellationToken = default);

    /// <summary>Descarga la media ENTRANTE de un mensaje a base64 (POST /chat/getBase64FromMediaMessage/{instance}).</summary>
    Task<EvolutionMediaResult> GetBase64FromMediaMessageAsync(string baseUrl, string apiKey, string instanceName, string messageKeyId, CancellationToken cancellationToken = default);
}

public interface IEvolutionMasterConfigService
{
    Task<EvolutionMasterDto?> GetAsync(CancellationToken cancellationToken = default);
    Task<EvolutionMasterDto> SaveAsync(SaveEvolutionMasterRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Valida contra el servidor real (reachability + API key). Null si no hay config.</summary>
    Task<EvolutionValidationResult?> ValidateAsync(Guid actorUserId, CancellationToken cancellationToken = default);
}
