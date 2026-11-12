using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Scraping;
using Microsoft.Identity.Client;

namespace Ecorex.Infrastructure.Email;

/// <summary>
/// Lee OTPs/tokens de un buzon de Microsoft 365 por <b>Microsoft Graph API</b> (app-only / client
/// credentials), NO por IMAP. Es la via que usan integraciones tipo n8n: solo requiere el permiso de
/// APLICACION <c>Mail.Read</c> con consentimiento de admin -se salta el New-ServicePrincipal /
/// Add-MailboxPermission / habilitar IMAP que exige el IMAP app-only-. Reusa la MISMA app de Azure
/// (tenant/client/secret) que el modo OAuth2 de IMAP.
///
/// Flujo: pide token (scope https://graph.microsoft.com/.default) y consulta los mensajes recientes del
/// buzon (<c>/users/{buzon}/messages</c>), filtra por remitente/asunto/fecha y extrae el token con la
/// regex. Poll hasta que llegue o venza el timeout. No lanza por fallo de red: devuelve un veredicto tipado.
/// </summary>
public sealed class GraphOtpMailboxReader : IOtpMailboxReader
{
    private const int MaxScan = 25; // mensajes recientes a revisar por vuelta

    public async Task<OtpReadResult> ReadTokenAsync(OtpReadRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.OauthTenantId) || string.IsNullOrWhiteSpace(req.OauthClientId))
        {
            return new(false, null, "Graph: falta el Tenant ID o el Client ID de Azure AD.");
        }

        Regex rx;
        try { rx = new Regex(req.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (Exception ex) { return new(false, null, $"Regex invalida: {ex.Message}"); }

        // Token app-only para Graph.
        string accessToken;
        try
        {
            var app = ConfidentialClientApplicationBuilder.Create(req.OauthClientId)
                .WithClientSecret(req.Password)
                .WithAuthority($"https://login.microsoftonline.com/{req.OauthTenantId}")
                .Build();
            var tok = await app.AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" }).ExecuteAsync(ct);
            accessToken = tok.AccessToken;
        }
        catch (MsalServiceException ex)
        {
            return new(false, null, $"Graph: no se pudo obtener el token ({ex.ErrorCode}): {ex.Message}");
        }

        var since = req.SinceUtc ?? DateTimeOffset.UtcNow.AddMinutes(-10);
        var plazoSeg = Math.Clamp(req.TimeoutSeconds, 5, 600);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(plazoSeg);
        var pollMs = Math.Clamp(req.PollSeconds, 2, 30) * 1000;

        // Mensajes del buzon, mas recientes primero. La FECHA se filtra en el servidor de Graph ($filter): asi
        // solo viajan los correos de la ventana del login (respuesta chica aunque el buzon tenga correos
        // pesados). Remitente/asunto/regex se filtran en memoria. receivedDateTime esta en $filter y en
        // $orderby a la vez, que es lo que Graph exige para no rechazar la consulta.
        var sinceIso = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(req.Username)}/messages"
            + $"?$filter=receivedDateTime%20ge%20{sinceIso}"
            + $"&$orderby=receivedDateTime%20desc&$top={MaxScan}"
            + "&$select=subject,from,receivedDateTime,bodyPreview,body";

        // El limite por consulta lo pone un CTS por intento (no HttpClient.Timeout): asi se distingue "Graph
        // tardo" (transitorio -> reintenta dentro del plazo) de "cancelaron el flujo" (se propaga).
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var exitos = 0;
        var fallos = 0;
        var fallos5xx = 0;
        string? ultimoFallo = null;
        string? ultimoHttp = null;   // la ultima respuesta de error REAL de Graph (mas informativa que un timeout)
        try
        {
            while (true)
            {
                string? payload = null;
                var restante = deadline - DateTimeOffset.UtcNow;
                var limite = restante < MinRequestTimeout ? MinRequestTimeout
                    : (restante > PerRequestTimeout ? PerRequestTimeout : restante);
                try
                {
                    using var intento = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    intento.CancelAfter(limite);
                    using var resp = await http.GetAsync(url, intento.Token);
                    var body = await resp.Content.ReadAsStringAsync(intento.Token);
                    if (resp.IsSuccessStatusCode)
                    {
                        payload = body;
                    }
                    else if (EsTransitorio(resp.StatusCode))
                    {
                        fallos++;
                        if ((int)resp.StatusCode >= 500) { fallos5xx++; }
                        ultimoFallo = ultimoHttp = $"Graph {(int)resp.StatusCode}: {ExtractGraphError(body)}";
                    }
                    else
                    {
                        // 400/401/403/404...: error de configuracion, reintentar no lo arregla.
                        return new(false, null, $"Graph {(int)resp.StatusCode}: {ExtractGraphError(body)}");
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    fallos++;
                    ultimoFallo = $"Graph no respondio en {limite.TotalSeconds:0} s";
                }
                catch (HttpRequestException ex)
                {
                    fallos++;
                    ultimoFallo = $"error de red con Graph: {ex.Message}";
                }

                if (payload is not null)
                {
                    OtpReadResult? hit;
                    try { hit = Buscar(payload, rx, since, req); }
                    catch (JsonException) { hit = null; fallos++; ultimoFallo = "Graph devolvio una respuesta ilegible"; payload = null; }
                    if (payload is not null) { exitos++; }
                    if (hit is not null) { return hit; }
                }

                if (DateTimeOffset.UtcNow >= deadline) { break; }
                try { await Task.Delay(pollMs, ct); } catch (OperationCanceledException) { break; }
            }

            if (exitos == 0 && fallos5xx > 0)
            {
                // Graph RESPONDE pero con 5xx en cada intento: Exchange Online no esta sirviendo ese buzon
                // (visto 2026-11-12: 502 UnknownError a los ~21 s hasta en la consulta minima).
                return new(false, null,
                    $"Microsoft 365 no esta entregando el buzon {req.Username}: {ultimoHttp} "
                    + $"({fallos5xx} de {fallos} intentos en {plazoSeg} s). Revisar en el admin de Microsoft 365 que el buzon "
                    + "tenga licencia de Exchange Online y este activo (abrirlo en Outlook Web), y el estado del servicio. "
                    + "No es el agente ni ECOREX.");
            }
            if (exitos == 0 && fallos > 0)
            {
                return new(false, null,
                    $"No se pudo leer el buzon por Microsoft Graph (Office 365): {ultimoFallo}. "
                    + $"Se intento {fallos} vez/veces en {plazoSeg} s sin respuesta valida; es lentitud o caida del lado de Microsoft o de la red del servidor, no del agente.");
            }
            var nota = fallos > 0 ? $" (hubo {fallos} consulta(s) fallida(s) a Graph: {ultimoFallo})" : "";
            return new(false, null, "No llego un correo que cumpla los filtros dentro del tiempo de espera." + nota);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(false, null, $"Error Graph: {ex.Message}"); }
    }

    private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(60);
    // Minimo 30 s: Exchange devuelve sus 502 hacia los ~21 s; con menos, el ultimo intento se cortaba antes
    // de ver el error real y el mensaje decia "no respondio" en vez del 502.
    private static readonly TimeSpan MinRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>429 (throttling), 408 y 5xx son transitorios: se reintentan dentro del plazo del paso.</summary>
    private static bool EsTransitorio(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || code == HttpStatusCode.RequestTimeout || (int)code >= 500;

    /// <summary>Busca el token en la pagina de mensajes de Graph. Null si ningun correo cumple los filtros.</summary>
    private static OtpReadResult? Buscar(string payload, Regex rx, DateTimeOffset since, OtpReadRequest req)
    {
        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("value", out var arr) || arr.ValueKind != JsonValueKind.Array) { return null; }
        foreach (var m in arr.EnumerateArray())
        {
            var received = m.TryGetProperty("receivedDateTime", out var rd) && rd.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(rd.GetString(), out var rdt) ? rdt : DateTimeOffset.MinValue;
            if (received < since) { continue; }

            var from = m.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object
                && f.TryGetProperty("emailAddress", out var ea) && ea.TryGetProperty("address", out var ad)
                ? ad.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(req.FromContains)
                && !from.Contains(req.FromContains, StringComparison.OrdinalIgnoreCase)) { continue; }

            var subject = m.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(req.SubjectContains)
                && !subject.Contains(req.SubjectContains, StringComparison.OrdinalIgnoreCase)) { continue; }

            var preview = m.TryGetProperty("bodyPreview", out var bp) ? bp.GetString() ?? "" : "";
            var bodyContent = m.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.Object
                && b.TryGetProperty("content", out var bc) ? bc.GetString() ?? "" : "";
            var haystack = subject + "\n" + preview + "\n" + bodyContent;
            var match = rx.Match(haystack);
            if (match.Success)
            {
                var token = match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : match.Value;
                return new(true, token.Trim(), null, subject, received);
            }
        }
        return null;
    }

    /// <summary>Saca el mensaje del error de Graph ({"error":{"code","message"}}) o devuelve el crudo recortado.</summary>
    private static string ExtractGraphError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
            {
                var code = e.TryGetProperty("code", out var c) ? c.GetString() : null;
                return (string.IsNullOrEmpty(code) ? "" : code + ": ") + (m.GetString() ?? "");
            }
        }
        catch { /* no era JSON */ }
        return payload.Length <= 300 ? payload : payload[..300];
    }
}
