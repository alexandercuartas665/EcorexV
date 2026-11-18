using Ecorex.Application.Common;
using Ecorex.Application.Gestor;
using Ecorex.Infrastructure.Email;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// ADR-0185: remitente comercial por campana. La direccion propia solo es From si es del dominio autenticado del
/// servidor; si no, va como Reply-To (no se rompe SPF/DKIM ni cae en spam).
/// </summary>
public class SmtpSenderOverrideTests
{
    [Fact]
    public void Sin_remitente_propio_usa_el_del_servidor()
        => Assert.Equal(("soporte", "soporte@bitcode.com.co", (string?)null),
            SmtpEmailSender.ResolveSender("soporte@bitcode.com.co", "soporte", null));

    [Fact]
    public void Sin_nombre_configurado_usa_la_direccion_como_nombre()
        => Assert.Equal("soporte@bitcode.com.co", SmtpEmailSender.ResolveSender("soporte@bitcode.com.co", null, null).FromName);

    [Fact]
    public void Mismo_dominio_cambia_el_from()
    {
        var r = SmtpEmailSender.ResolveSender("soporte@bitcode.com.co", "soporte",
            new EmailSenderOverride("Comercial BITCODE", "comercial@BITCODE.com.co", null));
        Assert.Equal("Comercial BITCODE", r.FromName);
        Assert.Equal("comercial@BITCODE.com.co", r.FromEmail);
        Assert.Null(r.ReplyTo);
    }

    [Fact]
    public void Otro_dominio_conserva_el_from_y_pasa_a_reply_to()
    {
        var r = SmtpEmailSender.ResolveSender("soporte@bitcode.com.co", "soporte",
            new EmailSenderOverride("Comercial SOLDARCO", "comercial@soldarco.com", null));
        Assert.Equal("Comercial SOLDARCO", r.FromName);
        Assert.Equal("soporte@bitcode.com.co", r.FromEmail);
        Assert.Equal("comercial@soldarco.com", r.ReplyTo);
    }

    [Fact]
    public void Responder_a_explicito_gana_y_correos_invalidos_se_ignoran()
    {
        var r = SmtpEmailSender.ResolveSender("soporte@bitcode.com.co", "soporte",
            new EmailSenderOverride(null, "no es correo", "ventas@soldarco.com"));
        Assert.Equal("soporte", r.FromName);
        Assert.Equal("soporte@bitcode.com.co", r.FromEmail);
        Assert.Equal("ventas@soldarco.com", r.ReplyTo);
    }

    [Fact]
    public void Paso_email_lee_el_remitente_de_params_json()
    {
        var s = ContactWorkflowDispatcher.EmailSenderFor("{\"remitenteNombre\":\"Comercial\",\"responderA\":\"a@b.co\"}");
        Assert.NotNull(s);
        Assert.Equal("Comercial", s!.FromName);
        Assert.Equal("a@b.co", s.ReplyTo);
        Assert.Null(ContactWorkflowDispatcher.EmailSenderFor(null));
        Assert.Null(ContactWorkflowDispatcher.EmailSenderFor("{}"));
        Assert.Null(ContactWorkflowDispatcher.EmailSenderFor("no json"));
    }
}
