using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Hito de un proyecto (legacy PROYECTOS_HITO): un punto de control con nombre, fecha objetivo
/// y orden. Las actividades (TaskItem) se pueden enlazar a un hito via TaskItem.MilestoneId.
/// TENANT-SCOPED (hereda el filtro global).
/// </summary>
public class ProjectMilestone : TenantEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Fecha objetivo del hito (opcional).</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Orden de presentacion dentro del proyecto.</summary>
    public int SortOrder { get; set; }

    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
