namespace Ecorex.Application.Tenancy;

/// <summary>
/// Catalogo de tipos de actividad del tenant activo (ej. "Direccion Comercial/Cotizacion").
/// Clasifican los TaskItem; en FASE 4 anclaran la definicion de flujo (WorkflowDefinitionId).
/// </summary>
public interface IActivityTypeService
{
    Task<IReadOnlyList<ActivityTypeDto>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<ActivityTypeDto?> GetAsync(Guid activityTypeId, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ActivityTypeDto>> CreateAsync(CreateActivityTypeRequest request, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<ActivityTypeDto>> UpdateAsync(Guid activityTypeId, UpdateActivityTypeRequest request, CancellationToken cancellationToken = default);
    /// <summary>Borra el tipo si ninguna tarea lo usa; si esta en uso, lo archiva (soft).</summary>
    Task<TaskCoreResult<bool>> DeleteAsync(Guid activityTypeId, CancellationToken cancellationToken = default);
}
