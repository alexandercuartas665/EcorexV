namespace Ecorex.Application.Tenancy;

/// <summary>
/// Motor de CIERRE del agente (Ola 1). Determinista y server-side (NO es una herramienta/MCP que el modelo
/// invoque: se dispara desde el punto de cierre que ya existe -SessionCompleted tras crear_actividad/
/// crear_lead- o desde el marcador [[cierre]]). Al cerrar: (a) si esta configurado "olvidar", reinicia el
/// contexto de la conversacion (no destructivo, el agente saluda desde cero); (b) dispara las alertas
/// configuradas (WhatsApp por plantilla y/o correo) al usuario asignado o a otro usuario elegido.
/// </summary>
public interface IAgentCierreService
{
    /// <summary>Configuracion de cierre del agente (nunca null; vacia si no hay).</summary>
    Task<AgentCierreConfig> GetConfigAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>Guarda la configuracion de cierre del agente. Devuelve false si el agente no existe.</summary>
    Task<bool> SaveConfigAsync(Guid agentId, AgentCierreConfig config, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta el cierre para una conversacion real: reinicio de memoria (si aplica) + alertas. Best-effort:
    /// nunca lanza (un fallo de envio no debe romper la respuesta al cliente). summary = resumen del cierre
    /// (cuerpo del marcador [[cierre: ...]] o el texto final del agente).
    /// </summary>
    Task HandleCloseAsync(Guid agentId, Guid conversationId, string? summary, Guid actorUserId, CancellationToken cancellationToken = default);
}
