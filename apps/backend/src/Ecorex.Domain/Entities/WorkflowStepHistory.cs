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

    /// <summary>CYCLESTART legacy: primer nodo de un ciclo abierto por reinicio.</summary>
    public bool IsCycleStart { get; set; }

    /// <summary>Resultado de aprobacion en compuertas (APROBADO legacy, ej. "Approved"/"Rejected").</summary>
    public string? ApprovalResult { get; set; }

    public string? ApprovalComment { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
