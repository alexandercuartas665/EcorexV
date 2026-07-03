namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de un paso del historial de seguimiento (WorkflowStepHistory). El historial es
/// APPEND-ONLY: un paso Rejected nunca se borra; la reactivacion crea una fila nueva.
/// Skipped marca ramas pendientes que quedaron sin efecto al completarse la instancia.
/// </summary>
public enum WorkflowStepStatus
{
    Pending = 0,
    Completed,
    Rejected,
    Skipped
}
