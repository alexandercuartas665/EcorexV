using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class TaskItemService : ITaskItemService
{
    /// <summary>Consecutivo de tareas: codigo "T05" = prefijo "T" con padding 5 ("T00001").</summary>
    public const string SequenceCode = "T05";
    public const string SequencePrefix = "T";
    public const int SequencePadding = 5;

    private const int MaxWorkLogSeconds = 86400;
    private const int RecentActivityLimit = 20;
    private const string ConflictMessage = "La tarea fue modificada por otro usuario. Recarga e intenta de nuevo.";
    private const string ClosedMessage = "La tarea esta cerrada (Closed) y es de solo lectura.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISequenceService _sequences;

    public TaskItemService(IApplicationDbContext db, ITenantContext tenantContext, ISequenceService sequences)
    {
        _db = db;
        _tenantContext = tenantContext;
        _sequences = sequences;
    }

    public async Task<TaskCoreResult<TaskItemDetailDto>> CreateAsync(CreateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("No hay tenant activo.");
        }
        var title = (request.Title ?? "").Trim();
        if (title.Length == 0)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El titulo es obligatorio.");
        }

        // Validaciones de pertenencia al tenant: el filtro global oculta filas de otros
        // tenants, asi que un id ajeno simplemente "no existe".
        var activityType = await _db.ActivityTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.ActivityTypeId, cancellationToken);
        if (activityType is null)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El tipo de actividad no existe en el tenant.");
        }
        if (activityType.IsArchived)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El tipo de actividad esta archivado.");
        }
        if (request.ProjectId is Guid projectId
            && !await _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El proyecto no existe en el tenant.");
        }
        if (request.AssigneeTenantUserId is Guid assigneeId
            && !await _db.TenantUsers.AnyAsync(u => u.Id == assigneeId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El asignado no pertenece al tenant.");
        }
        var tagIds = (request.TagIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (tagIds.Count > 0)
        {
            var existing = await _db.TaskItemTags.CountAsync(t => tagIds.Contains(t.Id), cancellationToken);
            if (existing != tagIds.Count)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("Alguna etiqueta no existe en el tenant.");
            }
        }

        // Fila del consecutivo asegurada ANTES de la transaccion: una carrera de creacion
        // (violacion de unicidad) no debe abortar la transaccion principal (PostgreSQL).
        await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);

        // Transaccion atomica: consecutivo + tarea + etiquetas + actividad "creo la tarea".
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var number = await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);

        var task = new TaskItem
        {
            TenantId = tenantId,
            Number = number,
            Title = title,
            Description = Normalize(request.Description),
            ActivityTypeId = request.ActivityTypeId,
            Priority = request.Priority,
            // Estado inicial: Pending; Active si nace asignada.
            Status = request.AssigneeTenantUserId is null ? TaskItemStatus.Pending : TaskItemStatus.Active,
            AssigneeTenantUserId = request.AssigneeTenantUserId,
            DueDate = request.DueDate,
            RequesterName = Normalize(request.RequesterName),
            RequesterEmail = Normalize(request.RequesterEmail),
            RequesterPhone = Normalize(request.RequesterPhone),
            CcEmails = SerializeCcEmails(request.CcEmails),
            ProjectId = request.ProjectId,
            Color = Normalize(request.Color)
        };
        _db.TaskItems.Add(task);

        foreach (var tagId in tagIds)
        {
            _db.TaskItemTagAssignments.Add(new TaskItemTagAssignment
            {
                TenantId = tenantId,
                TaskItemId = task.Id,
                TagId = tagId
            });
        }

        _db.TaskItemActivities.Add(BuildActivity(tenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, $"creo la tarea {number}"));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TaskCoreResult<TaskItemDetailDto>.Ok((await GetDetailAsync(task.Id, cancellationToken))!);
    }

    public async Task<TaskCoreResult<TaskItemDetailDto>> UpdateAsync(Guid taskId, UpdateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemDetailDto>.NotFound("Tarea no encontrada.");
        }
        if (task.Status == TaskItemStatus.Closed)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid(ClosedMessage);
        }
        // Token de concurrencia optimista (ADR-0013): token viejo -> conflicto tipado.
        // El ConcurrencyToken de EF cubre ademas la carrera entre esta lectura y el guardado.
        if (task.Version != request.Version)
        {
            return TaskCoreResult<TaskItemDetailDto>.Conflict(ConflictMessage);
        }
        var title = (request.Title ?? task.Title).Trim();
        if (title.Length == 0)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El titulo es obligatorio.");
        }
        var activityType = await _db.ActivityTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.ActivityTypeId, cancellationToken);
        if (activityType is null)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El tipo de actividad no existe en el tenant.");
        }
        if (request.ProjectId is Guid projectId
            && !await _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El proyecto no existe en el tenant.");
        }

        task.Title = title;
        task.Description = Normalize(request.Description);
        task.ActivityTypeId = request.ActivityTypeId;
        task.Priority = request.Priority;
        task.DueDate = request.DueDate;
        task.RequesterName = Normalize(request.RequesterName);
        task.RequesterEmail = Normalize(request.RequesterEmail);
        task.RequesterPhone = Normalize(request.RequesterPhone);
        task.CcEmails = SerializeCcEmails(request.CcEmails);
        task.ProjectId = request.ProjectId;
        task.Color = Normalize(request.Color);

        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, "edito la tarea"));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<TaskItemDetailDto>.Conflict(ConflictMessage);
        }
        return TaskCoreResult<TaskItemDetailDto>.Ok((await GetDetailAsync(taskId, cancellationToken))!);
    }

    public async Task<TaskCoreResult<TaskItemSummaryDto>> ChangeStatusAsync(Guid taskId, TaskItemStatus newStatus, string? reason, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemSummaryDto>.NotFound("Tarea no encontrada.");
        }
        if (task.Status == newStatus)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Invalid("La tarea ya esta en ese estado.");
        }
        // Maquina de estados (Ecorex.Domain.Rules): transicion invalida -> error tipado.
        if (!TaskItemStateMachine.CanTransition(task.Status, newStatus))
        {
            return TaskCoreResult<TaskItemSummaryDto>.InvalidTransition(
                $"Transicion invalida: {task.Status} -> {newStatus}.");
        }

        var previous = task.Status;
        task.Status = newStatus;
        task.ClosedAt = newStatus == TaskItemStatus.Closed ? DateTimeOffset.UtcNow : null;

        var text = $"cambio el estado de {previous} a {newStatus}";
        if (!string.IsNullOrWhiteSpace(reason))
        {
            text += $": {reason.Trim()}";
        }
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, text));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Conflict(ConflictMessage);
        }
        return TaskCoreResult<TaskItemSummaryDto>.Ok(await ToSummaryAsync(task, cancellationToken));
    }

    public async Task<TaskCoreResult<TaskItemSummaryDto>> AssignAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemSummaryDto>.NotFound("Tarea no encontrada.");
        }
        if (task.Status == TaskItemStatus.Closed)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Invalid(ClosedMessage);
        }
        var assignee = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == tenantUserId, cancellationToken);
        if (assignee is null)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Invalid("El asignado no pertenece al tenant.");
        }

        task.AssigneeTenantUserId = tenantUserId;
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, $"asigno la tarea a {assignee.Email}"));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Conflict(ConflictMessage);
        }
        return TaskCoreResult<TaskItemSummaryDto>.Ok(await ToSummaryAsync(task, cancellationToken));
    }

    public async Task<TaskCoreResult<TaskItemSummaryDto>> UnassignAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemSummaryDto>.NotFound("Tarea no encontrada.");
        }
        if (task.Status == TaskItemStatus.Closed)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Invalid(ClosedMessage);
        }
        if (task.AssigneeTenantUserId is null)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Invalid("La tarea no tiene asignado.");
        }

        task.AssigneeTenantUserId = null;
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, "quito la asignacion de la tarea"));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Conflict(ConflictMessage);
        }
        return TaskCoreResult<TaskItemSummaryDto>.Ok(await ToSummaryAsync(task, cancellationToken));
    }

    public async Task<IReadOnlyList<TaskItemTagDto>> ListTagsAsync(CancellationToken cancellationToken = default)
    {
        return await _db.TaskItemTags.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TaskItemTagDto(t.Id, t.Name, t.Color))
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskCoreResult<TaskItemTagDto>> CreateTagAsync(string name, string? color, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<TaskItemTagDto>.Invalid("No hay tenant activo.");
        }
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return TaskCoreResult<TaskItemTagDto>.Invalid("El nombre de la etiqueta es obligatorio.");
        }
        // El indice unico (TenantId, Name) respalda esta validacion amigable.
        if (await _db.TaskItemTags.AnyAsync(t => t.Name == trimmed, cancellationToken))
        {
            return TaskCoreResult<TaskItemTagDto>.Invalid($"Ya existe la etiqueta '{trimmed}'.");
        }
        var tag = new TaskItemTag { TenantId = tenantId, Name = trimmed, Color = Normalize(color) };
        _db.TaskItemTags.Add(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<TaskItemTagDto>.Ok(new TaskItemTagDto(tag.Id, tag.Name, tag.Color));
    }

    public async Task<TaskCoreResult<bool>> AttachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<bool>.Invalid("No hay tenant activo.");
        }
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<bool>.NotFound("Tarea no encontrada.");
        }
        if (!await _db.TaskItemTags.AnyAsync(t => t.Id == tagId, cancellationToken))
        {
            return TaskCoreResult<bool>.NotFound("Etiqueta no encontrada.");
        }
        if (await _db.TaskItemTagAssignments.AnyAsync(a => a.TaskItemId == taskId && a.TagId == tagId, cancellationToken))
        {
            return TaskCoreResult<bool>.Ok(false);
        }
        _db.TaskItemTagAssignments.Add(new TaskItemTagAssignment
        {
            TenantId = tenantId,
            TaskItemId = taskId,
            TagId = tagId
        });
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<bool>> DetachTagAsync(Guid taskId, Guid tagId, CancellationToken cancellationToken = default)
    {
        var assignment = await _db.TaskItemTagAssignments
            .FirstOrDefaultAsync(a => a.TaskItemId == taskId && a.TagId == tagId, cancellationToken);
        if (assignment is null)
        {
            return TaskCoreResult<bool>.NotFound("La tarea no tiene esa etiqueta.");
        }
        _db.TaskItemTagAssignments.Remove(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<TaskItemActivityDto>> AddCommentAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return TaskCoreResult<TaskItemActivityDto>.Invalid("El comentario no puede estar vacio.");
        }
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemActivityDto>.NotFound("Tarea no encontrada.");
        }
        var activity = BuildActivity(task.TenantId, taskId, actorUserId, actorName, TaskActivityType.Comment, trimmed);
        _db.TaskItemActivities.Add(activity);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<TaskItemActivityDto>.Ok(
            new TaskItemActivityDto(activity.Id, activity.Type, activity.ActorName, activity.Text, activity.CreatedAt));
    }

    public async Task<TaskCoreResult<TaskItemAttachmentDto>> AddAttachmentAsync(AddTaskAttachmentRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var fileName = (request.FileName ?? "").Trim();
        var url = (request.Url ?? "").Trim();
        if (fileName.Length == 0 || url.Length == 0)
        {
            return TaskCoreResult<TaskItemAttachmentDto>.Invalid("Nombre de archivo y URL son obligatorios.");
        }
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TaskItemId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemAttachmentDto>.NotFound("Tarea no encontrada.");
        }

        var attachment = new TaskItemAttachment
        {
            TenantId = task.TenantId,
            TaskItemId = request.TaskItemId,
            FileName = fileName,
            Url = url,
            MimeType = Normalize(request.MimeType),
            SizeBytes = request.SizeBytes,
            UploadedBy = actorUserId,
            UploadedByName = actorName
        };
        _db.TaskItemAttachments.Add(attachment);
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, $"adjunto el archivo {fileName}"));
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<TaskItemAttachmentDto>.Ok(new TaskItemAttachmentDto(
            attachment.Id, attachment.FileName, attachment.Url, attachment.MimeType,
            attachment.SizeBytes, attachment.UploadedByName, attachment.CreatedAt));
    }

    public async Task<TaskCoreResult<bool>> DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _db.TaskItemAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken);
        if (attachment is null)
        {
            return TaskCoreResult<bool>.NotFound("Adjunto no encontrado.");
        }
        _db.TaskItemAttachments.Remove(attachment);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<TaskWorkLogDto>> AddWorkLogAsync(AddTaskWorkLogRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (request.Seconds <= 0 || request.Seconds > MaxWorkLogSeconds)
        {
            return TaskCoreResult<TaskWorkLogDto>.Invalid(
                $"Los segundos deben estar entre 1 y {MaxWorkLogSeconds} (24 horas).");
        }
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TaskItemId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskWorkLogDto>.NotFound("Tarea no encontrada.");
        }
        if (!await _db.TenantUsers.AnyAsync(u => u.Id == request.TenantUserId, cancellationToken))
        {
            return TaskCoreResult<TaskWorkLogDto>.Invalid("El usuario del worklog no pertenece al tenant.");
        }

        var workLog = new TaskWorkLog
        {
            TenantId = task.TenantId,
            TaskItemId = request.TaskItemId,
            TenantUserId = request.TenantUserId,
            Seconds = request.Seconds,
            Note = Normalize(request.Note),
            Kind = request.Kind,
            LoggedAt = request.LoggedAt ?? DateTimeOffset.UtcNow
        };
        _db.TaskWorkLogs.Add(workLog);
        if (request.LogActivity)
        {
            var minutes = Math.Max(1, request.Seconds / 60);
            _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
                TaskActivityType.Action, $"registro {minutes} minutos de trabajo"));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<TaskWorkLogDto>.Ok(new TaskWorkLogDto(
            workLog.Id, workLog.TaskItemId, workLog.TenantUserId, workLog.Seconds,
            workLog.Note, workLog.Kind, workLog.LoggedAt));
    }

    public async Task<IReadOnlyList<TaskWorkLogDto>> ListWorkLogsAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        return await _db.TaskWorkLogs.AsNoTracking()
            .Where(w => w.TaskItemId == taskId)
            .OrderByDescending(w => w.LoggedAt)
            .Select(w => new TaskWorkLogDto(w.Id, w.TaskItemId, w.TenantUserId, w.Seconds, w.Note, w.Kind, w.LoggedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<long> TotalSecondsAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        return await _db.TaskWorkLogs.AsNoTracking()
            .Where(w => w.TaskItemId == taskId)
            .SumAsync(w => (long)w.Seconds, cancellationToken);
    }

    public async Task<PagedResult<TaskItemSummaryDto>> ListAsync(TaskItemListFilter filter, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        // Filtros combinables AND, todos via LINQ parametrizado sobre el filtro global de tenant.
        var query = _db.TaskItems.AsNoTracking().AsQueryable();
        if (!filter.IncludeArchived)
        {
            query = query.Where(t => !t.IsArchived);
        }
        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToList();
            query = query.Where(t => statuses.Contains(t.Status));
        }
        if (filter.Priority is TaskPriority priority)
        {
            query = query.Where(t => t.Priority == priority);
        }
        if (filter.AssigneeTenantUserId is Guid assigneeId)
        {
            query = query.Where(t => t.AssigneeTenantUserId == assigneeId);
        }
        if (filter.ActivityTypeId is Guid activityTypeId)
        {
            query = query.Where(t => t.ActivityTypeId == activityTypeId);
        }
        if (filter.ProjectId is Guid projectId)
        {
            query = query.Where(t => t.ProjectId == projectId);
        }
        if (filter.TagIds is { Count: > 0 })
        {
            var tagIds = filter.TagIds.ToList();
            query = query.Where(t => _db.TaskItemTagAssignments.Any(a => a.TaskItemId == t.Id && tagIds.Contains(a.TagId)));
        }
        if (filter.DueFrom is DateTimeOffset dueFrom)
        {
            query = query.Where(t => t.DueDate != null && t.DueDate >= dueFrom);
        }
        if (filter.DueTo is DateTimeOffset dueTo)
        {
            query = query.Where(t => t.DueDate != null && t.DueDate <= dueTo);
        }
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            // ToLower en ambos lados: contains case-insensitive portable (PG es case-sensitive).
            var text = filter.Text.Trim().ToLowerInvariant();
            query = query.Where(t => t.Title.ToLower().Contains(text));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var summaries = await ToSummariesAsync(items, cancellationToken);
        return new PagedResult<TaskItemSummaryDto>(summaries, total, page, pageSize);
    }

    public async Task<TaskItemDetailDto?> GetDetailAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null) { return null; }

        var summary = await ToSummaryAsync(task, cancellationToken);
        var totalSeconds = await TotalSecondsAsync(taskId, cancellationToken);
        var recentActivity = await _db.TaskItemActivities.AsNoTracking()
            .Where(a => a.TaskItemId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(RecentActivityLimit)
            .Select(a => new TaskItemActivityDto(a.Id, a.Type, a.ActorName, a.Text, a.CreatedAt))
            .ToListAsync(cancellationToken);
        var attachments = await _db.TaskItemAttachments.AsNoTracking()
            .Where(a => a.TaskItemId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TaskItemAttachmentDto(a.Id, a.FileName, a.Url, a.MimeType, a.SizeBytes, a.UploadedByName, a.CreatedAt))
            .ToListAsync(cancellationToken);

        return new TaskItemDetailDto(summary, task.Description,
            task.RequesterName, task.RequesterEmail, task.RequesterPhone,
            DeserializeCcEmails(task.CcEmails), totalSeconds, recentActivity, attachments);
    }

    // ---- Helpers ----

    private async Task<TaskItemSummaryDto> ToSummaryAsync(TaskItem task, CancellationToken cancellationToken)
        => (await ToSummariesAsync([task], cancellationToken))[0];

    private async Task<IReadOnlyList<TaskItemSummaryDto>> ToSummariesAsync(IReadOnlyList<TaskItem> tasks, CancellationToken cancellationToken)
    {
        if (tasks.Count == 0) { return Array.Empty<TaskItemSummaryDto>(); }

        var taskIds = tasks.Select(t => t.Id).ToList();
        var tagsByTask = (await _db.TaskItemTagAssignments.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskItemId))
                .Join(_db.TaskItemTags.AsNoTracking(), a => a.TagId, t => t.Id,
                    (a, t) => new { a.TaskItemId, Tag = new TaskItemTagDto(t.Id, t.Name, t.Color) })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.TaskItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaskItemTagDto>)g.Select(x => x.Tag).OrderBy(t => t.Name).ToList());

        var activityTypeIds = tasks.Select(t => t.ActivityTypeId).Distinct().ToList();
        var activityTypeNames = await _db.ActivityTypes.AsNoTracking()
            .Where(t => activityTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.Category}/{t.Name}", cancellationToken);

        return tasks.Select(t => new TaskItemSummaryDto(
            t.Id, t.Number, t.Title, t.ActivityTypeId,
            activityTypeNames.TryGetValue(t.ActivityTypeId, out var name) ? name : null,
            t.Priority, t.Status, t.AssigneeTenantUserId, t.DueDate, t.ProjectId, t.Color,
            t.IsArchived, t.ClosedAt, t.Version, t.CreatedAt,
            tagsByTask.TryGetValue(t.Id, out var tags) ? tags : Array.Empty<TaskItemTagDto>())).ToList();
    }

    private static TaskItemActivity BuildActivity(Guid tenantId, Guid taskItemId, Guid? actorUserId, string actorName, TaskActivityType type, string text)
        => new()
        {
            TenantId = tenantId,
            TaskItemId = taskItemId,
            Type = type,
            ActorUserId = actorUserId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Sistema" : actorName.Trim(),
            Text = text
        };

    private static string? SerializeCcEmails(IReadOnlyList<string>? emails)
    {
        var cleaned = (emails ?? Array.Empty<string>())
            .Select(e => (e ?? "").Trim())
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return cleaned.Count == 0 ? null : JsonSerializer.Serialize(cleaned, JsonOptions);
    }

    private static IReadOnlyList<string> DeserializeCcEmails(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
