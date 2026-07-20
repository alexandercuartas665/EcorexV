using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Concepto de actividad del CRM (000125): configuracion que gobierna las actividades que se
/// realizan desde el gestor de contactos. Distinto de <see cref="ActivityType"/> y de
/// <see cref="ActividadSubcategoria"/> (que clasifican TAREAS): este catalogo es propio del CRM.
/// TENANT-SCOPED. Unico por (TenantId, Code).
/// </summary>
public class ConceptoActividad : TenantEntity
{
    /// <summary>Codigo del concepto (identificador corto propio del tenant).</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>
    /// Formulario que se diligencia al ejecutar la actividad de este concepto. Null = sin formulario.
    /// FK real con NO ACTION (archivar el formulario no toca el catalogo).
    /// </summary>
    public Guid? FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }

    /// <summary>
    /// Tarea-proceso que produce este concepto: la subcategoria de actividad (000270) con la que
    /// se da de alta un TaskItem al registrar la gestion. La subcategoria ya trae su flujo,
    /// tablero y formulario, asi que NO se guarda aqui el flujo suelto: el alta de tareas sigue
    /// pasando por el mismo puente concepto-tarea que usa el wizard. Null = no genera tarea.
    /// FK real con NO ACTION (archivar la subcategoria no toca el catalogo del CRM).
    /// </summary>
    public Guid? SubcategoriaId { get; set; }
    public ActividadSubcategoria? Subcategoria { get; set; }

    /// <summary>Si el concepto captura/maneja valores (montos).</summary>
    public bool HandlesValues { get; set; }

    /// <summary>Modo de seguimiento: ninguno, proceso de atencion o evento de calendario.</summary>
    public ConceptoActividadMode Mode { get; set; } = ConceptoActividadMode.None;

    public int SortOrder { get; set; }

    /// <summary>Archivado: no se ofrece para actividades nuevas pero conserva historia.</summary>
    public bool IsArchived { get; set; }
}
