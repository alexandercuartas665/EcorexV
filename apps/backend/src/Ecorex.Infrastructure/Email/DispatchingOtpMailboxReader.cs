using Ecorex.Application.Scraping;

namespace Ecorex.Infrastructure.Email;

/// <summary>
/// Enruta la lectura del OTP segun el modo de autenticacion del buzon: <c>Graph</c> usa Microsoft Graph API
/// (<see cref="GraphOtpMailboxReader"/>); <c>Basic</c>/<c>OAuth2</c> usan IMAP (<see cref="ImapOtpMailboxReader"/>).
/// Es el <see cref="IOtpMailboxReader"/> que ve el resto del sistema; los lectores concretos se inyectan.
/// </summary>
public sealed class DispatchingOtpMailboxReader(ImapOtpMailboxReader imap, GraphOtpMailboxReader graph) : IOtpMailboxReader
{
    public Task<OtpReadResult> ReadTokenAsync(OtpReadRequest req, CancellationToken ct = default)
        => string.Equals(req.AuthMode, "Graph", StringComparison.OrdinalIgnoreCase)
            ? graph.ReadTokenAsync(req, ct)
            : imap.ReadTokenAsync(req, ct);
}
