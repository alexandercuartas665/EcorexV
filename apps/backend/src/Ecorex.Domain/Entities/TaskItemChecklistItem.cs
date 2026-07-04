using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Item de checklist de una tarea del nucleo (ADR-0020): la lista de subtareas visibles en
/// la tarjeta del tablero de actividades ("3/4" y barra de progreso). Entidad TENANT-SCOPED,
/// vive y muere con su TaskItem (FK cascade).
/// </summary>
public class TaskItemChecklistItem : TenantEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    /// <summary>Texto del item (max 500).</summary>
    public string Text { get; set; } = null!;

    public bool IsCompleted { get; set; }

    /// <summary>Momento en que se marco completado. Null si esta pendiente.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// TenantUser que completo el item. Columna informativa sin FK dura: conservar la traza
    /// aunque el usuario salga del tenant no debe bloquear su borrado.
    /// </summary>
    public Guid? CompletedByTenantUserId { get; set; }

    /// <summary>Orden vertical dentro del checklist (0 = arriba).</summary>
    public int SortOrder { get; set; }
}
