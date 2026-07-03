using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Tipo de actividad del catalogo del tenant (ej. "Direccion Comercial/Cotizacion").
/// Clasifica los TaskItem y en FASE 4 anclara la definicion de flujo de trabajo.
/// TENANT-SCOPED. Unico por (TenantId, Category, Name).
/// </summary>
public class ActivityType : TenantEntity
{
    /// <summary>Agrupador del catalogo (ej. area o direccion: "Direccion Comercial").</summary>
    public string Category { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Archivado: no se ofrece para tareas nuevas pero conserva historia.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Definicion de flujo que gobierna las tareas de este tipo (FASE 4): al crear una
    /// tarea, si la definicion esta publicada, se arranca una instancia automaticamente.
    /// FK real con NO ACTION (archivar la definicion no toca el catalogo).
    /// </summary>
    public Guid? WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    /// <summary>Placeholder FASE 4: si las tareas de este tipo exigen diligenciar un formulario.</summary>
    public bool RequiresForm { get; set; }
}
