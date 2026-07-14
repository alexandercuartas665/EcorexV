using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Notifications;
using Ecorex.Application.Organization;
using Ecorex.Application.Workflows;
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
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IEmailSender _emailSender;
    private readonly INotificationBroadcaster? _notificationBroadcaster;
    // Ola A3: expande el cargo del nodo -> usuarios candidatos. Con esto el primer paso del flujo
    // nace ASIGNADO (no colgando) y se revalida en servidor que el encargado ocupe ese cargo (D2).
    private readonly INodeAssigneeResolver _nodeAssignees;

    public TaskItemService(IApplicationDbContext db, ITenantContext tenantContext, ISequenceService sequences,
        IWorkflowEngine workflowEngine, IEmailSender emailSender, INodeAssigneeResolver nodeAssignees,
        INotificationBroadcaster? notificationBroadcaster = null)
    {
        _nodeAssignees = nodeAssignees;
        _db = db;
        _tenantContext = tenantContext;
        _sequences = sequences;
        _workflowEngine = workflowEngine;
        _emailSender = emailSender;
        _notificationBroadcaster = notificationBroadcaster;
    }

    public async Task<TaskCoreResult<TaskItemDetailDto>> CreateAsync(CreateTaskItemRequest request, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("No hay tenant activo.");
        }
        // El titulo puede venir vacio si el concepto define TituloAuto (se resuelve mas abajo,
        // cuando ya se cargo la subcategoria).
        var title = (request.Title ?? "").Trim();

        // Clasificacion (D1): la tarea se clasifica por concepto (subcategoria) y/o por el
        // ActivityType legacy. Debe venir al menos uno.
        if (request.ActivityTypeId is null && request.SubcategoriaId is null)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("Indica un concepto (subcategoria) o un tipo de actividad.");
        }

        // Validaciones de pertenencia al tenant: el filtro global oculta filas de otros
        // tenants, asi que un id ajeno simplemente "no existe".
        ActivityType? activityType = null;
        if (request.ActivityTypeId is Guid activityTypeId)
        {
            activityType = await _db.ActivityTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == activityTypeId, cancellationToken);
            if (activityType is null)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El tipo de actividad no existe en el tenant.");
            }
            if (activityType.IsArchived)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El tipo de actividad esta archivado.");
            }
        }

        // Concepto (subcategoria): valida; de el se derivan tablero/columna (Ola 1) y, mas abajo,
        // el arranque de flujo + titulo/detalle automaticos + notificaciones (Ola 2).
        ActividadSubcategoria? subcategoria = null;
        if (request.SubcategoriaId is Guid subcategoriaId)
        {
            subcategoria = await _db.ActividadSubcategorias.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == subcategoriaId, cancellationToken);
            if (subcategoria is null)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El concepto (subcategoria) no existe en el tenant.");
            }
            if (subcategoria.IsArchived)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El concepto (subcategoria) esta archivado.");
            }
        }

        // Ola 2 (RQ07): titulo y detalle automaticos del concepto cuando el alta no los trae.
        // Soporta tokens basicos (@cliente). Los flags de inicio (IniciaModulo) habilitan esto.
        if (title.Length == 0 && subcategoria?.TituloAuto is { Length: > 0 } tituloAuto)
        {
            title = RenderConceptTemplate(tituloAuto, request).Trim();
        }
        if (title.Length == 0)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El titulo es obligatorio.");
        }
        var description = Normalize(request.Description)
            ?? (subcategoria?.DetalleAuto is { Length: > 0 } detalleAuto
                ? Normalize(RenderConceptTemplate(detalleAuto, request))
                : null);

        if (request.EntidadId is Guid entidadId
            && !await _db.Entidades.AnyAsync(e => e.Id == entidadId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("La entidad (Empresa/Area) no existe en el tenant.");
        }
        if (request.ProjectId is Guid projectId
            && !await _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El proyecto no existe en el tenant.");
        }
        // Proyectos P3: el hito debe pertenecer al proyecto indicado.
        if (request.MilestoneId is Guid milestoneId)
        {
            if (request.ProjectId is not Guid milestoneProjectId)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El hito requiere indicar el proyecto.");
            }
            if (!await _db.ProjectMilestones.AnyAsync(m => m.Id == milestoneId && m.ProjectId == milestoneProjectId, cancellationToken))
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El hito no pertenece al proyecto indicado.");
            }
        }
        TenantUser? assignee = null;
        if (request.AssigneeTenantUserId is Guid assigneeId)
        {
            assignee = await _db.TenantUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == assigneeId, cancellationToken);
            if (assignee is null)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El asignado no pertenece al tenant.");
            }
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

        // ADR-0020: cuelgue opcional en un tablero de actividades. ColumnId debe pertenecer
        // al BoardId; sin ColumnId se usa la primera columna del tablero.
        Guid? boardId = request.BoardId;
        Guid? columnId = request.ColumnId;
        // Ola 1: si el request no fijo tablero y el concepto tiene uno, se hereda del concepto.
        // Solo el tablero: la columna inicial la resuelve el bloque de abajo (primera columna),
        // NO la TaskBoardColumnId del concepto (que marca "terminado", no el inicio).
        if (boardId is null && subcategoria?.TaskBoardId is Guid conceptBoardId)
        {
            boardId = conceptBoardId;
        }
        if (columnId is not null && boardId is null)
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("ColumnId requiere BoardId.");
        }
        if (boardId is Guid targetBoardId)
        {
            var board = await _db.TaskBoards.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == targetBoardId, cancellationToken);
            if (board is null || board.Kind != TaskBoardKind.Activities)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El tablero de actividades no existe en el tenant.");
            }
            if (columnId is Guid targetColumnId)
            {
                if (!await _db.TaskBoardColumns.AnyAsync(c => c.Id == targetColumnId && c.BoardId == targetBoardId, cancellationToken))
                {
                    return TaskCoreResult<TaskItemDetailDto>.Invalid("La columna no pertenece al tablero.");
                }
            }
            else
            {
                columnId = await _db.TaskBoardColumns.AsNoTracking()
                    .Where(c => c.BoardId == targetBoardId)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (columnId is null)
                {
                    return TaskCoreResult<TaskItemDetailDto>.Invalid("El tablero no tiene columnas.");
                }
            }
        }

        // Fila del consecutivo asegurada ANTES de la transaccion: una carrera de creacion
        // (violacion de unicidad) no debe abortar la transaccion principal (PostgreSQL).
        await _sequences.EnsureSequenceAsync(SequenceCode, cancellationToken);

        // Transaccion atomica: consecutivo + tarea + etiquetas + actividad "creo la tarea".
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        var number = await _sequences.NextAsync(SequenceCode, SequencePrefix, SequencePadding, cancellationToken);

        // Posicion al final de la columna destino (dentro de la transaccion).
        var boardSortOrder = 0;
        if (columnId is Guid placedColumnId)
        {
            boardSortOrder = (await _db.TaskItems
                .Where(t => t.ColumnId == placedColumnId)
                .Select(t => (int?)t.BoardSortOrder)
                .MaxAsync(cancellationToken) ?? -1) + 1;
        }

        var task = new TaskItem
        {
            TenantId = tenantId,
            Number = number,
            Title = title,
            Description = description,
            ActivityTypeId = request.ActivityTypeId,
            SubcategoriaId = request.SubcategoriaId,
            EntidadId = request.EntidadId,
            Priority = request.Priority,
            // Estado inicial: Pending; Active si nace asignada.
            Status = request.AssigneeTenantUserId is null ? TaskItemStatus.Pending : TaskItemStatus.Active,
            AssigneeTenantUserId = request.AssigneeTenantUserId,
            DueDate = request.DueDate,
            StartDate = request.StartDate,
            BoardId = boardId,
            ColumnId = columnId,
            BoardSortOrder = boardSortOrder,
            RequesterName = Normalize(request.RequesterName),
            RequesterEmail = Normalize(request.RequesterEmail),
            RequesterPhone = Normalize(request.RequesterPhone),
            CcEmails = SerializeCcEmails(request.CcEmails),
            ProjectId = request.ProjectId,
            MilestoneId = request.MilestoneId,
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

        // Entrega de notificaciones al CREAR (mismo criterio que AssignAsync, Ola 7): si la tarea NACE
        // asignada se notifica al encargado (in-app + email + badge); y a los destinatarios que el
        // concepto tenga configurados. Las filas in-app quedan dentro de la MISMA transaccion; el email
        // y la difusion SignalR son best-effort tras el commit.
        IReadOnlyList<(Guid Id, string Email)> conceptRecipients = Array.Empty<(Guid, string)>();
        if (assignee is not null)
        {
            _db.TaskItemActivities.Add(BuildActivity(tenantId, task.Id, actorUserId, actorName,
                TaskActivityType.Action, $"notifico a {assignee.Email}: le asignaron la tarea {number}"));
            _db.Notifications.Add(BuildNotification(tenantId, assignee.Id, NotificationKind.TaskAssigned,
                title: $"Te asignaron la tarea {number}",
                body: string.IsNullOrWhiteSpace(title) ? $"Tarea {number}" : title,
                actorName: actorName, relatedTaskItemId: task.Id));
        }
        if (subcategoria is not null)
        {
            conceptRecipients = await AddConceptNotificationAsync(tenantId, task.Id, subcategoria.Id,
                actorUserId, actorName, excludeEmail: assignee?.Email, cancellationToken: cancellationToken);
        }
        if (assignee is not null || conceptRecipients.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        // FASE 4 / Ola 2 (ADR-0014): arranca el flujo PUBLICADO del CONCEPTO (preferente) o, si no,
        // del ActivityType legacy, dentro de la MISMA transaccion (el motor detecta la transaccion
        // abierta y se une; un fallo revierte tambien la tarea). El primer paso queda ruteado por
        // cargo y visible en el TABLERO ("mis pendientes") + en el detalle de la tarea (ADR-0038).
        var workflowDefinitionId = subcategoria?.WorkflowDefinitionId ?? activityType?.WorkflowDefinitionId;
        if (workflowDefinitionId is Guid wfDefId
            && await _db.WorkflowDefinitions.AnyAsync(
                d => d.Id == wfDefId && d.IsPublished && !d.IsArchived, cancellationToken))
        {
            var started = await _workflowEngine.StartInstanceAsync(
                wfDefId, task.Id, actorUserId, actorName, cancellationToken);
            if (started.Status is not (WorkflowEngineStatus.Ok or WorkflowEngineStatus.StuckDetected))
            {
                await transaction.RollbackAsync(cancellationToken);
                return TaskCoreResult<TaskItemDetailDto>.Invalid(
                    $"No se pudo iniciar el flujo del concepto/tipo de actividad: {started.Error}");
            }

            // Ola A3: el paso que quedo activo NO debe nacer colgando. Hasta ahora AddStep lo dejaba
            // sin AssignedToTenantUserId y la asignacion se resolvia perezosamente cuando alguien lo
            // "reclamaba"; el candidato no se enteraba de nada. Aqui, en la MISMA transaccion:
            //   1. se revalida en SERVIDOR que el encargado ocupe el cargo del nodo (D2: el flujo
            //      manda; restringir el combo del wizard no basta, un API podria saltarselo);
            //   2. se FIJA el asignado del paso;
            //   3. la notificacion al encargado ya la emitio el bloque de arriba (TaskAssigned).
            var currentStep = task.WorkflowInstanceId is Guid instanceId
                ? await _db.WorkflowStepHistories
                    .Where(s => s.InstanceId == instanceId
                        && s.IsCurrent
                        && s.Status == WorkflowStepStatus.Pending)
                    .OrderBy(s => s.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            if (currentStep is not null)
            {
                var candidates = await _nodeAssignees.ResolveCandidatesAsync(currentStep.NodeId, cancellationToken);
                var nodeHasCargo = await _db.WorkflowNodePolicies
                    .AnyAsync(p => p.WorkflowNodeId == currentStep.NodeId, cancellationToken);

                if (assignee is not null && nodeHasCargo && !candidates.Contains(assignee.Id))
                {
                    // El encargado no ocupa el cargo que dicta el flujo para este paso.
                    await transaction.RollbackAsync(cancellationToken);
                    return TaskCoreResult<TaskItemDetailDto>.Invalid(
                        "El encargado debe ocupar el cargo que el flujo asigna al primer paso.");
                }

                if (assignee is not null)
                {
                    currentStep.AssignedToTenantUserId = assignee.Id;
                    _db.TaskItemActivities.Add(BuildActivity(tenantId, task.Id, actorUserId, actorName,
                        TaskActivityType.Action,
                        $"ruto el primer paso del flujo a {assignee.Email}"));
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Entrega best-effort FUERA de la transaccion (email + badge en vivo), igual que AssignAsync:
        // un fallo de SMTP/SignalR no revierte la tarea ya creada (la notificacion in-app ya quedo).
        if (assignee is not null)
        {
            var taskTitle = string.IsNullOrWhiteSpace(title) ? number : title;
            await SendNotificationEmailAsync(assignee.Email,
                $"Te asignaron la tarea {number}",
                $"Te asignaron la tarea <strong>{number}</strong>: {System.Net.WebUtility.HtmlEncode(taskTitle)}.",
                cancellationToken);
            await BroadcastNotificationAsync(assignee.Id, cancellationToken);
        }
        foreach (var (recipientId, recipientEmail) in conceptRecipients)
        {
            await SendNotificationEmailAsync(recipientEmail,
                $"Nueva actividad del proceso ({number})",
                $"Se registro la actividad <strong>{number}</strong> de un proceso en el que estas configurado como destinatario.",
                cancellationToken);
            await BroadcastNotificationAsync(recipientId, cancellationToken);
        }

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
        if (request.ActivityTypeId is Guid updActivityTypeId
            && !await _db.ActivityTypes.AnyAsync(t => t.Id == updActivityTypeId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El tipo de actividad no existe en el tenant.");
        }
        if (request.SubcategoriaId is Guid updSubcategoriaId
            && !await _db.ActividadSubcategorias.AnyAsync(s => s.Id == updSubcategoriaId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El concepto (subcategoria) no existe en el tenant.");
        }
        if (request.EntidadId is Guid updEntidadId
            && !await _db.Entidades.AnyAsync(e => e.Id == updEntidadId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("La entidad (Empresa/Area) no existe en el tenant.");
        }
        if (request.ProjectId is Guid projectId
            && !await _db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return TaskCoreResult<TaskItemDetailDto>.Invalid("El proyecto no existe en el tenant.");
        }
        // Proyectos P3: el hito debe pertenecer al proyecto indicado en el request.
        if (request.MilestoneId is Guid updMilestoneId)
        {
            if (request.ProjectId is not Guid updMilestoneProjectId)
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El hito requiere indicar el proyecto.");
            }
            if (!await _db.ProjectMilestones.AnyAsync(m => m.Id == updMilestoneId && m.ProjectId == updMilestoneProjectId, cancellationToken))
            {
                return TaskCoreResult<TaskItemDetailDto>.Invalid("El hito no pertenece al proyecto indicado.");
            }
        }

        task.Title = title;
        task.Description = Normalize(request.Description);
        // null = no tocar la clasificacion existente (Ola 1). El modal (Ola 3) hara la semantica
        // completa (incluido limpiar).
        if (request.ActivityTypeId is not null) { task.ActivityTypeId = request.ActivityTypeId; }
        if (request.SubcategoriaId is not null) { task.SubcategoriaId = request.SubcategoriaId; }
        if (request.EntidadId is not null) { task.EntidadId = request.EntidadId; }
        task.Priority = request.Priority;
        task.DueDate = request.DueDate;
        task.StartDate = request.StartDate;
        task.RequesterName = Normalize(request.RequesterName);
        task.RequesterEmail = Normalize(request.RequesterEmail);
        task.RequesterPhone = Normalize(request.RequesterPhone);
        task.CcEmails = SerializeCcEmails(request.CcEmails);
        task.ProjectId = request.ProjectId;
        // Al reasignar proyecto, el hito sigue el request (coherente con ProjectId reemplazado).
        task.MilestoneId = request.ProjectId is null ? null : request.MilestoneId;
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
        // Ola 7 (endurecimiento): notificacion al asignar. Ademas de la traza en la historia de la
        // tarea, se ENTREGA una notificacion in-app al encargado (bandeja/campana). La entrega por
        // email con plantilla queda como backlog (el canal existe: IEmailSender).
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, $"notifico a {assignee.Email}: le asignaron la tarea {task.Number}"));
        _db.Notifications.Add(BuildNotification(task.TenantId, tenantUserId, NotificationKind.TaskAssigned,
            title: $"Te asignaron la tarea {task.Number}",
            body: string.IsNullOrWhiteSpace(task.Title) ? $"Tarea {task.Number}" : task.Title!,
            actorName: actorName, relatedTaskItemId: task.Id));
        IReadOnlyList<(Guid Id, string Email)> conceptRecipients = Array.Empty<(Guid, string)>();
        if (task.SubcategoriaId is Guid subcategoriaId)
        {
            conceptRecipients = await AddConceptNotificationAsync(task.TenantId, task.Id, subcategoriaId, actorUserId, actorName,
                excludeEmail: assignee.Email, cancellationToken: cancellationToken);
        }
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Conflict(ConflictMessage);
        }

        // #4a: entrega REAL por email (best-effort, FUERA de la transaccion). Si el correo del tenant
        // no esta configurado, IEmailSender devuelve Ok=false sin lanzar; cualquier fallo se ignora
        // para no romper la asignacion (la notificacion in-app ya quedo persistida).
        var taskTitle = string.IsNullOrWhiteSpace(task.Title) ? task.Number : task.Title!;
        await SendNotificationEmailAsync(assignee.Email,
            $"Te asignaron la tarea {task.Number}",
            $"Te asignaron la tarea <strong>{task.Number}</strong>: {System.Net.WebUtility.HtmlEncode(taskTitle)}.",
            cancellationToken);
        foreach (var (_, email) in conceptRecipients)
        {
            await SendNotificationEmailAsync(email,
                $"Nueva actividad del proceso ({task.Number})",
                $"Se registro la actividad <strong>{task.Number}</strong> de un proceso en el que estas configurado como destinatario.",
                cancellationToken);
        }

        // #4b: refresco EN VIVO del badge de la campana (SignalR) para el encargado y los destinatarios
        // del concepto. Best-effort: un fallo de difusion no afecta la asignacion ya persistida.
        await BroadcastNotificationAsync(tenantUserId, cancellationToken);
        foreach (var (id, _) in conceptRecipients)
        {
            await BroadcastNotificationAsync(id, cancellationToken);
        }

        return TaskCoreResult<TaskItemSummaryDto>.Ok(await ToSummaryAsync(task, cancellationToken));
    }

    /// <summary>
    /// #4a: envia una notificacion por email best-effort. No lanza (los errores de SMTP no deben
    /// tumbar la operacion de negocio); si el correo del tenant no esta habilitado no hace nada.
    /// </summary>
    private async Task SendNotificationEmailAsync(string? toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) { return; }
        try
        {
            var html = $"<div style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;font-size:14px;color:#222\">"
                + $"<p>{htmlBody}</p>"
                + "<p style=\"color:#888;font-size:12px\">ECOREX.tareas - notificacion automatica.</p></div>";
            await _emailSender.SendAsync(toEmail.Trim(), subject, html, cancellationToken);
        }
        catch
        {
            // best-effort: la notificacion in-app ya quedo; un fallo de SMTP no rompe la asignacion.
        }
    }

    /// <summary>#4b: difunde por SignalR que un usuario recibio una notificacion (best-effort, no lanza).</summary>
    private async Task BroadcastNotificationAsync(Guid recipientTenantUserId, CancellationToken cancellationToken)
    {
        if (_notificationBroadcaster is null) { return; }
        try
        {
            await _notificationBroadcaster.NotificationAddedAsync(recipientTenantUserId, cancellationToken);
        }
        catch
        {
            // best-effort: si SignalR falla, la campana igual se actualiza al recargar/navegar.
        }
    }

    /// <summary>
    /// Ola 7: agrega la traza + notificacion in-app a los destinatarios configurados en el concepto
    /// (ActividadSubcategoriaNotificacion), opcionalmente excluyendo un correo (el encargado ya tiene
    /// su propia). No guarda: el llamador hace SaveChanges. Devuelve los correos de los destinatarios
    /// (para la entrega por email best-effort tras el commit, #4a).
    /// </summary>
    private async Task<IReadOnlyList<(Guid Id, string Email)>> AddConceptNotificationAsync(Guid tenantId, Guid taskId, Guid subcategoriaId,
        Guid actorUserId, string actorName, string? excludeEmail = null, CancellationToken cancellationToken = default)
    {
        var recipients = await _db.ActividadSubcategoriaNotificaciones.AsNoTracking()
            .Where(n => n.SubcategoriaId == subcategoriaId)
            .Join(_db.TenantUsers.AsNoTracking(), n => n.TenantUserId, u => u.Id,
                (n, u) => new { u.Id, u.Email })
            .Where(x => excludeEmail == null || x.Email != excludeEmail)
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0)
        {
            return Array.Empty<(Guid, string)>();
        }
        _db.TaskItemActivities.Add(BuildActivity(tenantId, taskId, actorUserId, actorName,
            TaskActivityType.Action,
            $"notifico a {recipients.Count} usuario(s) del concepto: {string.Join(", ", recipients.Select(r => r.Email))}"));
        // Entrega real in-app: una notificacion por destinatario configurado en el concepto.
        foreach (var r in recipients)
        {
            _db.Notifications.Add(BuildNotification(tenantId, r.Id, NotificationKind.ConceptNotice,
                title: "Nueva actividad del proceso",
                body: "Se registro una actividad de un proceso en el que estas configurado como destinatario.",
                actorName: actorName, relatedTaskItemId: taskId));
        }
        return recipients.Select(r => (r.Id, r.Email)).ToList();
    }

    /// <summary>Construye una notificacion in-app (Ola 7) con TenantId explicito (no hay stamping automatico).</summary>
    private static Notification BuildNotification(Guid tenantId, Guid recipientTenantUserId, NotificationKind kind,
        string title, string body, string? actorName, Guid? relatedTaskItemId)
        => new()
        {
            TenantId = tenantId,
            RecipientTenantUserId = recipientTenantUserId,
            Kind = kind,
            Title = title,
            Body = body,
            LinkRoute = "actividades",
            RelatedTaskItemId = relatedTaskItemId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? null : actorName.Trim(),
        };

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

    public async Task<TaskCoreResult<TaskItemSummaryDto>> ArchiveAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        // Archivado = visibilidad (IsArchived), NO transicion de estado: no pasa por
        // TaskItemStateMachine y se permite sobre tareas Closed (caso tipico: limpiar
        // el historial cerrado). Ver decision documentada en ITaskItemService.
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemSummaryDto>.NotFound("Tarea no encontrada.");
        }
        if (task.IsArchived)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Invalid("La tarea ya esta archivada.");
        }

        task.IsArchived = true;
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, "archivo la tarea"));
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

    public async Task<TaskCoreResult<TaskItemSummaryDto>> RestoreAsync(Guid taskId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemSummaryDto>.NotFound("Tarea no encontrada.");
        }
        if (!task.IsArchived)
        {
            return TaskCoreResult<TaskItemSummaryDto>.Invalid("La tarea no esta archivada.");
        }

        task.IsArchived = false;
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, task.Id, actorUserId, actorName,
            TaskActivityType.Action, "restauro la tarea"));
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

    // ---- Checklist (ADR-0020) ----

    public async Task<TaskCoreResult<TaskItemChecklistItemDto>> AddChecklistItemAsync(Guid taskId, string text, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return TaskCoreResult<TaskItemChecklistItemDto>.Invalid("El texto del item es obligatorio.");
        }
        if (trimmed.Length > 500)
        {
            return TaskCoreResult<TaskItemChecklistItemDto>.Invalid("El texto del item supera 500 caracteres.");
        }
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<TaskItemChecklistItemDto>.NotFound("Tarea no encontrada.");
        }
        if (task.Status == TaskItemStatus.Closed)
        {
            return TaskCoreResult<TaskItemChecklistItemDto>.Invalid(ClosedMessage);
        }

        var nextOrder = (await _db.TaskItemChecklistItems
            .Where(i => i.TaskItemId == taskId)
            .Select(i => (int?)i.SortOrder)
            .MaxAsync(cancellationToken) ?? -1) + 1;
        var item = new TaskItemChecklistItem
        {
            TenantId = task.TenantId,
            TaskItemId = taskId,
            Text = trimmed,
            SortOrder = nextOrder
        };
        _db.TaskItemChecklistItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<TaskItemChecklistItemDto>.Ok(ToChecklistDto(item));
    }

    public async Task<TaskCoreResult<TaskItemChecklistItemDto>> ToggleChecklistItemAsync(Guid checklistItemId, bool isCompleted, Guid? completedByTenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var item = await _db.TaskItemChecklistItems
            .FirstOrDefaultAsync(i => i.Id == checklistItemId, cancellationToken);
        if (item is null)
        {
            return TaskCoreResult<TaskItemChecklistItemDto>.NotFound("Item de checklist no encontrado.");
        }
        if (item.IsCompleted == isCompleted)
        {
            return TaskCoreResult<TaskItemChecklistItemDto>.Ok(ToChecklistDto(item));
        }

        item.IsCompleted = isCompleted;
        item.CompletedAt = isCompleted ? DateTimeOffset.UtcNow : null;
        item.CompletedByTenantUserId = isCompleted ? completedByTenantUserId : null;
        if (isCompleted)
        {
            // Solo el COMPLETAR deja traza (desmarcar es correccion, no hito).
            _db.TaskItemActivities.Add(BuildActivity(item.TenantId, item.TaskItemId, actorUserId, actorName,
                TaskActivityType.Action, $"completo el item de checklist '{item.Text}'"));
        }
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<TaskItemChecklistItemDto>.Ok(ToChecklistDto(item));
    }

    public async Task<TaskCoreResult<bool>> RemoveChecklistItemAsync(Guid checklistItemId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var item = await _db.TaskItemChecklistItems
            .FirstOrDefaultAsync(i => i.Id == checklistItemId, cancellationToken);
        if (item is null)
        {
            return TaskCoreResult<bool>.NotFound("Item de checklist no encontrado.");
        }
        _db.TaskItemChecklistItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<bool>> ReorderChecklistAsync(Guid taskId, IReadOnlyList<Guid> orderedItemIds, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var items = await _db.TaskItemChecklistItems
            .Where(i => i.TaskItemId == taskId)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return TaskCoreResult<bool>.NotFound("La tarea no tiene checklist.");
        }
        for (int i = 0; i < orderedItemIds.Count; i++)
        {
            var item = items.FirstOrDefault(x => x.Id == orderedItemIds[i]);
            if (item is not null) { item.SortOrder = i; }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    // ---- Asignados M:N (ADR-0020) ----

    public async Task<TaskCoreResult<bool>> AddAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            return TaskCoreResult<bool>.NotFound("Tarea no encontrada.");
        }
        if (task.Status == TaskItemStatus.Closed)
        {
            return TaskCoreResult<bool>.Invalid(ClosedMessage);
        }
        var member = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == tenantUserId, cancellationToken);
        if (member is null)
        {
            return TaskCoreResult<bool>.Invalid("El asignado no pertenece al tenant.");
        }
        if (await _db.TaskItemAssignments.AnyAsync(a => a.TaskItemId == taskId && a.TenantUserId == tenantUserId, cancellationToken))
        {
            return TaskCoreResult<bool>.Ok(false);
        }

        _db.TaskItemAssignments.Add(new TaskItemAssignment
        {
            TenantId = task.TenantId,
            TaskItemId = taskId,
            TenantUserId = tenantUserId
        });
        _db.TaskItemActivities.Add(BuildActivity(task.TenantId, taskId, actorUserId, actorName,
            TaskActivityType.Action, $"agrego a {member.Email} al equipo de la tarea"));
        await _db.SaveChangesAsync(cancellationToken);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<bool>> RemoveAssigneeAsync(Guid taskId, Guid tenantUserId, Guid actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        var assignment = await _db.TaskItemAssignments
            .FirstOrDefaultAsync(a => a.TaskItemId == taskId && a.TenantUserId == tenantUserId, cancellationToken);
        if (assignment is null)
        {
            return TaskCoreResult<bool>.NotFound("El usuario no esta asignado a la tarea.");
        }
        var member = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == tenantUserId, cancellationToken);
        _db.TaskItemAssignments.Remove(assignment);
        _db.TaskItemActivities.Add(BuildActivity(assignment.TenantId, taskId, actorUserId, actorName,
            TaskActivityType.Action, $"quito a {member?.Email ?? "un usuario"} del equipo de la tarea"));
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
        if (filter.SubcategoriaId is Guid filterSubcategoriaId)
        {
            query = query.Where(t => t.SubcategoriaId == filterSubcategoriaId);
        }
        if (filter.EntidadId is Guid filterEntidadId)
        {
            query = query.Where(t => t.EntidadId == filterEntidadId);
        }
        if (filter.ProjectId is Guid projectId)
        {
            query = query.Where(t => t.ProjectId == projectId);
        }
        if (filter.MilestoneId is Guid filterMilestoneId)
        {
            query = query.Where(t => t.MilestoneId == filterMilestoneId);
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
        var checklist = await _db.TaskItemChecklistItems.AsNoTracking()
            .Where(i => i.TaskItemId == taskId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
            .Select(i => new TaskItemChecklistItemDto(i.Id, i.TaskItemId, i.Text, i.IsCompleted,
                i.CompletedAt, i.CompletedByTenantUserId, i.SortOrder))
            .ToListAsync(cancellationToken);
        var assignees = await LoadAssigneesAsync(taskId, cancellationToken);

        return new TaskItemDetailDto(summary, task.Description,
            task.RequesterName, task.RequesterEmail, task.RequesterPhone,
            DeserializeCcEmails(task.CcEmails), totalSeconds, recentActivity, attachments,
            checklist, assignees);
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

        var activityTypeIds = tasks.Where(t => t.ActivityTypeId.HasValue)
            .Select(t => t.ActivityTypeId!.Value).Distinct().ToList();
        var activityTypeNames = await _db.ActivityTypes.AsNoTracking()
            .Where(t => activityTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.Category}/{t.Name}", cancellationToken);

        var subcategoriaIds = tasks.Where(t => t.SubcategoriaId.HasValue)
            .Select(t => t.SubcategoriaId!.Value).Distinct().ToList();
        var subcategoriaNames = subcategoriaIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ActividadSubcategorias.AsNoTracking()
                .Where(s => subcategoriaIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Nombre, cancellationToken);

        // Proyectos P3: nombre del hito enlazado (para las tarjetas del tablero).
        var milestoneIds = tasks.Where(t => t.MilestoneId.HasValue)
            .Select(t => t.MilestoneId!.Value).Distinct().ToList();
        var milestoneNames = milestoneIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ProjectMilestones.AsNoTracking()
                .Where(m => milestoneIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        return tasks.Select(t => new TaskItemSummaryDto(
            t.Id, t.Number, t.Title, t.ActivityTypeId,
            t.ActivityTypeId is Guid atid && activityTypeNames.TryGetValue(atid, out var name) ? name : null,
            t.Priority, t.Status, t.AssigneeTenantUserId, t.DueDate, t.ProjectId, t.Color,
            t.IsArchived, t.ClosedAt, t.Version, t.CreatedAt,
            tagsByTask.TryGetValue(t.Id, out var tags) ? tags : Array.Empty<TaskItemTagDto>(),
            t.StartDate, t.BoardId, t.ColumnId,
            t.SubcategoriaId,
            t.SubcategoriaId is Guid scid && subcategoriaNames.TryGetValue(scid, out var scname) ? scname : null,
            t.EntidadId,
            t.MilestoneId,
            t.MilestoneId is Guid msid && milestoneNames.TryGetValue(msid, out var msname) ? msname : null)).ToList();
    }

    /// <summary>Equipo asignado (M:N, ADR-0020) con iniciales para los avatares.</summary>
    private async Task<IReadOnlyList<TaskItemAssigneeDto>> LoadAssigneesAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var rows = await _db.TaskItemAssignments.AsNoTracking()
            .Where(a => a.TaskItemId == taskId)
            .Join(_db.TenantUsers.AsNoTracking(), a => a.TenantUserId, u => u.Id,
                (a, u) => new { u.Id, u.Email, u.PlatformUserId })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) { return Array.Empty<TaskItemAssigneeDto>(); }

        var platformIds = rows.Select(r => r.PlatformUserId).Distinct().ToList();
        var displayNames = await _db.PlatformUsers.AsNoTracking().IgnoreQueryFilters()
            .Where(p => platformIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName ?? p.Email, cancellationToken);
        return rows.Select(r =>
        {
            var name = displayNames.TryGetValue(r.PlatformUserId, out var n) ? n : r.Email;
            return new TaskItemAssigneeDto(r.Id, MemberInitials.From(name), name);
        }).OrderBy(a => a.DisplayName).ToList();
    }

    private static TaskItemChecklistItemDto ToChecklistDto(TaskItemChecklistItem item)
        => new(item.Id, item.TaskItemId, item.Text, item.IsCompleted,
            item.CompletedAt, item.CompletedByTenantUserId, item.SortOrder);

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

    /// <summary>
    /// Renderiza una plantilla de concepto (TituloAuto/DetalleAuto, RQ07) con tokens basicos.
    /// Hoy solo resuelve <c>@cliente</c> (por el solicitante del request); los tokens no resueltos
    /// quedan literales. El set completo de tokens (fecha, entidad, etc.) llega con el modal (Ola 3).
    /// </summary>
    private static string RenderConceptTemplate(string template, CreateTaskItemRequest request)
    {
        var cliente = (request.RequesterName ?? "").Trim();
        var rendered = template.Replace("@cliente", cliente, StringComparison.OrdinalIgnoreCase);

        // Si el token quedo vacio (p.ej. el arranque form-first no captura el cliente), la plantilla
        // "Requerimiento infra - @cliente" dejaria un separador colgando: "Requerimiento infra - ".
        // Se limpia el borde para que el titulo no nazca roto.
        return cliente.Length == 0 ? rendered.Trim().TrimEnd('-', ':', '|', ',', ' ').TrimEnd() : rendered;
    }
}
