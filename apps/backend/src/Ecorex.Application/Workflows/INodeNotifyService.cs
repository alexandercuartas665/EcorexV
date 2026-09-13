namespace Ecorex.Application.Workflows;

/// <summary>
/// Motor de reglas de notificacion por NODO de flujo (ADR-0100). Determinista y server-side (NO es una
/// herramienta/MCP): lo dispara el WorkflowEngine cuando un paso LLEGA (se vuelve actual). Reusa el
/// despachador de canales del Cierre (correo/WhatsApp/grupo/Telegram). Best-effort: nunca lanza.
/// La configuracion de reglas se lee/escribe por IWorkflowDesignService (metadato del nodo, NotifyJson).
/// </summary>
public interface INodeNotifyService
{
    /// <summary>
    /// Dispara las reglas de notificacion del nodo al activarse el paso. taskId null = instancia sin tarea
    /// (los tokens de tarea quedan vacios y no hay enlace). Nunca lanza.
    /// </summary>
    Task NotifyStepArrivalAsync(Guid nodeId, Guid stepId, Guid? taskId, Guid actorUserId, CancellationToken cancellationToken = default);
}
