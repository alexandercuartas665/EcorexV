using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Nucleo de tareas del tenant activo (TaskItem, ADR-0013): consecutivo por tenant,
/// maquina de estados, asignacion, etiquetas por tenant, comentarios, adjuntos y worklogs.
/// Los conflictos de concurrencia y las transiciones invalidas regresan como TaskCoreResult
/// tipado, nunca como excepcion cruda.
/// </summary>
public interface ITaskItemService
{
    /// <summary>
    /// Crea la tarea en una transaccion atomica: emite el consecutivo (prefijo "T", padding 5,
    /// code "T05"), inserta la tarea + etiquetas + actividad "creo la tarea". Estado inicial
    /// Pending, o Active si trae asignado.
    /// </summary>
    Task<TaskCoreResult<TaskItemDetailDto>> CreateAsync(CreateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>Actualiza con token de concurrencia: version vieja -> resultado Conflict.</summary>
    Task<TaskCoreResult<TaskItemDetailDto>> UpdateAsync(Guid taskId, UpdateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia el estado validando TaskItemStateMachine; transicion invalida -> InvalidTransition.
    /// Registra actividad y setea ClosedAt al pasar a Closed.
    /// </summary>
    Task<TaskCoreResult<TaskItemSummaryDto>> ChangeStatusAsync(Guid taskId, TaskItemStatus newStatus, string? reason, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<TaskItemSummaryDto>> AssignAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<TaskItemSummaryDto>> UnassignAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archiva la tarea (soft-archive, IsArchived = true): sale de los listados por defecto
    /// (ListAsync sin IncludeArchived) pero conserva toda su historia. NO es una transicion de
    /// la maquina de estados: el Status no cambia. Decision: archivar tareas Closed SI esta
    /// permitido (es el caso tipico: limpiar el historial cerrado); la restriccion de solo
    /// lectura de Closed aplica a la EDICION de contenido, no a la visibilidad.
    /// Registra la actividad "archivo la tarea".
    /// </summary>
    Task<TaskCoreResult<TaskItemSummaryDto>> ArchiveAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restaura una tarea archivada (IsArchived = false): vuelve a los listados por defecto
    /// con el mismo Status que tenia. Registra la actividad "restauro la tarea".
    /// </summary>
    Task<TaskCoreResult<TaskItemSummaryDto>> RestoreAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    // Etiquetas (catalogo por tenant)
    Task<IReadOnlyList<TaskItemTagDto>> ListTagsAsync(CancellationToken cancellationToken = default);
    Task<TaskCoreResult<TaskItemTagDto>> CreateTagAsync(string name, string? color, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<bool>> AttachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<bool>> DetachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default);

    // Checklist (ADR-0020): items de subtarea visibles en la tarjeta del tablero.
    Task<TaskCoreResult<TaskItemChecklistItemDto>> AddChecklistItemAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca/desmarca el item. Al COMPLETAR registra CompletedAt/CompletedByTenantUserId y
    /// la actividad "completo el item..."; al desmarcar limpia ambos sin actividad.
    /// </summary>
    Task<TaskCoreResult<TaskItemChecklistItemDto>> ToggleChecklistItemAsync(Guid checklistItemId, bool isCompleted, Guid? completedByTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<bool>> RemoveChecklistItemAsync(Guid checklistItemId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>Reordena el checklist completo de la tarea segun la lista de ids.</summary>
    Task<TaskCoreResult<bool>> ReorderChecklistAsync(Guid taskId, IReadOnlyList<Guid> orderedItemIds, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    // Asignados M:N (ADR-0020): equipo adicional de la tarea; el ENCARGADO single sigue
    // siendo AssigneeTenantUserId (Assign/Unassign de arriba).
    Task<TaskCoreResult<bool>> AddAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<bool>> RemoveAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    // Comentarios y adjuntos
    Task<TaskCoreResult<TaskItemActivityDto>> AddCommentAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<TaskItemAttachmentDto>> AddAttachmentAsync(AddTaskAttachmentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<TaskCoreResult<bool>> DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    // Worklog
    Task<TaskCoreResult<TaskWorkLogDto>> AddWorkLogAsync(AddTaskWorkLogRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskWorkLogDto>> ListWorkLogsAsync(Guid taskId, CancellationToken cancellationToken = default);
    Task<long> TotalSecondsAsync(Guid taskId, CancellationToken cancellationToken = default);

    // Consulta
    Task<PagedResult<TaskItemSummaryDto>> ListAsync(TaskItemListFilter filter, CancellationToken cancellationToken = default);
    Task<TaskItemDetailDto?> GetDetailAsync(Guid taskId, CancellationToken cancellationToken = default);
}
