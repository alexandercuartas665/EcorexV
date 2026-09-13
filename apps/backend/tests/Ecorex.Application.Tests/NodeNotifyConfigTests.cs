using Ecorex.Application.Notifications;
using Ecorex.Application.Workflows;

namespace Ecorex.Application.Tests;

/// <summary>
/// Contrato de las reglas de notificacion por nodo (ADR-0100): parse/serialize, deteccion de vacio y
/// compatibilidad hacia atras (json viejo/invalido -> sin reglas).
/// </summary>
public class NodeNotifyConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ no es json")]
    public void Parse_VacioOInvalido_SinReglas(string? json)
    {
        var cfg = NodeNotifyConfig.Parse(json);
        Assert.NotNull(cfg.Reglas);
        Assert.Empty(cfg.Reglas!);
        Assert.True(cfg.IsEmpty);
    }

    [Fact]
    public void RoundTrip_ConservaLasReglasDeCadaCanal()
    {
        var cfg = new NodeNotifyConfig(new[]
        {
            new NodeNotifyRule(NotifyChannel.Correo, NodeNotifyRecipient.StepAssignee,
                Asunto: "Nueva tarea {tarea.numero}", Mensaje: "Hola {tarea.contacto}", IncluirEnlace: true),
            new NodeNotifyRule(NotifyChannel.WhatsApp, NodeNotifyRecipient.Usuario,
                UsuarioId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                LineaId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Plantilla: "aviso_paso", Idioma: "es"),
            new NodeNotifyRule(NotifyChannel.WhatsAppGrupo,
                LineaGrupoId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                GrupoJid: "120363000000000000@g.us", Mensaje: "Llego {tarea.titulo}"),
            new NodeNotifyRule(NotifyChannel.Telegram, ChatId: "-1009876543210", Mensaje: "Paso {tarea.numero}")
        });

        var back = NodeNotifyConfig.Parse(cfg.Serialize());
        Assert.Equal(4, back.Reglas!.Count);

        var correo = back.Reglas![0];
        Assert.Equal(NotifyChannel.Correo, correo.Canal);
        Assert.Equal("Nueva tarea {tarea.numero}", correo.Asunto);
        Assert.True(correo.IncluirEnlace);

        var wa = back.Reglas![1];
        Assert.Equal(NotifyChannel.WhatsApp, wa.Canal);
        Assert.Equal(NodeNotifyRecipient.Usuario, wa.Destino);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), wa.UsuarioId);
        Assert.Equal("aviso_paso", wa.Plantilla);

        var grupo = back.Reglas![2];
        Assert.Equal(NotifyChannel.WhatsAppGrupo, grupo.Canal);
        Assert.Equal("120363000000000000@g.us", grupo.GrupoJid);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), grupo.LineaGrupoId);

        var tg = back.Reglas![3];
        Assert.Equal(NotifyChannel.Telegram, tg.Canal);
        Assert.Equal("-1009876543210", tg.ChatId);
    }

    [Fact]
    public void Serialize_EscribeEnumsComoTexto()
    {
        var cfg = new NodeNotifyConfig(new[] { new NodeNotifyRule(NotifyChannel.Telegram) });
        var json = cfg.Serialize();
        Assert.Contains("Telegram", json);
        Assert.DoesNotContain("\"canal\":3", json);
    }
}
