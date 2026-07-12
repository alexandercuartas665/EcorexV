using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Implementacion del gestor de tableros de actividades (ADR-0020). Las tarjetas son
/// TaskItem; la columna es UBICACION visual y el Status sigue gobernado por
/// TaskItemStateMachine (transicion oportunista a Done al mover a columna IsDone).
/// Todos los filtros se traducen a SQL (LINQ server-side) sobre el filtro global de tenant.
/// </summary>
public sealed class ActivityBoardService : IActivityBoardService
{
    /// <summary>Consecutivo de tableros de actividades: "PRY" -> "PRY-0001".</summary>
    public const string SequenceCode = "PRY";
    public const string SequencePrefix = "PRY-";
    public const int SequencePadding = 4;

    private const string ConflictMessage = "La tarea fue modificada por otro usuario. Recarga e intenta de nuevo.";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISequenceService _sequences;
    private readonly ITaskItemService _taskItems;
    private readonly IAuditWriter _audit;
    private readonly INodeAssigneeResolver _nodeAssignees;

    public ActivityBoardService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ISequenceService sequences,
        ITaskItemService taskItems,
        IAuditWriter audit,
        INodeAssigneeResolver nodeAssignees)
    {
        _db = db;
        _tenantContext = tenantContext;
        _sequences = sequences;
        _taskItems = taskItems;
        _audit = audit;
        _nodeAssignees = nodeAssignees;
    }

    // ---- Indice ----

    public async Task<ActivityBoardIndexDto> ListBoardsAsync(ActivityBoardIndexFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _db.TaskBoards.AsNoTracking()
            .Where(b => b.Kind == TaskBoardKind.Activities);
        if (!filter.IncludeArchived)
        {
            query = query.Where(b => !b.IsArchived);
        }
        // Filtros del indice, server-side y combinables AND (ADR-0020): miembro/etiqueta/tipo
        // miran las TAREAS del tablero; el rango de fechas mira el vencimiento DEL TABLERO.
        if (filter.MemberTenantUserId is Guid member)
        {
            query = query.Where(b => _db.TaskItems.Any(t => t.BoardId == b.Id && !t.IsArchived
                && (t.AssigneeTenantUserId == member
                    || _db.TaskItemAssignments.Any(a => a.TaskItemId == t.Id && a.TenantUserId == member))));
        }
        if (filter.TagId is Guid tagId)
        {
            query = query.Where(b => _db.TaskItems.Any(t => t.BoardId == b.Id && !t.IsArchived
                && _db.TaskItemTagAssignments.Any(a => a.TaskItemId == t.Id && a.TagId == tagId)));
        }
        if (filter.ActivityTypeId is Guid activityTypeId)
        {
            query = query.Where(b => _db.TaskItems.Any(t => t.BoardId == b.Id && !t.IsArchived
                && t.ActivityTypeId == activityTypeId));
        }
        if (filter.DueFrom is DateTimeOffset dueFrom)
        {
            query = query.Where(b => b.DueDate != null && b.DueDate >= dueFrom);
        }
        if (filter.DueTo is DateTimeOffset dueTo)
        {
            query = query.Where(b => b.DueDate != null && b.DueDate <= dueTo);
        }
        if (filter.HasDueDate is bool hasDue)
        {
            query = hasDue
                ? query.Where(b => b.DueDate != null)
                : query.Where(b => b.DueDate == null);
        }

        var boards = await query
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .ToListAsync(cancellationToken);
        if (boards.Count == 0)
        {
            return new ActivityBoardIndexDto(Array.Empty<ActivityBoardSummaryDto>(), new ActivityBoardKpisDto(0, 0, 0, 0));
        }

        var boardIds = boards.Select(b => b.Id).ToList();
        var columns = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.BoardId, c.Name, c.IsDone })
            .ToListAsync(cancellationToken);
        var doneColumnIds = columns.Where(c => c.IsDone).Select(c => c.Id).ToHashSet();

        var now = DateTimeOffset.UtcNow;
        // Una fila por tarea del indice: suficiente para progreso, KPIs y avatares.
        var tasks = await _db.TaskItems.AsNoTracking()
            .Where(t => t.BoardId != null && boardIds.Contains(t.BoardId.Value) && !t.IsArchived)
            .Select(t => new { BoardId = t.BoardId!.Value, t.Id, t.ColumnId, t.AssigneeTenantUserId, t.DueDate })
            .ToListAsync(cancellationToken);
        var taskIds = tasks.Select(t => t.Id).ToList();
        var boardByTask = tasks.ToDictionary(t => t.Id, t => t.BoardId);

        var checklist = taskIds.Count == 0
            ? []
            : await _db.TaskItemChecklistItems.AsNoTracking()
                .Where(i => taskIds.Contains(i.TaskItemId))
                .Select(i => new { i.TaskItemId, i.IsCompleted })
                .ToListAsync(cancellationToken);
        var checklistByBoard = checklist
            .GroupBy(i => boardByTask[i.TaskItemId])
            .ToDictionary(g => g.Key, g => (Done: g.Count(i => i.IsCompleted), Total: g.Count()));

        var assignmentRows = taskIds.Count == 0
            ? []
            : await _db.TaskItemAssignments.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskItemId))
                .Select(a => new { a.TaskItemId, a.TenantUserId })
                .ToListAsync(cancellationToken);

        // Miembros distintos por tablero: encargados + asignados M:N.
        var membersByBoard = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var t in tasks.Where(t => t.AssigneeTenantUserId is not null))
        {
            membersByBoard.TryAdd(t.BoardId, new HashSet<Guid>());
            membersByBoard[t.BoardId].Add(t.AssigneeTenantUserId!.Value);
        }
        foreach (var a in assignmentRows)
        {
            var boardId = boardByTask[a.TaskItemId];
            membersByBoard.TryAdd(boardId, new HashSet<Guid>());
            membersByBoard[boardId].Add(a.TenantUserId);
        }
        var memberInfos = await LoadMemberInfosAsync(
            membersByBoard.Values.SelectMany(s => s).Distinct().ToList(), cancellationToken);

        var summaries = new List<ActivityBoardSummaryDto>(boards.Count);
        int kpiTasks = 0, kpiCompleted = 0, kpiAtRisk = 0;
        foreach (var board in boards)
        {
            var boardTasks = tasks.Where(t => t.BoardId == board.Id).ToList();
            var inDone = boardTasks.Count(t => t.ColumnId is Guid col && doneColumnIds.Contains(col));
            checklistByBoard.TryGetValue(board.Id, out var check);
            var progress = ActivityBoardCalculations.BoardProgressPct(check.Done, check.Total, inDone, boardTasks.Count);
            var overdue = boardTasks.Any(t => t.DueDate is DateTimeOffset due && due < now
                && !(t.ColumnId is Guid c && doneColumnIds.Contains(c)));

            kpiTasks += boardTasks.Count;
            kpiCompleted += inDone;
            // KPI "en riesgo" (decision ADR-0020): tablero AtRisk o con tareas vencidas.
            if (board.Status == TaskBoardStatus.AtRisk || overdue) { kpiAtRisk++; }

            var members = membersByBoard.TryGetValue(board.Id, out var ids)
                ? ids.Select(id => memberInfos.TryGetValue(id, out var m) ? m : null)
                    .Where(m => m is not null).Select(m => m!)
                    .OrderBy(m => m.DisplayName).ToList()
                : (IReadOnlyList<ActivityBoardMemberDto>)Array.Empty<ActivityBoardMemberDto>();

            summaries.Add(new ActivityBoardSummaryDto(
                board.Id, board.Code, board.Name, board.Description, board.Color,
                board.Status, board.DueDate, board.IsArchived, board.SortOrder,
                columns.Where(c => c.BoardId == board.Id).Select(c => c.Name).ToList(),
                progress, boardTasks.Count, members));
        }

        return new ActivityBoardIndexDto(summaries,
            new ActivityBoardKpisDto(boards.Count, kpiTasks, kpiCompleted, kpiAtRisk));
    }

    // ---- CRUD de tableros ----

    public async Task<TaskCoreResult<ActivityBoardSummaryDto>> CreateBoardAsync(CreateActivityBoardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<ActivityBoardSummaryDto>.Invalid("No hay tenant activo.");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<ActivityBoardSummaryDto>.Invalid("El nombre del tablero es obligatorio.");
        }
        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        if (code is not null && code.Length > 20)
        {
            return TaskCoreResult<ActivityBoardSummaryDto>.Invalid("El codigo supera 20 caracteres.");
        }
        if (code is not null && await _db.TaskBoards.AnyAsync(b => b.Code == code, cancellationToken))
        {
            return TaskCoreResult<ActivityBoardSummaryDto>.Invalid($"Ya existe un tablero con el codigo '{code}'.");
        }

        // Consecutivo PRY-#### si no viene codigo: misma coreografia de TaskItemService
        // (fila asegurada fuera de la transaccion; emision adentro, rollback devuelve el numero).
        if (code is null)
        {
            await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);
        }
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        code ??= await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);

        var nextOrder = (await _db.TaskBoards.Select(b => (int?)b.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var board = new TaskBoard
        {
            TenantId = tenantId,
            Kind = TaskBoardKind.Activities,
            Code = code,
            Name = name,
            Description = Normalize(request.Description),
            Color = Normalize(request.Color),
            Status = request.Status,
            DueDate = request.DueDate,
            SortOrder = nextOrder
        };
        _db.TaskBoards.Add(board);

        // Columnas default del prototipo (mismo patron/valores de TaskBoardService).
        for (int i = 0; i < TaskBoardService.DefaultColumns.Length; i++)
        {
            var (cname, ccolor, isDone) = TaskBoardService.DefaultColumns[i];
            _db.TaskBoardColumns.Add(new TaskBoardColumn
            {
                TenantId = tenantId,
                BoardId = board.Id,
                Name = cname,
                Color = ccolor,
                SortOrder = i,
                IsDone = isDone
            });
        }

        _audit.Write(actorUserId, "activity-board.create", nameof(TaskBoard), board.Id,
            previousValue: null, newValue: new { board.Code, board.Name }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TaskCoreResult<ActivityBoardSummaryDto>.Ok(new ActivityBoardSummaryDto(
            board.Id, board.Code, board.Name, board.Description, board.Color,
            board.Status, board.DueDate, board.IsArchived, board.SortOrder,
            TaskBoardService.DefaultColumns.Select(c => c.Name).ToList(),
            0, 0, Array.Empty<ActivityBoardMemberDto>()));
    }

    public async Task<TaskCoreResult<ActivityBoardSummaryDto>> UpdateBoardAsync(Guid boardId, UpdateActivityBoardRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards
            .FirstOrDefaultAsync(b => b.Id == boardId && b.Kind == TaskBoardKind.Activities, cancellationToken);
        if (board is null)
        {
            return TaskCoreResult<ActivityBoardSummaryDto>.NotFound("Tablero de actividades no encontrado.");
        }
        var name = (request.Name ?? board.Name).Trim();
        if (name.Length == 0)
        {
            return TaskCoreResult<ActivityBoardSummaryDto>.Invalid("El nombre del tablero es obligatorio.");
        }

        board.Name = name;
        board.Description = Normalize(request.Description);
        board.Color = Normalize(request.Color);
        board.Status = request.Status;
        board.DueDate = request.DueDate;
        board.IsArchived = request.IsArchived;
        await _db.SaveChangesAsync(cancellationToken);

        var columnNames = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);
        return TaskCoreResult<ActivityBoardSummaryDto>.Ok(new ActivityBoardSummaryDto(
            board.Id, board.Code, board.Name, board.Description, board.Color,
            board.Status, board.DueDate, board.IsArchived, board.SortOrder,
            columnNames, 0, 0, Array.Empty<ActivityBoardMemberDto>()));
    }

    public async Task<TaskCoreResult<bool>> DeleteBoardAsync(Guid boardId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards
            .FirstOrDefaultAsync(b => b.Id == boardId && b.Kind == TaskBoardKind.Activities, cancellationToken);
        if (board is null)
        {
            return TaskCoreResult<bool>.NotFound("Tablero de actividades no encontrado.");
        }

        // Las FKs TaskItem->board/columna son NO ACTION a proposito: desacoplar primero
        // (las tareas sobreviven fuera del tablero), luego borrar tablero + columnas.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        await _db.TaskItems
            .Where(t => t.BoardId == boardId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.BoardId, (Guid?)null)
                .SetProperty(t => t.ColumnId, (Guid?)null)
                .SetProperty(t => t.BoardSortOrder, 0), cancellationToken);
        _db.TaskBoards.Remove(board);
        _audit.Write(actorUserId, "activity-board.delete", nameof(TaskBoard), board.Id,
            previousValue: new { board.Code, board.Name }, newValue: null, tenantId: board.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    // ---- Detalle con filtros ----

    public async Task<TaskCoreResult<ActivityBoardDetailDto>> GetBoardDetailAsync(Guid boardId, ActivityBoardDetailFilter filter, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId && b.Kind == TaskBoardKind.Activities, cancellationToken);
        if (board is null)
        {
            return TaskCoreResult<ActivityBoardDetailDto>.NotFound("Tablero de actividades no encontrado.");
        }
        if (filter.Scope == ActivityBoardScope.Mine && filter.CurrentTenantUserId is null)
        {
            return TaskCoreResult<ActivityBoardDetailDto>.Invalid("El alcance 'mias' requiere el usuario actual.");
        }

        var columns = await _db.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        // Query base filtrada (sin alcance): sobre ella se calculan los contadores por alcance.
        var query = _db.TaskItems.AsNoTracking()
            .Where(t => t.BoardId == boardId && !t.IsArchived);
        if (filter.ColumnIds is { Count: > 0 })
        {
            var columnIds = filter.ColumnIds.ToList();
            query = query.Where(t => t.ColumnId != null && columnIds.Contains(t.ColumnId.Value));
        }
        if (filter.AssigneeTenantUserIds is { Count: > 0 })
        {
            // Asignado = encargado O miembro M:N (chips de avatar del prototipo).
            var assigneeIds = filter.AssigneeTenantUserIds.ToList();
            query = query.Where(t =>
                (t.AssigneeTenantUserId != null && assigneeIds.Contains(t.AssigneeTenantUserId.Value))
                || _db.TaskItemAssignments.Any(a => a.TaskItemId == t.Id && assigneeIds.Contains(a.TenantUserId)));
        }
        if (ActivityBoardCalculations.DueRangeUtc(filter.Due, filter.DueOn, DateTimeOffset.UtcNow)
            is (DateTimeOffset from, DateTimeOffset to))
        {
            query = query.Where(t => t.DueDate != null && t.DueDate >= from && t.DueDate < to);
        }
        if (filter.TagIds is { Count: > 0 })
        {
            var tagIds = filter.TagIds.ToList();
            query = query.Where(t => _db.TaskItemTagAssignments.Any(a => a.TaskItemId == t.Id && tagIds.Contains(a.TagId)));
        }

        // Contadores por alcance (con los demas filtros aplicados, ignorando el alcance).
        // ADR-0038: el alcance Mine incluye ademas las tareas con paso de flujo ruteado al usuario;
        // el set se precomputa (la candidatura por cargo no es SQL) y se reusa en conteo y listado.
        var routedTaskIds = filter.CurrentTenantUserId is Guid meRouted
            ? await GetRoutedTaskIdsAsync(meRouted, cancellationToken)
            : Array.Empty<Guid>();
        var noRoute = Array.Empty<Guid>();

        var teamCount = await query.CountAsync(cancellationToken);
        var mineCount = filter.CurrentTenantUserId is Guid me
            ? await ApplyScope(query, ActivityBoardScope.Mine, me, routedTaskIds).CountAsync(cancellationToken)
            : 0;
        var unassignedCount = await ApplyScope(query, ActivityBoardScope.Unassigned, null, noRoute).CountAsync(cancellationToken);

        var tasks = await ApplyScope(query, filter.Scope, filter.CurrentTenantUserId, routedTaskIds)
            .OrderBy(t => t.BoardSortOrder).ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        var taskIds = tasks.Select(t => t.Id).ToList();

        // Cargas por lote de todo lo que pinta la tarjeta.
        var checklistStats = taskIds.Count == 0
            ? new Dictionary<Guid, (int Done, int Total)>()
            : (await _db.TaskItemChecklistItems.AsNoTracking()
                .Where(i => taskIds.Contains(i.TaskItemId))
                .GroupBy(i => i.TaskItemId)
                .Select(g => new { g.Key, Done = g.Count(i => i.IsCompleted), Total = g.Count() })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.Key, x => (x.Done, x.Total));
        var attachmentCounts = taskIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _db.TaskItemAttachments.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskItemId))
                .GroupBy(a => a.TaskItemId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var commentCounts = taskIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _db.TaskItemActivities.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskItemId) && a.Type == TaskActivityType.Comment)
                .GroupBy(a => a.TaskItemId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var tagsByTask = taskIds.Count == 0
            ? new Dictionary<Guid, List<TaskItemTagDto>>()
            : (await _db.TaskItemTagAssignments.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskItemId))
                .Join(_db.TaskItemTags.AsNoTracking(), a => a.TagId, t => t.Id,
                    (a, t) => new { a.TaskItemId, Tag = new TaskItemTagDto(t.Id, t.Name, t.Color) })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.TaskItemId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Tag).OrderBy(t => t.Name).ToList());
        var assignmentsByTask = taskIds.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _db.TaskItemAssignments.AsNoTracking()
                .Where(a => taskIds.Contains(a.TaskItemId))
                .Select(a => new { a.TaskItemId, a.TenantUserId })
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.TaskItemId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.TenantUserId).ToList());

        var memberIds = tasks.Where(t => t.AssigneeTenantUserId is not null)
            .Select(t => t.AssigneeTenantUserId!.Value)
            .Concat(assignmentsByTask.Values.SelectMany(v => v))
            .Distinct().ToList();
        var memberInfos = await LoadMemberInfosAsync(memberIds, cancellationToken);

        var columnDtos = columns.Select(column =>
        {
            var cards = tasks.Where(t => t.ColumnId == column.Id).Select(t =>
            {
                checklistStats.TryGetValue(t.Id, out var check);
                var teamAssignees = assignmentsByTask.TryGetValue(t.Id, out var ids)
                    ? ids.Select(id => memberInfos.TryGetValue(id, out var m) ? m : null)
                        .Where(m => m is not null).Select(m => m!)
                        .OrderBy(m => m.DisplayName).ToList()
                    : (IReadOnlyList<ActivityBoardMemberDto>)Array.Empty<ActivityBoardMemberDto>();
                return new ActivityCardDto(
                    t.Id, t.Number, t.Title, t.Description, t.Priority, t.Status,
                    t.StartDate, t.DueDate,
                    check.Done, check.Total, ActivityBoardCalculations.Pct(check.Done, check.Total),
                    // Color de progreso derivado de la COLUMNA (regla visual del prototipo).
                    column.Color,
                    t.AssigneeTenantUserId is Guid owner && memberInfos.TryGetValue(owner, out var ownerDto) ? ownerDto : null,
                    teamAssignees,
                    attachmentCounts.TryGetValue(t.Id, out var att) ? att : 0,
                    commentCounts.TryGetValue(t.Id, out var com) ? com : 0,
                    tagsByTask.TryGetValue(t.Id, out var tags) ? tags : Array.Empty<TaskItemTagDto>(),
                    column.Id, t.BoardSortOrder, t.Version, t.CreatedAt);
            }).ToList();
            return new ActivityBoardColumnDto(column.Id, column.Name, column.Color, column.SortOrder, column.IsDone, cards);
        }).ToList();

        return TaskCoreResult<ActivityBoardDetailDto>.Ok(new ActivityBoardDetailDto(
            board.Id, board.Code, board.Name, board.Description, board.Status, board.DueDate,
            board.IsArchived, columnDtos,
            new ActivityScopeCountersDto(teamCount, mineCount, unassignedCount)));
    }

    // ---- Movimiento de tarjetas ----

    public async Task<TaskCoreResult<MoveTaskResultDto>> MoveTaskAsync(Guid taskItemId, Guid targetColumnId, int sortOrder, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskItemId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<MoveTaskResultDto>.NotFound("Tarea no encontrada.");
        }
        if (task.BoardId is not Guid boardId)
        {
            return TaskCoreResult<MoveTaskResultDto>.Invalid("La tarea no esta en un tablero.");
        }
        var column = await _db.TaskBoardColumns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == targetColumnId, cancellationToken);
        if (column is null || column.BoardId != boardId)
        {
            return TaskCoreResult<MoveTaskResultDto>.Invalid("La columna destino no pertenece al tablero de la tarea.");
        }

        // Reorden estable (ola 3): sortOrder es el INDICE DE DROP dentro de la columna
        // destino. Se re-secuencia toda la columna en memoria (insercion en el indice,
        // clampada) para que BoardSortOrder quede denso y el orden sea deterministico,
        // tambien en el reorden INTRA columna. Un solo SaveChanges: atomico.
        var siblings = await _db.TaskItems
            .Where(t => t.ColumnId == targetColumnId && !t.IsArchived && t.Id != task.Id)
            .OrderBy(t => t.BoardSortOrder).ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        var sameColumn = task.ColumnId == targetColumnId;
        var insertAt = Math.Clamp(sortOrder, 0, siblings.Count);
        siblings.Insert(insertAt, task);
        task.ColumnId = targetColumnId;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].BoardSortOrder != i) { siblings[i].BoardSortOrder = i; }
        }
        if (!sameColumn)
        {
            _db.TaskItemActivities.Add(new TaskItemActivity
            {
                TenantId = task.TenantId,
                TaskItemId = task.Id,
                Type = TaskActivityType.Action,
                ActorUserId = actorUserId,
                ActorName = string.IsNullOrWhiteSpace(actorName) ? "Sistema" : actorName.Trim(),
                Text = $"movio la tarea a la columna '{column.Name}'"
            });
        }

        // Transicion OPORTUNISTA a Done al llegar a una columna final (ADR-0020): la
        // columna es ubicacion, el estado lo gobierna TaskItemStateMachine. Si la maquina
        // no permite Status -> Done, la tarjeta se mueve igual y se reporta el motivo.
        var statusChanged = false;
        string? statusNote = null;
        if (column.IsDone && task.Status is not (TaskItemStatus.Done or TaskItemStatus.Closed))
        {
            if (TaskItemStateMachine.CanTransition(task.Status, TaskItemStatus.Done))
            {
                var previous = task.Status;
                task.Status = TaskItemStatus.Done;
                statusChanged = true;
                _db.TaskItemActivities.Add(new TaskItemActivity
                {
                    TenantId = task.TenantId,
                    TaskItemId = task.Id,
                    Type = TaskActivityType.Action,
                    ActorUserId = actorUserId,
                    ActorName = string.IsNullOrWhiteSpace(actorName) ? "Sistema" : actorName.Trim(),
                    Text = $"cambio el estado de {previous} a {TaskItemStatus.Done} al mover a '{column.Name}'"
                });
            }
            else
            {
                statusNote = $"La tarjeta se movio, pero la transicion {task.Status} -> Done no esta permitida; el estado no cambio.";
            }
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<MoveTaskResultDto>.Conflict(ConflictMessage);
        }
        return TaskCoreResult<MoveTaskResultDto>.Ok(new MoveTaskResultDto(
            task.Id, targetColumnId, task.BoardSortOrder, column.IsDone, statusChanged, task.Status, statusNote));
    }

    public async Task<TaskCoreResult<bool>> AddTaskToBoardAsync(Guid taskItemId, Guid boardId, Guid? columnId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskItemId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<bool>.NotFound("Tarea no encontrada.");
        }
        if (task.BoardId is not null)
        {
            return TaskCoreResult<bool>.Invalid("La tarea ya esta en un tablero. Sacala primero.");
        }
        var board = await _db.TaskBoards.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId && b.Kind == TaskBoardKind.Activities, cancellationToken);
        if (board is null)
        {
            return TaskCoreResult<bool>.NotFound("Tablero de actividades no encontrado.");
        }
        Guid targetColumnId;
        if (columnId is Guid requested)
        {
            if (!await _db.TaskBoardColumns.AnyAsync(c => c.Id == requested && c.BoardId == boardId, cancellationToken))
            {
                return TaskCoreResult<bool>.Invalid("La columna no pertenece al tablero.");
            }
            targetColumnId = requested;
        }
        else
        {
            var first = await _db.TaskBoardColumns.AsNoTracking()
                .Where(c => c.BoardId == boardId)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (first is not Guid firstColumnId)
            {
                return TaskCoreResult<bool>.Invalid("El tablero no tiene columnas.");
            }
            targetColumnId = firstColumnId;
        }

        task.BoardId = boardId;
        task.ColumnId = targetColumnId;
        task.BoardSortOrder = (await _db.TaskItems
            .Where(t => t.ColumnId == targetColumnId)
            .Select(t => (int?)t.BoardSortOrder)
            .MaxAsync(cancellationToken) ?? -1) + 1;
        _db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = task.TenantId,
            TaskItemId = task.Id,
            Type = TaskActivityType.Action,
            ActorUserId = actorUserId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Sistema" : actorName.Trim(),
            Text = $"agrego la tarea al tablero '{board.Name}'"
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<bool>.Conflict(ConflictMessage);
        }
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<bool>> RemoveFromBoardAsync(Guid taskItemId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskItemId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<bool>.NotFound("Tarea no encontrada.");
        }
        if (task.BoardId is null)
        {
            return TaskCoreResult<bool>.Invalid("La tarea no esta en un tablero.");
        }
        var boardName = await _db.TaskBoards.AsNoTracking()
            .Where(b => b.Id == task.BoardId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken);

        task.BoardId = null;
        task.ColumnId = null;
        task.BoardSortOrder = 0;
        _db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = task.TenantId,
            TaskItemId = task.Id,
            Type = TaskActivityType.Action,
            ActorUserId = actorUserId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Sistema" : actorName.Trim(),
            Text = $"saco la tarea del tablero '{boardName ?? "(desconocido)"}'"
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<bool>.Conflict(ConflictMessage);
        }
        return TaskCoreResult<bool>.Ok(true);
    }

    // ---- Creacion rapida ----

    public async Task<TaskCoreResult<TaskItemDetailDto>> QuickCreateTaskAsync(QuickCreateTaskRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var board = await _db.TaskBoards.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BoardId && b.Kind == TaskBoardKind.Activities, cancellationToken);
        if (board is null)
        {
            return TaskCoreResult<TaskItemDetailDto>.NotFound("Tablero de actividades no encontrado.");
        }
        if (!await _db.TaskBoardColumns.AnyAsync(c => c.Id == request.ColumnId && c.BoardId == request.BoardId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("La columna no pertenece al tablero.");
        }

        // Ola 6 (crear-desde-tablero): si viene un CONCEPTO (subcategoria, tipicamente SIN proceso)
        // se clasifica por el; si no, se cae al ActivityType (default = primer tipo activo, ADR-0020).
        Guid? activityTypeId = null;
        if (request.SubcategoriaId is null)
        {
            activityTypeId = request.ActivityTypeId
                ?? await _db.ActivityTypes.AsNoTracking()
                    .Where(t => !t.IsArchived)
                    .OrderBy(t => t.SortOrder).ThenBy(t => t.Category).ThenBy(t => t.Name)
                    .Select(t => (Guid?)t.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            if (activityTypeId is null)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El tenant no tiene tipos de actividad activos.");
            }
        }

        // Delegar en CreateAsync: consecutivo T + etiquetas + actividad + flujo del concepto/tipo,
        // TODO en una sola transaccion, con la tarea ya colgada del board/columna.
        return await _taskItems.CreateAsync(new CreateTaskItemRequest(
            Title: request.Title,
            ActivityTypeId: activityTypeId,
            Description: request.Description,
            Priority: request.Priority,
            AssigneeTenantUserId: request.AssigneeTenantUserId,
            DueDate: request.DueDate,
            TagIds: request.TagIds,
            StartDate: request.StartDate,
            BoardId: request.BoardId,
            ColumnId: request.ColumnId,
            SubcategoriaId: request.SubcategoriaId), actorUserId, actorName, cancellationToken);
    }

    // ---- Helpers ----

    /// <summary>
    /// Alcances del prototipo, en SQL: Mine = encargado O asignado M:N el usuario actual O tarea con
    /// PASO ACTUAL del flujo ruteado a mi (ADR-0038: el tablero es la bandeja; `routedTaskIds` se
    /// precomputa fuera del query porque la candidatura por cargo no es SQL puro); Unassigned = sin
    /// encargado Y sin asignados; Team = todas.
    /// </summary>
    private IQueryable<TaskItem> ApplyScope(IQueryable<TaskItem> query, ActivityBoardScope scope,
        Guid? currentTenantUserId, IReadOnlyList<Guid> routedTaskIds)
        => scope switch
        {
            ActivityBoardScope.Mine when currentTenantUserId is Guid me
                => query.Where(t => t.AssigneeTenantUserId == me
                    || _db.TaskItemAssignments.Any(a => a.TaskItemId == t.Id && a.TenantUserId == me)
                    || routedTaskIds.Contains(t.Id)),
            ActivityBoardScope.Unassigned
                => query.Where(t => t.AssigneeTenantUserId == null
                    && !_db.TaskItemAssignments.Any(a => a.TaskItemId == t.Id)),
            _ => query
        };

    /// <summary>
    /// ADR-0038: task-ids cuyo PASO ACTUAL del flujo (current+Pending de una instancia Running ligada
    /// a la tarea) esta ruteado al usuario: asignado directo, o (sin reclamar y nodo Task) el usuario
    /// es candidato de la policy del nodo. La candidatura por cargo se resuelve en memoria via
    /// INodeAssigneeResolver (traversal del organigrama, no SQL); se cachea por nodo.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> GetRoutedTaskIdsAsync(Guid me, CancellationToken cancellationToken)
    {
        var rows = await (
            from step in _db.WorkflowStepHistories.AsNoTracking()
            where step.IsCurrent && step.Status == WorkflowStepStatus.Pending
            join instance in _db.WorkflowInstances.AsNoTracking()
                on step.InstanceId equals instance.Id
            where instance.Status == WorkflowInstanceStatus.Running && instance.TaskItemId != null
            join node in _db.WorkflowNodes.AsNoTracking()
                on step.NodeId equals node.Id
            select new
            {
                TaskItemId = instance.TaskItemId!.Value,
                step.AssignedToTenantUserId,
                NodeId = node.Id,
                node.NodeType
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0) { return []; }

        var result = new HashSet<Guid>();
        var candidateByNode = new Dictionary<Guid, bool>();
        foreach (var r in rows)
        {
            if (r.AssignedToTenantUserId == me) { result.Add(r.TaskItemId); continue; }
            if (r.AssignedToTenantUserId is null && r.NodeType == WorkflowNodeType.Task)
            {
                if (!candidateByNode.TryGetValue(r.NodeId, out var isCandidate))
                {
                    var candidates = await _nodeAssignees.ResolveCandidatesAsync(r.NodeId, cancellationToken);
                    isCandidate = candidates.Contains(me);
                    candidateByNode[r.NodeId] = isCandidate;
                }
                if (isCandidate) { result.Add(r.TaskItemId); }
            }
        }
        return result.ToList();
    }

    /// <summary>Carga TenantUser + DisplayName del PlatformUser para los avatares.</summary>
    private async Task<Dictionary<Guid, ActivityBoardMemberDto>> LoadMemberInfosAsync(
        IReadOnlyList<Guid> tenantUserIds, CancellationToken cancellationToken)
    {
        if (tenantUserIds.Count == 0) { return new Dictionary<Guid, ActivityBoardMemberDto>(); }
        var users = await _db.TenantUsers.AsNoTracking()
            .Where(u => tenantUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.PlatformUserId })
            .ToListAsync(cancellationToken);
        var platformIds = users.Select(u => u.PlatformUserId).Distinct().ToList();
        var displayNames = await _db.PlatformUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(p => platformIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName ?? p.Email, cancellationToken);
        return users.ToDictionary(u => u.Id, u =>
        {
            var name = displayNames.TryGetValue(u.PlatformUserId, out var n) ? n : u.Email;
            return new ActivityBoardMemberDto(u.Id, MemberInitials.From(name), name);
        });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
