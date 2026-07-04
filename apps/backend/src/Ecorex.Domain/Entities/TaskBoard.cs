using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Tablero Kanban del tenant para gestionar tareas/proyectos. Cada agencia puede tener varios
/// tableros (ej. "Operacion", "Marketing", "Soporte"). Entidad TENANT-SCOPED.
/// ADR-0020: extendido para los tableros de ACTIVIDADES unificados (Kind = Activities),
/// cuyas tarjetas son TaskItem; los tableros del CRM heredado siguen como CrmLegacy.
/// </summary>
public class TaskBoard : TenantEntity
{
    /// <summary>
    /// Codigo legible del tablero (ej. "PRY-0042", consecutivo TenantSequence "PRY").
    /// Nullable: los tableros CRM heredados no tienen codigo.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Nombre visible del tablero (ej. "Operacion Q1").</summary>
    public string Name { get; set; } = null!;

    /// <summary>Descripcion opcional para que los miembros entiendan el proposito del tablero.</summary>
    public string? Description { get; set; }

    /// <summary>Color del tablero en la lista (hex). Solo visual.</summary>
    public string? Color { get; set; }

    /// <summary>Orden de visualizacion en la lista de tableros del tenant.</summary>
    public int SortOrder { get; set; }

    /// <summary>Tableros archivados quedan ocultos de la lista por defecto pero conservan sus datos.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Estado gerencial del tablero (rotulo manual del responsable, prototipo del indice).
    /// Default InProgress.
    /// </summary>
    public TaskBoardStatus Status { get; set; } = TaskBoardStatus.InProgress;

    /// <summary>Fecha limite del tablero (para el chip "Vence ..." del indice).</summary>
    public DateTimeOffset? DueDate { get; set; }

    /// <summary>
    /// Tipo de tablero (ADR-0020): CrmLegacy (tarjetas TaskCard, default de los existentes)
    /// o Activities (tarjetas = TaskItem, gestor unificado 000636).
    /// </summary>
    public TaskBoardKind Kind { get; set; } = TaskBoardKind.CrmLegacy;
}
