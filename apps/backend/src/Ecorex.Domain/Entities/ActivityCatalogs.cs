using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Rasgo comun de los catalogos configurables del modulo de actividades (prioridades, estados,
/// tipos de proyecto): nombre, descripcion, color, activo y orden. Permite tratar los tres de
/// forma generica en el servicio sin duplicar CRUD (mismo enfoque que ICatalogEntity de
/// inventarios). TENANT-SCOPED. Nunca se borra fisico: se archiva (IsActive=false).
/// </summary>
public interface IActivityCatalogEntity
{
    Guid Id { get; }
    string Name { get; set; }
    string? Description { get; set; }
    string? Color { get; set; }
    bool IsActive { get; set; }
    int SortOrder { get; set; }
}

/// <summary>
/// Prioridad configurable de actividades (Sistema - Actividades, legacy 000621). Reemplaza el
/// stub: ahora es un catalogo por tenant que alimenta los chips de prioridad del wizard. Como
/// TaskItem sigue guardando el enum <see cref="TaskPriority"/> (ciclo/consultas dependen de el),
/// cada fila declara a que valor del enum representa (<see cref="MappedPriority"/>). Asi el
/// catalogo controla etiqueta/color/orden sin tocar el modelo de la tarea.
/// </summary>
public class ActivityPriority : TenantEntity, IActivityCatalogEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Valor del enum de la tarea que representa esta fila (High/Medium/Low).</summary>
    public TaskPriority MappedPriority { get; set; } = TaskPriority.Medium;
}

/// <summary>
/// Estado configurable de actividades (Sistema - Actividades, legacy 000653). Catalogo de
/// ETIQUETAS por tenant. DECOUPLED a proposito de <see cref="Ecorex.Domain.Rules.TaskItemStateMachine"/>:
/// el ciclo de vida de la tarea lo sigue gobernando el enum <see cref="TaskItemStatus"/>; este
/// catalogo es para clasificacion/visualizacion configurable y no altera transiciones.
/// </summary>
public class ActivityState : TenantEntity, IActivityCatalogEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Tipo de proyecto configurable (Sistema - Actividades, legacy 000690). Catalogo por tenant que
/// clasifica los proyectos (<see cref="Project"/> lo referencia por FK opcional). TENANT-SCOPED.
/// </summary>
public class ProjectType : TenantEntity, IActivityCatalogEntity
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
