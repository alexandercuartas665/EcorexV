using Ecorex.Infrastructure.Email;
using MailKit.Security;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Modo TLS del emisor SMTP con MailKit (ADR-0170). El caso que motivo el cambio: SendGrid en el puerto 465
/// (TLS implicito) colgaba a System.Net.Mail.SmtpClient hasta el timeout.
/// </summary>
public class SmtpSocketOptionsTests
{
    [Theory]
    [InlineData(465, true, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, false, SecureSocketOptions.SslOnConnect)]   // 465 siempre es TLS implicito
    [InlineData(587, true, SecureSocketOptions.StartTls)]
    [InlineData(587, false, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(25, true, SecureSocketOptions.Auto)]
    [InlineData(25, false, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(2525, true, SecureSocketOptions.Auto)]
    public void Puerto_y_ssl_definen_el_modo_tls(int port, bool useSsl, SecureSocketOptions expected)
        => Assert.Equal(expected, SmtpEmailSender.ResolveSocketOptions(port, useSsl));

    [Fact]
    public void El_error_incluye_la_cadena_de_excepciones_internas()
    {
        var ex = new InvalidOperationException("Failure sending mail.",
            new IOException("Unable to read data from the transport connection.",
                new TimeoutException("The operation has timed out.")));

        var text = SmtpEmailSender.DescribeError(ex);

        Assert.Equal("Failure sending mail. -> Unable to read data from the transport connection. -> The operation has timed out.", text);
    }
}
