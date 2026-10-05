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
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(req.TimeoutSeconds, 5, 600));
        var pollMs = Math.Clamp(req.PollSeconds, 2, 30) * 1000;

        // Mensajes del buzon, mas recientes primero. Filtro fino (remitente/asunto/fecha) en memoria.
        var url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(req.Username)}/messages"
            + $"?$top={MaxScan}&$orderby=receivedDateTime%20desc"
            + "&$select=subject,from,receivedDateTime,bodyPreview,body";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            while (true)
            {
                using var resp = await http.GetAsync(url, ct);
                var payload = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var msg = ExtractGraphError(payload);
                    return new(false, null, $"Graph {(int)resp.StatusCode}: {msg}");
                }

                using (var doc = JsonDocument.Parse(payload))
                {
                    if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
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
                    }
                }

                if (DateTimeOffset.UtcNow >= deadline) { break; }
                try { await Task.Delay(pollMs, ct); } catch (OperationCanceledException) { break; }
            }

            return new(false, null, "No llego un correo que cumpla los filtros dentro del tiempo de espera.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(false, null, $"Error Graph: {ex.Message}"); }
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
