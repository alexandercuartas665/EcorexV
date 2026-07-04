namespace Ecorex.Application.Tenancy;

/// <summary>
/// Gestor de tableros de ACTIVIDADES unificados (ADR-0020, modulo 000636 del prototipo):
/// tableros TaskBoard con Kind = Activities cuyas tarjetas son TaskItem de primera clase.
/// Patron de resultados tipados (TaskCoreResult), todo filtrado por el query filter global
/// de tenant. Los tableros CRM heredados (Kind = CrmLegacy) NO pasan por este servicio.
/// </summary>
public interface IActivityBoardService
{
    /// <summary>
    /// Indice de tableros con filtros server-side + KPIs globales calculados sobre el
    /// conjunto filtrado. Por tablero: code, nombre, estado, vencimiento, nombres de
    /// columnas, progreso % (ADR-0020: checklist completado/total; sin checklist cae a
    /// tareas en columna IsDone/total), miembros distintos y conteo de tareas.
    /// </summary>
    Task<ActivityBoardIndexDto> ListBoardsAsync(ActivityBoardIndexFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea el tablero Kind = Activities con Code autogenerado via TenantSequence "PRY"
    /// (prefijo "PRY-", padding 4) si no se envia, y las columnas default del prototipo
    /// (Por hacer / En progreso / En revision / Completado).
    /// </summary>
    Task<TaskCoreResult<ActivityBoardSummaryDto>> CreateBoardAsync(CreateActivityBoardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    Task<TaskCoreResult<ActivityBoardSummaryDto>> UpdateBoardAsync(Guid boardId, UpdateActivityBoardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina el tablero: primero DESACOPLA sus tareas (BoardId/ColumnId = null; las FKs
    /// son NO ACTION a proposito), luego borra tablero + columnas. Las tareas sobreviven.
    /// </summary>
    Task<TaskCoreResult<bool>> DeleteBoardAsync(Guid boardId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detalle del tablero: columnas + tarjetas con filtros combinables server-side
    /// (columnas, asignados encargado-O-M:N, fecha limite hoy/manana/con-fecha, tags,
    /// alcance team/mine/unassigned) y contadores por alcance.
    /// </summary>
    Task<TaskCoreResult<ActivityBoardDetailDto>> GetBoardDetailAsync(Guid boardId, ActivityBoardDetailFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mueve la tarjeta a otra columna del MISMO tablero. Si la columna destino es IsDone
    /// y la maquina de estados permite Status -> Done, aplica la transicion oportunista y
    /// registra actividad; si no la permite, mueve la tarjeta SIN tocar el estado y lo
    /// reporta en StatusNote (nunca falla por eso).
    /// </summary>
    Task<TaskCoreResult<MoveTaskResultDto>> MoveTaskAsync(Guid taskItemId, Guid targetColumnId, int sortOrder, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>Cuelga una tarea existente del tablero (columna dada o la primera).</summary>
    Task<TaskCoreResult<bool>> AddTaskToBoardAsync(Guid taskItemId, Guid boardId, Guid? columnId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>Saca la tarea del tablero (la tarea sigue existiendo fuera de tableros).</summary>
    Task<TaskCoreResult<bool>> RemoveFromBoardAsync(Guid taskItemId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creacion rapida desde la columna: delega en TaskItemService.CreateAsync (consecutivo
    /// "T" + etiquetas + actividad + flujo del tipo, TODO en una transaccion) colgando la
    /// tarea del board/columna en esa misma transaccion.
    /// </summary>
    Task<TaskCoreResult<TaskItemDetailDto>> QuickCreateTaskAsync(QuickCreateTaskRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default);
}
