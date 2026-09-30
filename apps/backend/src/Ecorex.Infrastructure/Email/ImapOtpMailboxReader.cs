using System.Text.RegularExpressions;
using Ecorex.Application.Scraping;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace Ecorex.Infrastructure.Email;

/// <summary>
/// Lee OTPs/tokens de un buzon por IMAP con MailKit (app-password sobre SSL). Sirve para Gmail
/// (imap.gmail.com) y Microsoft/O365 (outlook.office365.com). Poll hasta que llegue el correo que cumpla
/// los filtros o venza el timeout. No lanza por fallo de red/timeout: devuelve un veredicto tipado.
/// </summary>
public sealed class ImapOtpMailboxReader : IOtpMailboxReader
{
    // Tope de mensajes recientes a inspeccionar por vuelta (los OTP son recientes; evita barrer el buzon).
    private const int MaxScan = 40;

    public async Task<OtpReadResult> ReadTokenAsync(OtpReadRequest req, CancellationToken ct = default)
    {
        Regex rx;
        try { rx = new Regex(req.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (Exception ex) { return new(false, null, $"Regex invalida: {ex.Message}"); }

        var since = req.SinceUtc ?? DateTimeOffset.UtcNow.AddMinutes(-10);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(req.TimeoutSeconds, 5, 600));
        var pollMs = Math.Clamp(req.PollSeconds, 2, 30) * 1000;

        try
        {
            using var client = new ImapClient();
            var opt = req.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(req.Host, req.Port, opt, ct);
            await client.AuthenticateAsync(req.Username, req.Password, ct);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            while (true)
            {
                // IMAP filtra por DIA (granularidad de fecha); el corte fino por hora se hace en memoria.
                var uids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(since.UtcDateTime.Date.AddDays(-1)), ct);
                var start = Math.Max(0, uids.Count - MaxScan);
                for (var i = uids.Count - 1; i >= start; i--)
                {
                    var msg = await inbox.GetMessageAsync(uids[i], ct);
                    if (msg.Date < since) { continue; }
                    if (!string.IsNullOrWhiteSpace(req.FromContains)
                        && !(msg.From?.ToString() ?? string.Empty).Contains(req.FromContains, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(req.SubjectContains)
                        && !(msg.Subject ?? string.Empty).Contains(req.SubjectContains, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var body = (msg.TextBody ?? string.Empty) + "\n" + (msg.HtmlBody ?? string.Empty) + "\n" + (msg.Subject ?? string.Empty);
                    var m = rx.Match(body);
                    if (m.Success)
                    {
                        var token = m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                        await client.DisconnectAsync(true, ct);
                        return new(true, token.Trim(), null, msg.Subject, msg.Date);
                    }
                }

                if (DateTimeOffset.UtcNow >= deadline) { break; }
                try { await Task.Delay(pollMs, ct); } catch (OperationCanceledException) { break; }
            }

            await client.DisconnectAsync(true, ct);
            return new(false, null, "No llego un correo que cumpla los filtros dentro del tiempo de espera.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new(false, null, $"Error IMAP: {ex.Message}"); }
    }
}
