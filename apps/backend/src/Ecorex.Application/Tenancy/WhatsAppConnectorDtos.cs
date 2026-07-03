namespace Ecorex.Application.Tenancy;

/// <summary>Configuracion de servidor Evolution de la agencia: maestro de la plataforma o propio.</summary>
public sealed record EvolutionServerSettingDto(
    bool UseMasterServer,
    bool MasterReady,
    string? MasterBaseUrl,
    string? OwnBaseUrl,
    string? OwnTokenMasked,
    bool HasOwnToken);

public sealed record SetEvolutionServerRequest(
    bool UseMasterServer,
    string? OwnBaseUrl = null,
    string? OwnApiToken = null);

/// <summary>Resultado de conectar/refrescar una linea: QR a escanear (base64) o error.</summary>
public sealed record LineConnectResult(bool Ok, string? QrBase64, string? Error);

/// <summary>Resultado de un envio de mensaje de prueba. MessageId = id de WhatsApp del mensaje creado (para eliminar para todos).</summary>
public sealed record LineSendResult(bool Ok, string? Error, string? MessageId = null);
