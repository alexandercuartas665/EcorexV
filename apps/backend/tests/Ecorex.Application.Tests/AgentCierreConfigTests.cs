using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del contrato de configuracion de CIERRE del agente (Ola 1): parse/serialize, deteccion de
/// "sin acciones" (noop) y compatibilidad hacia atras (json viejo/invalido -> vacio).
/// </summary>
public class AgentCierreConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ no es json valido")]
    public void Parse_JsonVacioOInvalido_DevuelveVacio(string? json)
    {
        var cfg = AgentCierreConfig.Parse(json);
        Assert.False(cfg.Olvidar);
        Assert.NotNull(cfg.Alertas);
        Assert.Empty(cfg.Alertas!);
        Assert.True(cfg.IsNoop);
    }

    [Fact]
    public void RoundTrip_ConservaOlvidarYAlertas()
    {
        var original = new AgentCierreConfig(
            Olvidar: true,
            Alertas: new[]
            {
                new AgentCierreAlerta(CierreCanal.Correo, CierreDestino.Asignado, Asunto: "Nuevo cierre"),
                new AgentCierreAlerta(CierreCanal.WhatsApp, CierreDestino.Usuario,
                    UsuarioId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    LineaId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Plantilla: "cierre_comercial", Idioma: "es")
            });

        var back = AgentCierreConfig.Parse(original.Serialize());

        Assert.True(back.Olvidar);
        Assert.Equal(2, back.Alertas!.Count);

        var correo = back.Alertas![0];
        Assert.Equal(CierreCanal.Correo, correo.Canal);
        Assert.Equal(CierreDestino.Asignado, correo.Destino);
        Assert.Equal("Nuevo cierre", correo.Asunto);

        var wa = back.Alertas![1];
        Assert.Equal(CierreCanal.WhatsApp, wa.Canal);
        Assert.Equal(CierreDestino.Usuario, wa.Destino);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), wa.UsuarioId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), wa.LineaId);
        Assert.Equal("cierre_comercial", wa.Plantilla);
        Assert.Equal("es", wa.Idioma);
    }

    [Fact]
    public void Serialize_EscribeEnumsComoTexto()
    {
        var cfg = new AgentCierreConfig(false, new[] { new AgentCierreAlerta(CierreCanal.WhatsApp, CierreDestino.Usuario) });
        var json = cfg.Serialize();
        Assert.Contains("WhatsApp", json);
        Assert.Contains("Usuario", json);
        // Los enums NO deben salir como numeros.
        Assert.DoesNotContain("\"canal\":0", json);
    }

    [Fact]
    public void RoundTrip_CanalGrupoConservaJidYLinea()
    {
        var cfg = new AgentCierreConfig(false, new[]
        {
            new AgentCierreAlerta(CierreCanal.WhatsAppGrupo, CierreDestino.Asignado,
                LineaId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                GrupoJid: "120363000000000000@g.us")
        });

        var back = AgentCierreConfig.Parse(cfg.Serialize());
        var g = back.Alertas!.Single();
        Assert.Equal(CierreCanal.WhatsAppGrupo, g.Canal);
        Assert.Equal("120363000000000000@g.us", g.GrupoJid);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), g.LineaId);
    }

    [Fact]
    public void RoundTrip_CanalTelegramConservaChatId()
    {
        var cfg = new AgentCierreConfig(false, new[]
        {
            new AgentCierreAlerta(CierreCanal.Telegram, CierreDestino.Asignado, ChatId: "-1001234567890")
        });

        var back = AgentCierreConfig.Parse(cfg.Serialize());
        var t = back.Alertas!.Single();
        Assert.Equal(CierreCanal.Telegram, t.Canal);
        Assert.Equal("-1001234567890", t.ChatId);
    }

    [Fact]
    public void IsNoop_TrueSoloSinOlvidarNiAlertas()
    {
        Assert.True(new AgentCierreConfig(false, Array.Empty<AgentCierreAlerta>()).IsNoop);
        Assert.False(new AgentCierreConfig(true, Array.Empty<AgentCierreAlerta>()).IsNoop);
        Assert.False(new AgentCierreConfig(false, new[] { new AgentCierreAlerta() }).IsNoop);
    }

    [Fact]
    public void Parse_JsonSinAlertas_DejaListaVaciaNoNull()
    {
        var cfg = AgentCierreConfig.Parse("{\"olvidar\":true}");
        Assert.True(cfg.Olvidar);
        Assert.NotNull(cfg.Alertas);
        Assert.Empty(cfg.Alertas!);
    }
}
