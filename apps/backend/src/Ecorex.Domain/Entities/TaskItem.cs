using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Tarea de primera clase del nucleo ECOREX (ADR-0013): con numero consecutivo por tenant,
/// estados propios gobernados por TaskItemStateMachine, prioridad, solicitante y vinculo
/// opcional a proyecto. Reemplaza al TaskCard heredado (que queda como kanban generico CRM).
/// TENANT-SCOPED, con concurrencia optimista portable (Version, ADR-0013).
/// </summary>
public class TaskItem : TenantEntity, IVersioned
{
    /// <summary>Consecutivo legible por tenant (ej. "T00042"), emitido por TenantSequence.</summary>
    public string Number { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public Guid ActivityTypeId { get; set; }
    public ActivityType? ActivityType { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;

    /// <summary>Responsable actual (TenantUser). Null = sin asignar.</summary>
    public Guid? AssigneeTenantUserId { get; set; }
    public TenantUser? AssigneeTenantUser { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    // Solicitante externo (quien pidio la tarea, no necesariamente un usuario del sistema).
    public string? RequesterName { get; set; }
    public string? RequesterEmail { get; set; }
    public string? RequesterPhone { get; set; }

    /// <summary>Correos en copia, serializados como arreglo JSON (jsonb / nvarchar(max) segun motor).</summary>
    public string? CcEmails { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Color HEX para acentuar la tarea en la UI. Null = sin color especifico.</summary>
    public string? Color { get; set; }

    /// <summary>Soft-archive: fuera de las listas por defecto, conserva historia.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Momento en que la tarea paso a Closed (estado terminal).</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Instancia de flujo que gobierna esta tarea (FASE 4). Null = tarea sin flujo
    /// (estados libres via TaskItemStateMachine). FK sin cascada.
    /// </summary>
    public Guid? WorkflowInstanceId { get; set; }
    public WorkflowInstance? WorkflowInstance { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }
}
