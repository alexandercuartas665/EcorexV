using Ecorex.Application.Tenancy;

namespace Ecorex.Application.Tests;

// ADR-0161 - De-dup de la notificacion de cierre: crear la tarea == cerrar, asi que si el AVISO de tarea
// asignada ya notifico al asignado en el turno, la ALERTA de cierre NO debe repetir el mensaje al mismo
// destinatario. El rastro vive en AiToolRunContext (AsyncLocal), acotado al turno del agente.
public sealed class AgentCierreDedupTests
{
    [Fact]
    public void WasCloseNotified_es_false_sin_turno_activo()
    {
        // Fuera de un turno (sin Begin) no hay contexto: no se marca nada y no se duplica por accidente.
        Assert.False(AiToolRunContext.WasCloseNotified(Guid.NewGuid()));
    }

    [Fact]
    public void Mark_sin_turno_activo_es_noop()
    {
        var uid = Guid.NewGuid();
        AiToolRunContext.MarkCloseNotified(uid); // no debe lanzar ni dejar estado global
        Assert.False(AiToolRunContext.WasCloseNotified(uid));
    }

    [Fact]
    public void Dentro_del_turno_marca_y_detecta_al_destinatario()
    {
        var notificado = Guid.NewGuid();
        var otro = Guid.NewGuid();
        using (AiToolRunContext.Begin(conversationId: Guid.NewGuid(), imageBase64: null, imageMime: null))
        {
            Assert.False(AiToolRunContext.WasCloseNotified(notificado));
            AiToolRunContext.MarkCloseNotified(notificado);
            Assert.True(AiToolRunContext.WasCloseNotified(notificado));  // el aviso de tarea lo notifico
            Assert.False(AiToolRunContext.WasCloseNotified(otro));        // otro usuario sigue sin notificar
        }
    }

    [Fact]
    public void El_rastro_se_descarta_al_terminar_el_turno()
    {
        var uid = Guid.NewGuid();
        using (AiToolRunContext.Begin(conversationId: Guid.NewGuid(), imageBase64: null, imageMime: null))
        {
            AiToolRunContext.MarkCloseNotified(uid);
            Assert.True(AiToolRunContext.WasCloseNotified(uid));
        }
        // Fuera del scope el estado no persiste (no contamina el siguiente turno/cliente).
        Assert.False(AiToolRunContext.WasCloseNotified(uid));
    }

    [Fact]
    public void Turnos_anidados_no_heredan_el_rastro_del_externo()
    {
        var externo = Guid.NewGuid();
        using (AiToolRunContext.Begin(conversationId: Guid.NewGuid(), imageBase64: null, imageMime: null))
        {
            AiToolRunContext.MarkCloseNotified(externo);
            using (AiToolRunContext.Begin(conversationId: Guid.NewGuid(), imageBase64: null, imageMime: null))
            {
                // El scope interno es nuevo: no hereda lo marcado por el externo.
                Assert.False(AiToolRunContext.WasCloseNotified(externo));
            }
            // Al volver al externo, su marca sigue vigente.
            Assert.True(AiToolRunContext.WasCloseNotified(externo));
        }
    }

    // ADR-0163: casos que el agente creo o anoto en el turno (la alerta de cierre avisa a SU usuario asignado).
    [Fact]
    public void Casos_tocados_se_registran_en_orden_sin_duplicados_y_solo_en_el_turno()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        AiToolRunContext.MarkTaskTouched(a); // sin turno: no-op
        Assert.Empty(AiToolRunContext.TouchedTaskIds);
        using (AiToolRunContext.Begin(conversationId: Guid.NewGuid(), imageBase64: null, imageMime: null))
        {
            AiToolRunContext.MarkTaskTouched(a);
            AiToolRunContext.MarkTaskTouched(b);
            AiToolRunContext.MarkTaskTouched(a);
            Assert.Equal(new[] { a, b }, AiToolRunContext.TouchedTaskIds);
        }
        Assert.Empty(AiToolRunContext.TouchedTaskIds);
    }
}
