using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Instancia (caso) de un flujo en ejecucion, anclada a una version concreta de la
/// definicion. Puede vincularse 1:1 a un TaskItem (la tarea que gobierna el flujo).
/// Concurrencia optimista portable (Version, mismo patron IVersioned de TaskItem).
/// TENANT-SCOPED.
/// </summary>
public class WorkflowInstance : TenantEntity, IVersioned
{
    public Guid DefinitionId { get; set; }
    public WorkflowDefinition? Definition { get; set; }

    /// <summary>Tarea del nucleo asociada (unica por instancia si no es nula).</summary>
    public Guid? TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Ciclo mas alto abierto por reinicios (0 = primer ciclo, ACTIVIDAD_CICLO legacy).</summary>
    public int CurrentCycle { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }
}
