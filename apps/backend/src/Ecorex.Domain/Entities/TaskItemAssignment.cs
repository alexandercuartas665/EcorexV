using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Asignado ADICIONAL del equipo a una tarea del nucleo (ADR-0020): M:N TaskItem &lt;-&gt;
/// TenantUser para los avatares de la tarjeta. El ENCARGADO (responsable single) sigue
/// siendo TaskItem.AssigneeTenantUserId; esta tabla agrega los demas miembros del equipo.
/// Entidad TENANT-SCOPED, unica por (TaskItemId, TenantUserId).
/// </summary>
public class TaskItemAssignment : TenantEntity
{
    public Guid TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public Guid TenantUserId { get; set; }
    public TenantUser? TenantUser { get; set; }
}
