using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Paso del historial de seguimiento de una instancia (port de TAR_SEGUIMIENTO_PROCESO).
/// APPEND-ONLY: el motor nunca borra filas ni reescribe pasos cerrados; cada reinicio
/// agrega filas nuevas con CycleIndex+1 (auditoria completa de todos los ciclos).
/// IsCurrent equivale al FLAG_SIGUIENTE legacy (el paso que espera atencion).
/// TENANT-SCOPED.
/// </summary>
public class WorkflowStepHistory : TenantEntity
{
    public Guid InstanceId { get; set; }
    public WorkflowInstance? Instance { get; set; }

    public Guid NodeId { get; set; }
    public WorkflowNode? Node { get; set; }

    /// <summary>Iteracion del loop a la que pertenece el paso (0 = primer ciclo).</summary>
    public int CycleIndex { get; set; }

    /// <summary>FLAG_SIGUIENTE legacy: el paso esta activo (pendiente de atencion o recien completado sin avanzar).</summary>
    public bool IsCurrent { get; set; }

    public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.Pending;

    /// <summary>Encargado del paso (TenantUser). Null = sin asignar.</summary>
    public Guid? AssignedToTenantUserId { get; set; }

    /// <summary>Quien ejecuto/resolvio el paso (puede diferir del asignado).</summary>
    public Guid? ExecutedByTenantUserId { get; set; }

    /// <summary>
    /// AGENTE DE IA que ejecuto el paso (agentes en nodos, ola 2). Vive JUNTO a
    /// <see cref="ExecutedByTenantUserId"/> y no lo reemplaza: la traza tiene que poder decir si
    /// una aprobacion de compra la cerro una persona o una maquina, y en modo Proposes el paso lo
    /// cierra una persona a partir de lo que propuso un agente (los DOS autores son ciertos).
    ///
    /// Guid suelto SIN FK a AiAgent a proposito, igual que ExecutedByTenantUserId: el historial es
    /// append-only y debe sobrevivir al borrado del agente. Una auditoria que se puede borrar
    /// cambiando la configuracion no es una auditoria.
    /// </summary>
    public Guid? ExecutedByAiAgentId { get; set; }

    /// <summary>
    /// Momento en que el agente INTENTO atender el paso (exito, propuesta o fallo). Es la marca de
    /// IDEMPOTENCIA del disparo asincrono: el barrido solo toma pasos con este campo en null, asi
    /// que un reinicio del worker no vuelve a pagar tokens por una decision ya tomada.
    /// </summary>
    public DateTimeOffset? AgentAttemptedAt { get; set; }

    /// <summary>
    /// Resultado PROPUESTO por el agente (mismo vocabulario que <see cref="ApprovalResult"/>).
    /// Campo propio y no reuso de ApprovalResult porque CompleteStepAsync SOBRESCRIBE
    /// ApprovalResult/ApprovalComment con lo que decide quien cierra: la propuesta se perderia
    /// justo cuando importa (comparar lo que propuso la maquina con lo que decidio la persona).
    /// </summary>
    public string? AgentProposalResult { get; set; }

    /// <summary>Justificacion del agente para su propuesta (lo que la persona lee antes de confirmar).</summary>
    public string? AgentProposalComment { get; set; }

    /// <summary>
    /// Motivo LEGIBLE por el que el agente no pudo resolver (falta info, error del proveedor, cupo
    /// agotado). Cuando esto tiene valor el paso sigue Pending y vigente para una persona: el
    /// sistema nunca cierra en falso ni deja el caso atascado.
    /// </summary>
    public string? AgentFailureReason { get; set; }

    /// <summary>CYCLESTART legacy: primer nodo de un ciclo abierto por reinicio.</summary>
    public bool IsCycleStart { get; set; }

    /// <summary>Resultado de aprobacion en compuertas (APROBADO legacy, ej. "Approved"/"Rejected").</summary>
    public string? ApprovalResult { get; set; }

    public string? ApprovalComment { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
