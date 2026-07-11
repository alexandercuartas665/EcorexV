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

    /// <summary>
    /// Clasificacion legacy (catalogo plano ActivityType). DEPRECADA por D1: la tarea pivota a
    /// <see cref="SubcategoriaId"/> (concepto). Nullable en la transicion: se conserva en las tareas
    /// existentes y en el alta antigua, pero las tareas nuevas se clasifican por subcategoria.
    /// </summary>
    public Guid? ActivityTypeId { get; set; }
    public ActivityType? ActivityType { get; set; }

    /// <summary>
    /// Concepto (subcategoria del catalogo 000270) que clasifica y gobierna la tarea: de el se
    /// derivan tablero/columna, y (FASE Ola 2) flujo, formulario y flags. Nullable: las tareas
    /// legacy quedan en null; el alta nueva lo exige. FK Restrict (NO ACTION).
    /// </summary>
    public Guid? SubcategoriaId { get; set; }
    public ActividadSubcategoria? Subcategoria { get; set; }

    /// <summary>
    /// Entidad (Empresa/Area, modulo 000616) a la que pertenece la tarea; fuente del selector
    /// "Empresa/Area" del alta. Nullable. FK Restrict (NO ACTION).
    /// </summary>
    public Guid? EntidadId { get; set; }
    public Entidad? Entidad { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;

    /// <summary>Responsable actual (TenantUser). Null = sin asignar.</summary>
    public Guid? AssigneeTenantUserId { get; set; }
    public TenantUser? AssigneeTenantUser { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    /// <summary>Fecha de inicio planificada (vista Gantt del prototipo). Null = sin planificar.</summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>
    /// Tablero de actividades al que esta colgada la tarea (ADR-0020, Kind = Activities).
    /// Null = tarea fuera de tableros. FK sin cascada: borrar el tablero exige desacoplar antes.
    /// </summary>
    public Guid? BoardId { get; set; }
    public TaskBoard? Board { get; set; }

    /// <summary>
    /// Columna del tablero donde vive la tarjeta. Debe pertenecer a BoardId (lo valida
    /// Application). Null si la tarea no esta en un tablero. FK sin cascada.
    /// </summary>
    public Guid? ColumnId { get; set; }
    public TaskBoardColumn? Column { get; set; }

    /// <summary>Posicion vertical de la tarjeta dentro de su columna (0 = arriba).</summary>
    public int BoardSortOrder { get; set; }

    // Solicitante externo (quien pidio la tarea, no necesariamente un usuario del sistema).
    public string? RequesterName { get; set; }
    public string? RequesterEmail { get; set; }
    public string? RequesterPhone { get; set; }

    /// <summary>Correos en copia, serializados como arreglo JSON (jsonb / nvarchar(max) segun motor).</summary>
    public string? CcEmails { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Proyectos P3: hito del proyecto al que se enlaza la actividad (opcional).</summary>
    public Guid? MilestoneId { get; set; }
    public ProjectMilestone? Milestone { get; set; }

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
