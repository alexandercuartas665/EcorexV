namespace Ecorex.Application.Tenancy;

/// <summary>
/// Catalogo de tipos de actividad del tenant activo (ej. "Direccion Comercial/Cotizacion").
/// Clasifican los TaskItem; WorkflowDefinitionId ancla la definicion de flujo publicada que
/// arranca automaticamente al crear la tarea. Es el respaldo del modulo CONCEPTOS (000270):
/// jerarquia Categoria (agrupador string) / Concepto (fila ActivityType).
/// </summary>
public interface IActivityTypeService
{
    Task<IReadOnlyList<ActivityTypeDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<ActivityTypeDto?> GetAsync(Guid activityTypeId, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ActivityTypeDto>> CreateAsync(CreateActivityTypeRequest request, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ActivityTypeDto>> UpdateAsync(Guid activityTypeId, UpdateActivityTypeRequest request, CancellationToken cancellationToken = default);
    /// <summary>Borra el tipo si ninguna tarea lo usa; si esta en uso, lo archiva (soft).</summary>
    Task<TaskCoreResult<bool>> DeleteAsync(Guid activityTypeId, CancellationToken cancellationToken = default);

    // ---- Operaciones aditivas del modulo Conceptos (000270) ----

    /// <summary>Flujos publicados (no archivados) para el select "proceso vinculado".</summary>
    Task<IReadOnlyList<ActivityWorkflowOptionDto>> ListWorkflowOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Conteo de tareas por tipo (total y abiertas), analogo CANT_USADO del legacy.</summary>
    Task<IReadOnlyList<ActivityTypeUsageDto>> GetUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Archiva o restaura el concepto (FLAG_INA legacy). Invalid si ya esta en ese estado.</summary>
    Task<TaskCoreResult<ActivityTypeDto>> SetArchivedAsync(Guid activityTypeId, bool archived, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renombra la categoria (agrupador) moviendo TODOS sus conceptos en una sola operacion.
    /// Invalid si el nombre nuevo produce duplicados (Categoria, Nombre). Devuelve cuantos movio.
    /// </summary>
    Task<TaskCoreResult<int>> RenameCategoryAsync(string category, string newName, CancellationToken cancellationToken = default);

    /// <summary>Archiva/restaura TODOS los conceptos de la categoria (FLAG_INA de TIPO_TAR legacy).</summary>
    Task<TaskCoreResult<int>> SetCategoryArchivedAsync(string category, bool archived, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve el concepto un puesto arriba/abajo dentro de su categoria (normaliza SortOrder
    /// y permuta con el vecino en un solo SaveChanges). Invalid si ya esta en el extremo.
    /// </summary>
    Task<TaskCoreResult<ActivityTypeDto>> MoveAsync(Guid activityTypeId, bool up, CancellationToken cancellationToken = default);
}
