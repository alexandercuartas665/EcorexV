using Ecorex.Infrastructure.Email;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// ADR-0188: correlacion de los eventos de SendGrid. La cabecera X-SMTPAPI lleva solo ids (tenant + ejecucion) en
/// JSON compacto, y el id "queued as" del SMTP se guarda para correlacionar cuando un evento no trae ecx_run.
/// </summary>
public class SmtpSendGridTrackingTests
{
    private static readonly Guid Tenant = Guid.Parse("019a0000-0000-7000-8000-0000000000aa");

    [Fact]
    public void Cabecera_lleva_tenant_y_argumentos_validos_sin_espacios()
    {
        var header = SmtpEmailSender.BuildSmtpApiHeader(
            new Dictionary<string, string> { ["ecx_run"] = "r-1", ["Mala Clave"] = "x", ["vacio"] = " " }, Tenant);

        Assert.Equal($"{{\"unique_args\":{{\"ecx_run\":\"r-1\",\"ecx_tenant\":\"{Tenant}\"}}}}", header);
        Assert.DoesNotContain(" ", header);
    }

    [Fact]
    public void Sin_tenant_ni_argumentos_no_hay_cabecera()
    {
        Assert.Null(SmtpEmailSender.BuildSmtpApiHeader(null, null));
        Assert.Null(SmtpEmailSender.BuildSmtpApiHeader(new Dictionary<string, string>(), Guid.Empty));
    }

    [Theory]
    [InlineData("Ok: queued as ZqOS5O3HQY6qY-xl0Y0J8Q", "ZqOS5O3HQY6qY-xl0Y0J8Q")]
    [InlineData("250 Ok: queued as abc_123.", "abc_123")]
    [InlineData("2.0.0 OK 1700000000 x-12si", null)]
    [InlineData(null, null)]
    public void Id_en_cola_del_servidor(string? response, string? expected)
        => Assert.Equal(expected, SmtpEmailSender.QueuedMessageId(response));
}
