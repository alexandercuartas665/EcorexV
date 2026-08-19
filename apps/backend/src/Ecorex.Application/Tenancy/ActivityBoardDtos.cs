using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

// ---- Tableros de actividades unificados (ADR-0020): las tarjetas SON TaskItem ----

/// <summary>Miembro del tablero/tarjeta con iniciales para el avatar.</summary>
public sealed record ActivityBoardMemberDto(Guid TenantUserId, string Initials, string DisplayName);

/// <summary>
/// KPIs globales del indice de tableros (prototipo 000636). AtRiskBoards: tableros con
/// Status = AtRisk O con al menos una tarea vencida (DueDate pasada y fuera de columna
/// IsDone) - decision documentada en ADR-0020.
/// </summary>
public sealed record ActivityBoardKpisDto(int TotalBoards, int TotalTasks, int CompletedTasks, int AtRiskBoards);

/// <summary>Fila del indice de tableros: datos del chip + progreso + avatares.</summary>
public sealed record ActivityBoardSummaryDto(
    Guid Id, string? Code, string Name, string? Description, string? Color,
    TaskBoardStatus Status, DateTimeOffset? DueDate, bool IsArchived, int SortOrder,
    IReadOnlyList<string> ColumnNames, int ProgressPct, int TaskCount,
    IReadOnlyList<ActivityBoardMemberDto> Members);

public sealed record ActivityBoardIndexDto(
    IReadOnlyList<ActivityBoardSummaryDto> Boards, ActivityBoardKpisDto Kpis);

/// <summary>
/// Filtros del INDICE (server-side, combinables AND): usuario miembro (encargado O asignado
/// de alguna tarea del tablero), etiqueta y tipo de actividad de sus tareas, y rango sobre
/// la fecha limite DEL TABLERO.
/// </summary>
public sealed record ActivityBoardIndexFilter(
    Guid? MemberTenantUserId = null,
    Guid? TagId = null,
    Guid? ActivityTypeId = null,
    DateTimeOffset? DueFrom = null,
    DateTimeOffset? DueTo = null,
    bool IncludeArchived = false,
    // Ola 2 UI: dropdown "Fecha" del indice (Con fecha limite / Sin fecha). Null = todas.
    bool? HasDueDate = null,
    // Dropdown "Categoria" del indice: agrupador de tipos de actividad (ActivityType.Category).
    // Filtra los tableros con alguna tarea cuyo tipo esta en esa categoria. Null = todas.
    string? CategoryName = null);

public sealed record CreateActivityBoardRequest(
    string Name, string? Description = null, string? Color = null,
    string? Code = null,
    TaskBoardStatus Status = TaskBoardStatus.InProgress,
    DateTimeOffset? DueDate = null);

public sealed record UpdateActivityBoardRequest(
    string Name, string? Description, string? Color,
    TaskBoardStatus Status, DateTimeOffset? DueDate, bool IsArchived,
    // Motivos de cierre del tablero (null = no tocar; lista vacia = borrar).
    IReadOnlyList<string>? CloseReasons = null);

/// <summary>Alcance del detalle del tablero (chips del prototipo).</summary>
public enum ActivityBoardScope
{
    /// <summary>Equipo: todas las tarjetas.</summary>
    Team = 0,
    /// <summary>Pendientes mias: el usuario actual es encargado O asignado.</summary>
    Mine,
    /// <summary>No asignadas: sin encargado y sin asignados.</summary>
    Unassigned,
    /// <summary>Terminadas: tareas con estado Done (Terminada).</summary>
    Done
}

/// <summary>Filtro de fecha limite de las tarjetas (chips hoy / manana / con fecha).</summary>
public enum ActivityDueFilter
{
    Any = 0,
    Today,
    Tomorrow,
    /// <summary>Fecha puntual: usar DueOn del filtro.</summary>
    OnDate
}

/// <summary>
/// Filtros del DETALLE del tablero, combinables AND y aplicados en SQL (LINQ server-side).
/// AssigneeTenantUserIds matchea encargado O asignado M:N. CurrentTenantUserId es requerido
/// para Scope = Mine (los contadores de alcance lo usan tambien).
/// </summary>
public sealed record ActivityBoardDetailFilter(
    IReadOnlyList<Guid>? ColumnIds = null,
    IReadOnlyList<Guid>? AssigneeTenantUserIds = null,
    ActivityDueFilter Due = ActivityDueFilter.Any,
    DateTimeOffset? DueOn = null,
    IReadOnlyList<Guid>? TagIds = null,
    ActivityBoardScope Scope = ActivityBoardScope.Team,
    Guid? CurrentTenantUserId = null);

/// <summary>
/// Contadores por alcance del detalle (chips "Equipo / Pendientes mias / No asignadas").
/// Se calculan con los DEMAS filtros aplicados (columnas/asignados/fecha/tags), ignorando
/// el alcance seleccionado.
/// </summary>
public sealed record ActivityScopeCountersDto(int Team, int Mine, int Unassigned, int Done);

/// <summary>Tarjeta del tablero de actividades: un TaskItem con todo lo que pinta la UI.</summary>
public sealed record ActivityCardDto(
    Guid Id, string Number, string Title, string? Description,
    TaskPriority Priority, TaskItemStatus Status,
    DateTimeOffset? StartDate, DateTimeOffset? DueDate,
    int ChecklistDone, int ChecklistTotal, int ChecklistPct,
    string? ProgressColor,
    ActivityBoardMemberDto? Assignee,
    IReadOnlyList<ActivityBoardMemberDto> TeamAssignees,
    int AttachmentsCount, int CommentsCount,
    IReadOnlyList<TaskItemTagDto> Tags,
    Guid ColumnId, int BoardSortOrder, long Version,
    // Ola 3 (aditivo): fecha de creacion para la vista Gantt (si la tarea no tiene
    // StartDate, la barra arranca en CreatedAt segun el prototipo).
    DateTimeOffset CreatedAt = default,
    // Momento en que la tarjeta entro a su columna actual. Null en tareas anteriores a la
    // funcion; la UI cae a CreatedAt para mostrar "cuanto lleva aqui".
    DateTimeOffset? ColumnEnteredAt = null,
    // Ola 2 ADR-0065: valores de los campos personalizados (jsonb FieldKey->valor), para
    // pintarlos como columnas en la vista Lista configurable. Null si no tiene.
    string? CustomFieldsJson = null,
    // Subtareas (tareas hijas): IsSubtask marca la tarjeta con el indicador; ParentNumber es el
    // numero del padre (para el tooltip). El progreso del padre agrega las subtareas terminadas.
    bool IsSubtask = false, string? ParentNumber = null,
    // Subtareas terminadas / totales del padre. La fila "Progreso" de la tarjeta suma checklist +
    // subtareas (mismo criterio que ChecklistPct) para que texto y barra coincidan.
    int SubtaskDone = 0, int SubtaskTotal = 0,
    // Solicitante de la tarea: para agrupar la vista Lista por este campo (ADR-0065).
    string? RequesterName = null);

public sealed record ActivityBoardColumnDto(
    Guid Id, string Name, string? Color, int SortOrder, bool IsDone,
    IReadOnlyList<ActivityCardDto> Cards);

public sealed record ActivityBoardDetailDto(
    Guid Id, string? Code, string Name, string? Description,
    TaskBoardStatus Status, DateTimeOffset? DueDate, bool IsArchived,
    IReadOnlyList<ActivityBoardColumnDto> Columns,
    ActivityScopeCountersDto ScopeCounters,
    // Ola 2 ADR-0065: config de columnas de la vista Lista (JSON). Null = columnas por defecto.
    string? ListViewConfigJson = null,
    // Motivos de cierre configurados del tablero (para pedirlos al mover a una columna final). Vacio = no se pregunta.
    IReadOnlyList<string>? CloseReasons = null);

/// <summary>
/// Creacion rapida desde la columna del tablero. ActivityTypeId null usa el primer tipo
/// de actividad activo del tenant (decision ADR-0020: el quick-add del prototipo no pide
/// tipo; el detalle permite corregirlo).
/// </summary>
public sealed record QuickCreateTaskRequest(
    Guid BoardId, Guid ColumnId, string Title, string? Description = null,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeTenantUserId = null, DateTimeOffset? DueDate = null,
    IReadOnlyList<Guid>? TagIds = null,
    Guid? ActivityTypeId = null, DateTimeOffset? StartDate = null,
    // Ola 6: crear-desde-tablero clasifica por CONCEPTO (subcategoria sin proceso).
    Guid? SubcategoriaId = null);

/// <summary>
/// Resultado del movimiento de tarjeta. StatusChangedToDone indica si la transicion
/// oportunista a Done se aplico; si la maquina de estados no la permite, la tarjeta se
/// mueve igual y StatusNote lo explica.
/// </summary>
public sealed record MoveTaskResultDto(
    Guid TaskItemId, Guid ColumnId, int BoardSortOrder, bool ColumnIsDone,
    bool StatusChangedToDone, TaskItemStatus Status, string? StatusNote);
