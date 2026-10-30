namespace Ecorex.Application.Tenancy;

/// <summary>
/// Aviso de "TAREA ASIGNADA" (ADR-0140): cuando un agente de IA CREA Y ASIGNA una actividad/tarea
/// (crear_actividad / crear_tarea), notifica al usuario ASIGNADO A LA TAREA (su telefono/correo) con la
/// tarea en contexto, resolviendo las variables de la plantilla por binding ({tarea.*}, {usuario.*},
/// {sistema.*}) y dejando RASTRO en la bitacora del agente y en la historia de la tarea.
///
/// Distinto del CIERRE (<see cref="IAgentCierreService"/>), que se dispara al cerrar la conversacion, apunta
/// al asignado de la LINEA y no conoce la tarea. La configuracion vive en el MISMO CierreJson del agente,
/// bajo <see cref="AgentCierreConfig.AvisosTarea"/> (sin migracion). Best-effort: nunca lanza.
/// </summary>
public interface IAgentAvisoTareaService
{
    /// <summary>
    /// Dispara los avisos de tarea asignada configurados en el agente para la tarea recien creada/asignada.
    /// Best-effort: un fallo de envio queda registrado en la bitacora, nunca rompe la creacion de la tarea.
    /// <paramref name="conversationId"/> = conversacion que origino la tarea (para la bitacora del agente);
    /// null si la tarea no nacio de una conversacion.
    /// </summary>
    Task NotifyTaskAssignedAsync(Guid agentId, Guid taskId, Guid? conversationId, Guid actorUserId, CancellationToken cancellationToken = default);
}
