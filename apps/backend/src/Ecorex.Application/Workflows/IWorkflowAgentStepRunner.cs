namespace Ecorex.Application.Workflows;

/// <summary>
/// Atiende un paso de flujo con el AGENTE DE IA asignado a su nodo (agentes en nodos, ola 2).
/// Tenant-scoped: trabaja con el tenant activo y todo lo que lee pasa por el filtro global.
///
/// Contrato de seguridad del motor: pase lo que pase, el caso NUNCA queda atascado ni cerrado en
/// falso. Si el agente resuelve y el nodo es Autonomous, el paso se cierra con el AGENTE como
/// autor; si el nodo es Proposes, el paso sigue vigente con la propuesta guardada; y si el agente
/// no puede (falta info, error del proveedor, cupo agotado), el paso queda para la MISMA persona
/// que lo habria atendido si no hubiera agente, con el motivo escrito.
/// </summary>
public interface IWorkflowAgentStepRunner
{
    /// <summary>
    /// Atiende el paso indicado. Idempotente: un paso ya intentado (AgentAttemptedAt con valor) se
    /// devuelve como <see cref="WorkflowAgentStepOutcome.AlreadyAttempted"/> sin volver a pagar
    /// tokens, que es lo que permite reintentar el barrido sin miedo tras un reinicio.
    /// </summary>
    Task<WorkflowAgentStepOutcome> RunAsync(Guid stepId, CancellationToken cancellationToken = default);
}

/// <summary>Como termino el intento del agente sobre un paso.</summary>
public enum WorkflowAgentStepOutcome
{
    /// <summary>El paso no existe (o es de otro tenant), no esta vigente, o no es atendible.</summary>
    NotApplicable = 0,

    /// <summary>El nodo no tiene agente asignado: lo atiende gente, como siempre.</summary>
    NoAgent = 1,

    /// <summary>Ya se habia intentado antes; no se repite la llamada al proveedor.</summary>
    AlreadyAttempted = 2,

    /// <summary>Modo Autonomous: el agente cerro el paso y el flujo avanzo.</summary>
    Completed = 3,

    /// <summary>Modo Proposes: quedo la propuesta y el paso sigue esperando a una persona.</summary>
    Proposed = 4,

    /// <summary>El agente no pudo: el paso volvio a una persona con el motivo registrado.</summary>
    ReturnedToPerson = 5
}
