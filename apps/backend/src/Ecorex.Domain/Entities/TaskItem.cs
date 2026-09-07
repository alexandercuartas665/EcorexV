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

    /// <summary>
    /// Momento en que la tarjeta ENTRO a su columna actual. Se re-sella cada vez que la tarea
    /// cambia de columna (mover en el tablero); NO cambia al reordenar dentro de la misma columna.
    /// Sirve para "cuanto lleva aqui" en la tarjeta. Null en tareas anteriores a esta funcion: la
    /// UI cae a CreatedAt, que es lo mas cercano a la verdad para una tarjeta que nunca se movio.
    /// </summary>
    public DateTimeOffset? ColumnEnteredAt { get; set; }

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

    /// <summary>Motivo de cierre elegido al mover la tarea a una columna final (opcional). La lista de
    /// motivos se configura por tablero (<see cref="TaskBoard.CloseReasonsJson"/>).</summary>
    public string? CloseReason { get; set; }

    /// <summary>
    /// Instancia de flujo que gobierna esta tarea (FASE 4). Null = tarea sin flujo
    /// (estados libres via TaskItemStateMachine). FK sin cascada.
    /// </summary>
    public Guid? WorkflowInstanceId { get; set; }
    public WorkflowInstance? WorkflowInstance { get; set; }

    /// <summary>
    /// Valores de los campos personalizados del TABLERO de la tarea (dict FieldKey -&gt; valor),
    /// definidos en <see cref="TaskFieldDefinition"/>. Documento JSON: jsonb en PG /
    /// nvarchar(max) en SQL Server. Null = la tarea no tiene ningun campo personalizado capturado.
    /// Es un solo nivel (a diferencia de Tercero.FichasJson) porque la tarea ya sabe su tablero.
    /// </summary>
    public string? CustomFieldsJson { get; set; }

    /// <summary>
    /// Tarea PADRE (subtareas / tareas hijas). Null = tarea de primer nivel. Una subtarea es una
    /// TaskItem completa colgada de otra; hereda tablero/columna/tenant del padre al crearse. FK
    /// sin cascada (Restrict) para evitar rutas de cascada multiples en SQL Server. Un solo nivel:
    /// una subtarea no deberia tener a su vez subtareas (se valida en el servicio).
    /// </summary>
    public Guid? ParentId { get; set; }
    public TaskItem? Parent { get; set; }

    /// <summary>
    /// Tarea ORIGEN que GENERO esta tarea por una regla/flujo (ej. GENERAR_TAREAS_DESDE_TABLA, u Orden de
    /// Trabajo derivada). Distinto de <see cref="ParentId"/> (subtareas): aqui la tarea suele nacer en OTRO
    /// tablero con su propio numero, y este vinculo permite seguir la cadena tarea->hijo->nieto (p.ej. el
    /// lector del tablero movil localiza el descendiente al escanear el codigo impreso de la tarea origen).
    /// Null = la tarea no fue generada por otra. FK sin cascada (Restrict); puede encadenarse (multi-nivel).
    /// </summary>
    public Guid? SourceTaskId { get; set; }
    public TaskItem? SourceTask { get; set; }

    /// <summary>Token de concurrencia optimista portable (lo incrementa el interceptor).</summary>
    public long Version { get; set; }
}
