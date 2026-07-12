using System.Collections.Concurrent;
using Ecorex.Application.Common;

namespace Ecorex.Integration.Tests;

/// <summary>IEmailSender no-op para pruebas que no verifican el correo (devuelve Ok=false).</summary>
public sealed class NoOpEmailSender : IEmailSender
{
    public Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => Task.FromResult(new EmailSendResult(false, "correo deshabilitado en pruebas"));
}

/// <summary>IEmailSender que registra los envios, para verificar la entrega por email (#4a).</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task<EmailSendResult> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue((toEmail, subject, htmlBody));
        return Task.FromResult(new EmailSendResult(true, null));
    }
}
