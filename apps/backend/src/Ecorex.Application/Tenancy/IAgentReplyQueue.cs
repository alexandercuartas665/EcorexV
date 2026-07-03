namespace Ecorex.Application.Tenancy;

/// <summary>
/// Cola de auto-respuesta del agente de IA. La ingesta de un mensaje entrante encola la conversacion
/// y un despachador en background (con "debounce" para agrupar rafagas) ejecuta al agente y responde.
/// La implementacion real vive en el host que recibe el webhook (SuperAdmin); por defecto se registra
/// una implementacion No-Op para los hosts que no atienden con agentes (mismo patron que IChatBroadcaster).
/// </summary>
public interface IAgentReplyQueue
{
    /// <summary>Programa (o reprograma, reiniciando el debounce) la atencion de una conversacion.</summary>
    void Schedule(Guid tenantId, Guid conversationId);
}

/// <summary>Implementacion por defecto que ignora el encolado (hosts sin despachador de agentes).</summary>
public sealed class NoOpAgentReplyQueue : IAgentReplyQueue
{
    public void Schedule(Guid tenantId, Guid conversationId) { }
}
