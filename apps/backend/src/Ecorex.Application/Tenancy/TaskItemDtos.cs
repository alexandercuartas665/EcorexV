using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

// ---- Tareas del nucleo (TaskItem, ADR-0013) ----

public sealed record TaskItemTagDto(Guid Id, string Name, string? Color);

public sealed record TaskItemActivityDto(Guid Id, TaskActivityType Type, string ActorName, string Text, DateTimeOffset CreatedAt);

public sealed record TaskItemAttachmentDto(
    Guid Id, string FileName, string Url, string? MimeType, long SizeBytes,
    string? UploadedByName, DateTimeOffset CreatedAt);

public sealed record TaskWorkLogDto(
    Guid Id, Guid TaskItemId, Guid TenantUserId, int Seconds, string? Note,
    WorkLogKind Kind, DateTimeOffset LoggedAt);

/// <summary>Item del checklist de la tarea (ADR-0020).</summary>
public sealed record TaskItemChecklistItemDto(
    Guid Id, Guid TaskItemId, string Text, bool IsCompleted,
    DateTimeOffset? CompletedAt, Guid? CompletedByTenantUserId, int SortOrder);

/// <summary>Miembro asignado a la tarea (encargado o asignado adicional, ADR-0020).</summary>
public sealed record TaskItemAssigneeDto(Guid TenantUserId, string Initials, string DisplayName);

public sealed record TaskItemSummaryDto(
    Guid Id, string Number, string Title, Guid ActivityTypeId, string? ActivityTypeName,
    TaskPriority Priority, TaskItemStatus Status, Guid? AssigneeTenantUserId,
    DateTimeOffset? DueDate, Guid? ProjectId, string? Color, bool IsArchived,
    DateTimeOffset? ClosedAt, long Version, DateTimeOffset CreatedAt,
    IReadOnlyList<TaskItemTagDto> Tags,
    // ADR-0020: fecha de inicio (Gantt) y ubicacion en el tablero de actividades.
    DateTimeOffset? StartDate = null, Guid? BoardId = null, Guid? ColumnId = null);

public sealed record TaskItemDetailDto(
    TaskItemSummaryDto Item,
    string? Description,
    string? RequesterName, string? RequesterEmail, string? RequesterPhone,
    IReadOnlyList<string> CcEmails,
    long TotalWorkSeconds,
    IReadOnlyList<TaskItemActivityDto> RecentActivity,
    IReadOnlyList<TaskItemAttachmentDto> Attachments,
    // ADR-0020: checklist y equipo asignado (encargado + asignados M:N).
    IReadOnlyList<TaskItemChecklistItemDto> Checklist,
    IReadOnlyList<TaskItemAssigneeDto> Assignees);

public sealed record CreateTaskItemRequest(
    string Title, Guid ActivityTypeId, string? Description = null,
    TaskPriority Priority = TaskPriority.Medium,
    Guid? AssigneeTenantUserId = null, DateTimeOffset? DueDate = null,
    string? RequesterName = null, string? RequesterEmail = null, string? RequesterPhone = null,
    IReadOnlyList<string>? CcEmails = null, Guid? ProjectId = null, string? Color = null,
    IReadOnlyList<Guid>? TagIds = null,
    // ADR-0020: inicio planificado y cuelgue opcional en un tablero de actividades
    // (ColumnId debe pertenecer a BoardId; null = primera columna del tablero).
    DateTimeOffset? StartDate = null, Guid? BoardId = null, Guid? ColumnId = null);

/// <summary>Version es el token de concurrencia optimista leido por el cliente (ADR-0013).</summary>
public sealed record UpdateTaskItemRequest(
    string Title, string? Description, Guid ActivityTypeId, TaskPriority Priority,
    DateTimeOffset? DueDate, string? RequesterName, string? RequesterEmail, string? RequesterPhone,
    IReadOnlyList<string>? CcEmails, Guid? ProjectId, string? Color, long Version,
    DateTimeOffset? StartDate = null);

/// <summary>
/// Filtros combinables con AND para el listado de tareas. Todos opcionales; los rangos de
/// vencimiento son inclusivos. Text busca en el titulo (contains, case-insensitive).
/// </summary>
public sealed record TaskItemListFilter(
    IReadOnlyList<TaskItemStatus>? Statuses = null,
    TaskPriority? Priority = null,
    Guid? AssigneeTenantUserId = null,
    Guid? ActivityTypeId = null,
    Guid? ProjectId = null,
    IReadOnlyList<Guid>? TagIds = null,
    DateTimeOffset? DueFrom = null,
    DateTimeOffset? DueTo = null,
    string? Text = null,
    bool IncludeArchived = false,
    int Page = 1,
    int PageSize = 50);

public sealed record AddTaskWorkLogRequest(
    Guid TaskItemId, Guid TenantUserId, int Seconds, string? Note = null,
    WorkLogKind Kind = WorkLogKind.Manual, DateTimeOffset? LoggedAt = null,
    bool LogActivity = false);

public sealed record AddTaskAttachmentRequest(Guid TaskItemId, string FileName, string Url, string? MimeType, long SizeBytes);
