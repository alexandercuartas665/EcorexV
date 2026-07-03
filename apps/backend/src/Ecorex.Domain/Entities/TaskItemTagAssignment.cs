using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asignacion de una etiqueta del catalogo del tenant a una tarea. TENANT-SCOPED.
/// Unico por (TaskItemId, TagId).
/// </summary>
public class TaskItemTagAssignment : TenantEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public Guid TagId { get; set; }
    public TaskItemTag? Tag { get; set; }
}
