namespace Ecorex.Application.Scraping;

/// <summary>Peticion para leer un OTP/token de un buzon por IMAP.</summary>
/// <param name="Host">Host IMAP (outlook.office365.com / imap.gmail.com).</param>
/// <param name="Port">Puerto (993).</param>
/// <param name="UseSsl">SSL al conectar.</param>
/// <param name="Username">Usuario/correo.</param>
/// <param name="Password">App-password (en claro solo en memoria; el llamador la descifra).</param>
/// <param name="FromContains">Filtro: el remitente contiene (opcional).</param>
/// <param name="SubjectContains">Filtro: el asunto contiene (opcional).</param>
/// <param name="Regex">Regex para extraer el token del cuerpo/asunto; si tiene grupo, usa el grupo 1.</param>
/// <param name="SinceUtc">Solo correos recibidos desde esta hora (para no leer OTPs viejos).</param>
/// <param name="TimeoutSeconds">Cuanto esperar a que llegue el correo (poll).</param>
/// <param name="PollSeconds">Intervalo de reintento.</param>
/// <param name="AuthMode">"Basic" (usuario+password/app-password) u "OAuth2" (Microsoft 365 app-only).</param>
/// <param name="OauthTenantId">OAuth2: tenant de Azure AD.</param>
/// <param name="OauthClientId">OAuth2: client id de la app de Azure AD.</param>
/// <remarks>En OAuth2, <paramref name="Password"/> lleva el CLIENT SECRET de la app y
/// <paramref name="Username"/> es el buzon al que se accede.</remarks>
public sealed record OtpReadRequest(
    string Host, int Port, bool UseSsl, string Username, string Password,
    string? FromContains, string? SubjectContains, string Regex,
    DateTimeOffset? SinceUtc, int TimeoutSeconds, int PollSeconds = 5,
    string AuthMode = "Basic", string? OauthTenantId = null, string? OauthClientId = null);

/// <summary>Resultado de leer el OTP (nunca lanza por fallo de red/timeout: veredicto tipado).</summary>
public sealed record OtpReadResult(
    bool Ok, string? Token, string? Error, string? MatchedSubject = null, DateTimeOffset? MatchedAt = null);

/// <summary>
/// Lee un OTP/token de un buzon por IMAP (MailKit). Server-side (no el agente navegador). Poll hasta que
/// llegue el correo que cumpla los filtros o venza el timeout; extrae el token con la regex.
/// </summary>
public interface IOtpMailboxReader
{
    Task<OtpReadResult> ReadTokenAsync(OtpReadRequest req, CancellationToken ct = default);
}
