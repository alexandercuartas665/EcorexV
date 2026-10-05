namespace Ecorex.Application.Scraping;

/// <summary>Buzon OTP para la UI (sin exponer el secreto). AuthMode "Basic"|"OAuth2"; en OAuth2 se usan
/// OauthTenantId/OauthClientId (el client id no es secreto) y TienePassword = hay client secret guardado.</summary>
public sealed record OtpMailboxDto(
    Guid Id, string Nombre, string Proveedor, string Host, int Puerto, bool UsarSsl, string Usuario,
    bool Activo, bool TienePassword, DateTimeOffset? UltimaValidacion,
    string AuthMode = "Basic", string? OauthTenantId = null, string? OauthClientId = null);

/// <summary>Alta/edicion de un buzon OTP. Password null = conservar el secreto actual (no reescribir).
/// En OAuth2, Password lleva el CLIENT SECRET de la app de Azure AD.</summary>
public sealed record SaveOtpMailboxRequest(
    Guid? Id, string Nombre, string Proveedor, string Host, int Puerto, bool UsarSsl, string Usuario,
    string? Password, bool Activo,
    string AuthMode = "Basic", string? OauthTenantId = null, string? OauthClientId = null);

/// <summary>Servicio de configuracion + prueba del buzon OTP (modulo Extraccion de datos).</summary>
public interface IOtpMailboxConfigService
{
    Task<IReadOnlyList<OtpMailboxDto>> ListAsync(CancellationToken ct = default);
    Task<(Guid? Id, string? Error)> SaveAsync(SaveOtpMailboxRequest req, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Prueba la lectura: se conecta al buzon y busca un correo reciente que cumpla los filtros,
    /// extrayendo el token con la regex. Ventana: los ultimos <paramref name="ventanaMinutos"/> minutos.</summary>
    Task<OtpReadResult> ProbarAsync(Guid configId, string? remitente, string? asunto, string regex,
        int timeoutSegundos, int ventanaMinutos, CancellationToken ct = default);

    /// <summary>Lee un token AHORA para un flujo (lo usa el runtime del paso "Leer token de correo").
    /// SinceUtc acota a correos posteriores al disparo del login.</summary>
    Task<OtpReadResult> LeerTokenAsync(Guid configId, string? remitente, string? asunto, string regex,
        int timeoutSegundos, DateTimeOffset sinceUtc, CancellationToken ct = default);
}
