using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del nucleo de tareas (FASE 3, ADR-0013) en matriz dual
/// PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// consecutivo unico y correlativo bajo 10 creaciones CONCURRENTES (TenantSequence con
/// UPDATE condicional atomico), aislamiento cross-tenant de TaskItems y concurrencia
/// optimista portable (dos updates con token viejo -> el segundo recibe Conflict tipado).
/// </summary>
public abstract class TaskCoreTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected TaskCoreTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ConcurrentCreates_YieldUniqueCorrelativeNumbers()
    {
        var seed = await SeedTenantAsync("Nucleo Concurrencia");

        // 10 creaciones CONCURRENTES, cada una con su propio DbContext (no es thread-safe).
        var creations = Enumerable.Range(0, 10).Select(async i =>
        {
            await using var ctx = _fixture.CreateContext(seed.TenantId);
            var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
            var service = BuildService(ctx, tenantContext);
            return await service.CreateAsync(
                new CreateTaskItemRequest($"Tarea concurrente {i}", seed.ActivityTypeId),
                seed.PlatformUserId, "Tester");
        }).ToList();

        var results = await Task.WhenAll(creations);

        Assert.All(results, r => Assert.True(r.IsOk, r.Error));
        var numbers = results.Select(r => r.Value!.Item.Number).ToList();
        // Sin duplicados y correlativos exactos T00001..T00010.
        Assert.Equal(10, numbers.Distinct(StringComparer.Ordinal).Count());
        var expected = Enumerable.Range(1, 10).Select(n => "T" + n.ToString().PadLeft(5, '0'));
        Assert.Equal(expected.OrderBy(x => x), numbers.OrderBy(x => x));

        // La secuencia quedo apuntando al siguiente valor.
        await using var verify = _fixture.CreateContext(seed.TenantId);
        var next = await verify.TenantSequences.SingleAsync(s => s.Code == TaskItemService.SequenceCode);
        Assert.Equal(11, next.NextValue);
    }

    [Fact]
    public async Task TaskItems_AreIsolatedBetweenTenants()
    {
        var seedA = await SeedTenantAsync("Nucleo Tenant A");
        var seedB = await SeedTenantAsync("Nucleo Tenant B");

        var createdA = await CreateTaskAsync(seedA, "Tarea privada de A");
        Assert.True(createdA.IsOk, createdA.Error);

        // Tenant B no ve los TaskItems de A (filtro global por TenantId).
        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            Assert.Empty(await ctxB.TaskItems.ToListAsync());
            // Ni siquiera consultando el detalle por id directo.
            var tenantContext = new TestTenantContext(seedB.TenantId, seedB.PlatformUserId);
            var serviceB = BuildService(ctxB, tenantContext);
            Assert.Null(await serviceB.GetDetailAsync(createdA.Value!.Item.Id));
        }

        // Tenant A si ve su tarea.
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var items = await ctxA.TaskItems.ToListAsync();
            var item = Assert.Single(items);
            Assert.Equal(seedA.TenantId, item.TenantId);
        }
    }

    [Fact]
    public async Task OptimisticConcurrency_SecondStaleUpdate_GetsTypedConflict()
    {
        var seed = await SeedTenantAsync("Nucleo Conflicto");
        var created = await CreateTaskAsync(seed, "Tarea disputada");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;
        var staleVersion = created.Value.Item.Version; // token leido por "ambos usuarios"

        // Primer usuario actualiza con el token vigente: OK (la Version avanza).
        var first = await UpdateTitleAsync(seed, taskId, "Titulo del primer usuario", staleVersion);
        Assert.True(first.IsOk, first.Error);
        Assert.True(first.Value!.Item.Version > staleVersion);

        // Segundo usuario llega con el MISMO token viejo: conflicto tipado, no excepcion.
        var second = await UpdateTitleAsync(seed, taskId, "Titulo del segundo usuario", staleVersion);
        Assert.Equal(TaskCoreStatus.Conflict, second.Status);

        // La edicion del primero se conserva.
        await using var verify = _fixture.CreateContext(seed.TenantId);
        var task = await verify.TaskItems.SingleAsync(t => t.Id == taskId);
        Assert.Equal("Titulo del primer usuario", task.Title);
    }

    [Fact]
    public async Task ChangeStatus_InvalidTransition_GetsTypedError_AndClosedIsReadOnly()
    {
        var seed = await SeedTenantAsync("Nucleo Estados");
        var created = await CreateTaskAsync(seed, "Tarea con estados");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildService(ctx, tenantContext);

        // Pending -> Closed es invalido (error tipado, no excepcion).
        var invalid = await service.ChangeStatusAsync(taskId, TaskItemStatus.Closed, null, seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.InvalidTransition, invalid.Status);

        // Camino valido: Pending -> InProgress -> Done -> Closed (setea ClosedAt).
        Assert.True((await service.ChangeStatusAsync(taskId, TaskItemStatus.InProgress, null, seed.PlatformUserId, "Tester")).IsOk);
        Assert.True((await service.ChangeStatusAsync(taskId, TaskItemStatus.Done, null, seed.PlatformUserId, "Tester")).IsOk);
        var closed = await service.ChangeStatusAsync(taskId, TaskItemStatus.Closed, "cierre de prueba", seed.PlatformUserId, "Tester");
        Assert.True(closed.IsOk, closed.Error);
        Assert.NotNull(closed.Value!.ClosedAt);

        // Closed es terminal: ni cambia de estado ni admite edicion.
        var reopen = await service.ChangeStatusAsync(taskId, TaskItemStatus.Active, null, seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.InvalidTransition, reopen.Status);
        var edit = await service.UpdateAsync(taskId, new UpdateTaskItemRequest(
            "No deberia", null, seed.ActivityTypeId, TaskPriority.Low, null, null, null, null, null, null, null,
            closed.Value.Version), seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, edit.Status);
    }

    [Fact]
    public async Task ArchiveAndRestore_ToggleListVisibility_AndRecordActivity()
    {
        var seed = await SeedTenantAsync("Nucleo Archivado");
        var keep = await CreateTaskAsync(seed, "Tarea visible");
        var toArchive = await CreateTaskAsync(seed, "Tarea archivable");
        Assert.True(keep.IsOk, keep.Error);
        Assert.True(toArchive.IsOk, toArchive.Error);
        var taskId = toArchive.Value!.Item.Id;

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildService(ctx, tenantContext);

        // Archivar: OK, no cambia el estado (no es transicion de la maquina de estados).
        var previousStatus = toArchive.Value.Item.Status;
        var archived = await service.ArchiveAsync(taskId, seed.PlatformUserId, "Tester");
        Assert.True(archived.IsOk, archived.Error);
        Assert.True(archived.Value!.IsArchived);
        Assert.Equal(previousStatus, archived.Value.Status);

        // Doble archivado -> Invalid tipado, no excepcion.
        var again = await service.ArchiveAsync(taskId, seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, again.Status);

        // ListAsync por defecto (IncludeArchived = false) la oculta; el resto sigue visible.
        var defaultList = await service.ListAsync(new TaskItemListFilter());
        Assert.Equal(1, defaultList.TotalCount);
        Assert.DoesNotContain(defaultList.Items, t => t.Id == taskId);

        // Con IncludeArchived = true aparece marcada como archivada.
        var withArchived = await service.ListAsync(new TaskItemListFilter(IncludeArchived: true));
        Assert.Equal(2, withArchived.TotalCount);
        var archivedItem = Assert.Single(withArchived.Items, t => t.Id == taskId);
        Assert.True(archivedItem.IsArchived);

        // Restaurar: vuelve al listado por defecto con el mismo estado.
        var restored = await service.RestoreAsync(taskId, seed.PlatformUserId, "Tester");
        Assert.True(restored.IsOk, restored.Error);
        Assert.False(restored.Value!.IsArchived);
        Assert.Equal(previousStatus, restored.Value.Status);
        var afterRestore = await service.ListAsync(new TaskItemListFilter());
        Assert.Equal(2, afterRestore.TotalCount);
        Assert.Contains(afterRestore.Items, t => t.Id == taskId);

        // Restaurar una no archivada -> Invalid tipado.
        var notArchived = await service.RestoreAsync(taskId, seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, notArchived.Status);

        // Quedo la traza en TaskItemActivity: "archivo la tarea" y "restauro la tarea".
        var activities = await ctx.TaskItemActivities.AsNoTracking()
            .Where(a => a.TaskItemId == taskId)
            .Select(a => a.Text)
            .ToListAsync();
        Assert.Contains("archivo la tarea", activities);
        Assert.Contains("restauro la tarea", activities);
    }

    [Fact]
    public async Task Archive_OnClosedTask_IsAllowed()
    {
        // Decision documentada en ITaskItemService: el archivado es visibilidad, no una
        // transicion de estado, y archivar tareas Closed es el caso tipico de limpieza.
        var seed = await SeedTenantAsync("Nucleo Archivo Cerradas");
        var created = await CreateTaskAsync(seed, "Tarea cerrada y archivada");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildService(ctx, tenantContext);

        Assert.True((await service.ChangeStatusAsync(taskId, TaskItemStatus.InProgress, null, seed.PlatformUserId, "Tester")).IsOk);
        Assert.True((await service.ChangeStatusAsync(taskId, TaskItemStatus.Done, null, seed.PlatformUserId, "Tester")).IsOk);
        Assert.True((await service.ChangeStatusAsync(taskId, TaskItemStatus.Closed, null, seed.PlatformUserId, "Tester")).IsOk);

        var archived = await service.ArchiveAsync(taskId, seed.PlatformUserId, "Tester");
        Assert.True(archived.IsOk, archived.Error);
        Assert.True(archived.Value!.IsArchived);
        Assert.Equal(TaskItemStatus.Closed, archived.Value.Status);

        // Y tambien se puede restaurar: sigue Closed (solo cambio la visibilidad).
        var restored = await service.RestoreAsync(taskId, seed.PlatformUserId, "Tester");
        Assert.True(restored.IsOk, restored.Error);
        Assert.Equal(TaskItemStatus.Closed, restored.Value!.Status);
    }

    [Fact]
    public async Task CreateWithSubcategoria_LinksConcept_AndDerivesBoardAndFirstColumn()
    {
        // Ola 1 (puente Concepto->Tarea): un alta clasificada por concepto (subcategoria), sin
        // ActivityType, hereda el TABLERO del concepto y se ubica en su PRIMERA columna (no la
        // columna "terminada" que el concepto marca como fin).
        var seed = await SeedTenantAsync("Nucleo Concepto");

        Guid subcategoriaId;
        Guid boardId;
        Guid firstColumnId;
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var board = new TaskBoard { TenantId = seed.TenantId, Name = "Tablero Comercial", Kind = TaskBoardKind.Activities };
            ctx.TaskBoards.Add(board);
            var firstColumn = new TaskBoardColumn { TenantId = seed.TenantId, BoardId = board.Id, Name = "Por hacer", SortOrder = 0 };
            var doneColumn = new TaskBoardColumn { TenantId = seed.TenantId, BoardId = board.Id, Name = "Terminado", SortOrder = 1, IsDone = true };
            ctx.TaskBoardColumns.AddRange(firstColumn, doneColumn);
            var categoria = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-9", Nombre = "Comercial" };
            ctx.ActividadCategorias.Add(categoria);
            var subcategoria = new ActividadSubcategoria
            {
                TenantId = seed.TenantId,
                CategoriaId = categoria.Id,
                Codigo = "CAT-9-1",
                Nombre = "Cotizacion",
                TaskBoardId = board.Id,
                TaskBoardColumnId = doneColumn.Id // el concepto marca la columna "terminado"
            };
            ctx.ActividadSubcategorias.Add(subcategoria);
            await ctx.SaveChangesAsync();
            subcategoriaId = subcategoria.Id;
            boardId = board.Id;
            firstColumnId = firstColumn.Id;
        }

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var service = BuildService(ctx2, new TestTenantContext(seed.TenantId, seed.PlatformUserId));
        var created = await service.CreateAsync(
            new CreateTaskItemRequest("Tarea desde concepto", ActivityTypeId: null, SubcategoriaId: subcategoriaId),
            seed.PlatformUserId, "Tester");

        Assert.True(created.IsOk, created.Error);
        var item = created.Value!.Item;
        Assert.Equal(subcategoriaId, item.SubcategoriaId);
        Assert.Equal("Cotizacion", item.SubcategoriaName);
        Assert.Null(item.ActivityTypeId); // clasificada solo por concepto
        Assert.Equal(boardId, item.BoardId); // tablero heredado del concepto
        Assert.Equal(firstColumnId, item.ColumnId); // primera columna, NO la "terminado"
    }

    [Fact]
    public async Task Create_WithoutActivityTypeNorSubcategoria_IsInvalid()
    {
        // La tarea debe clasificarse por al menos uno de los dos (D1).
        var seed = await SeedTenantAsync("Nucleo Sin Clasificacion");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = BuildService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId));
        var result = await service.CreateAsync(
            new CreateTaskItemRequest("Sin clasificar", ActivityTypeId: null),
            seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task CreateWithSubcategoria_RecordsNotificationTrace_ForConceptRecipients()
    {
        // Ola 2: al crear una tarea de un concepto con destinatarios configurados, queda traza en
        // el historial (la entrega real -- email/in-app -- es la Ola 7).
        var seed = await SeedTenantAsync("Nucleo Notificaciones");

        Guid subcategoriaId;
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var categoria = new ActividadCategoria { TenantId = seed.TenantId, Codigo = "CAT-N", Nombre = "Operaciones" };
            ctx.ActividadCategorias.Add(categoria);
            var subcategoria = new ActividadSubcategoria
            {
                TenantId = seed.TenantId,
                CategoriaId = categoria.Id,
                Codigo = "CAT-N-1",
                Nombre = "Con notificacion"
            };
            ctx.ActividadSubcategorias.Add(subcategoria);
            ctx.ActividadSubcategoriaNotificaciones.Add(new ActividadSubcategoriaNotificacion
            {
                TenantId = seed.TenantId,
                SubcategoriaId = subcategoria.Id,
                TenantUserId = seed.TenantUserId
            });
            await ctx.SaveChangesAsync();
            subcategoriaId = subcategoria.Id;
        }

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var service = BuildService(ctx2, new TestTenantContext(seed.TenantId, seed.PlatformUserId));
        var created = await service.CreateAsync(
            new CreateTaskItemRequest("Tarea con notificacion", ActivityTypeId: null, SubcategoriaId: subcategoriaId),
            seed.PlatformUserId, "Tester");
        Assert.True(created.IsOk, created.Error);

        var activities = await ctx2.TaskItemActivities.AsNoTracking()
            .Where(a => a.TaskItemId == created.Value!.Item.Id)
            .Select(a => a.Text)
            .ToListAsync();
        Assert.Contains(activities, t => t.StartsWith("notifico a"));
    }

    [Fact]
    public async Task Assign_RecordsNotificationTrace_ForAssignee()
    {
        // Ola 7 (endurecimiento): al asignar una tarea queda traza de notificacion dirigida al
        // encargado (la entrega real -- email/in-app con plantilla -- es backlog).
        var seed = await SeedTenantAsync("Nucleo Notif Asignar");
        var created = await CreateTaskAsync(seed, "Tarea para asignar");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = BuildService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId));
        var assigned = await service.AssignAsync(taskId, seed.TenantUserId, seed.PlatformUserId, "Tester");
        Assert.True(assigned.IsOk, assigned.Error);

        var activities = await ctx.TaskItemActivities.AsNoTracking()
            .Where(a => a.TaskItemId == taskId)
            .Select(a => a.Text)
            .ToListAsync();
        Assert.Contains(activities, t => t.StartsWith("notifico a") && t.Contains("le asignaron la tarea"));
    }

    [Fact]
    public async Task Assign_CreatesInAppNotification_ForAssignee()
    {
        // Ola 7 (entrega real): al asignar, ademas de la traza se ENTREGA una notificacion in-app
        // (fila Notification) al encargado, no leida, con el tipo TaskAssigned.
        var seed = await SeedTenantAsync("Nucleo Notif InApp");
        var created = await CreateTaskAsync(seed, "Tarea con notificacion in-app");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = BuildService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId));
        var assigned = await service.AssignAsync(taskId, seed.TenantUserId, seed.PlatformUserId, "Tester");
        Assert.True(assigned.IsOk, assigned.Error);

        var notifs = await ctx.Notifications.AsNoTracking()
            .Where(n => n.RecipientTenantUserId == seed.TenantUserId)
            .ToListAsync();
        var assignNotif = Assert.Single(notifs);
        Assert.Equal(Ecorex.Domain.Enums.NotificationKind.TaskAssigned, assignNotif.Kind);
        Assert.False(assignNotif.IsRead);
        Assert.Equal(taskId, assignNotif.RelatedTaskItemId);
        Assert.Equal(seed.TenantId, assignNotif.TenantId);
    }

    [Fact]
    public async Task CreateActivity_LinkedToProjectMilestone_IsPersisted_AndCrossProjectRejected()
    {
        // Proyectos P3: una actividad creada en un proyecto se enlaza a un hito DEL MISMO proyecto;
        // un hito de otro proyecto es rechazado (Invalid).
        var seed = await SeedTenantAsync("Proyectos Hito");

        Guid projectId, milestoneId, otherMilestoneId;
        await using (var ctx = _fixture.CreateContext(seed.TenantId))
        {
            var project = new Project { TenantId = seed.TenantId, Code = "PRJ-T1", Name = "Proyecto test", OwnerTenantUserId = seed.TenantUserId };
            var other = new Project { TenantId = seed.TenantId, Code = "PRJ-T2", Name = "Otro proyecto", OwnerTenantUserId = seed.TenantUserId };
            ctx.Projects.AddRange(project, other);
            var milestone = new ProjectMilestone { TenantId = seed.TenantId, ProjectId = project.Id, Name = "Hito 1", SortOrder = 0 };
            var otherMilestone = new ProjectMilestone { TenantId = seed.TenantId, ProjectId = other.Id, Name = "Hito de otro", SortOrder = 0 };
            ctx.ProjectMilestones.AddRange(milestone, otherMilestone);
            await ctx.SaveChangesAsync();
            projectId = project.Id; milestoneId = milestone.Id; otherMilestoneId = otherMilestone.Id;
        }

        await using var ctx2 = _fixture.CreateContext(seed.TenantId);
        var service = BuildService(ctx2, new TestTenantContext(seed.TenantId, seed.PlatformUserId));

        // OK: hito del mismo proyecto -> se enlaza y el summary trae el nombre.
        var ok = await service.CreateAsync(
            new CreateTaskItemRequest("Actividad del hito", seed.ActivityTypeId, ProjectId: projectId, MilestoneId: milestoneId),
            seed.PlatformUserId, "Tester");
        Assert.True(ok.IsOk, ok.Error);
        Assert.Equal(projectId, ok.Value!.Item.ProjectId);
        Assert.Equal(milestoneId, ok.Value.Item.MilestoneId);
        Assert.Equal("Hito 1", ok.Value.Item.MilestoneName);

        // Invalid: hito de OTRO proyecto no pertenece al proyecto indicado.
        var bad = await service.CreateAsync(
            new CreateTaskItemRequest("Actividad hito ajeno", seed.ActivityTypeId, ProjectId: projectId, MilestoneId: otherMilestoneId),
            seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, bad.Status);
    }

    [Fact]
    public async Task Assign_SendsEmail_ToAssignee()
    {
        // #4a: al asignar se ENTREGA por email al encargado (best-effort via IEmailSender), ademas de
        // la notificacion in-app. Aqui se usa un IEmailSender grabador para verificar el envio.
        var seed = await SeedTenantAsync("Nucleo Email Asignar");
        var created = await CreateTaskAsync(seed, "Tarea con email");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var email = new RecordingEmailSender();
        var service = BuildService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId), email);
        var assigned = await service.AssignAsync(taskId, seed.TenantUserId, seed.PlatformUserId, "Tester");
        Assert.True(assigned.IsOk, assigned.Error);

        var assigneeEmail = await ctx.TenantUsers.AsNoTracking()
            .Where(u => u.Id == seed.TenantUserId).Select(u => u.Email).FirstAsync();
        var sent = email.Sent.ToList();
        Assert.Contains(sent, m => m.To == assigneeEmail && m.Subject.Contains("Te asignaron la tarea"));
    }

    [Fact]
    public async Task Create_BornAssigned_NotifiesAndEmailsAssignee()
    {
        // QA/fix: una tarea que NACE asignada (quick-create del tablero o wizard con encargado) debe
        // notificar al encargado igual que un re-assign: notificacion in-app (TaskAssigned) + email.
        // Antes del fix, CreateAsync solo fijaba el encargado sin entregar ninguna notificacion.
        var seed = await SeedTenantAsync("Nucleo Crear Asignada");

        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var email = new RecordingEmailSender();
        var service = BuildService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId), email);
        var created = await service.CreateAsync(
            new CreateTaskItemRequest("Tarea nacida asignada", seed.ActivityTypeId,
                AssigneeTenantUserId: seed.TenantUserId),
            seed.PlatformUserId, "Tester");
        Assert.True(created.IsOk, created.Error);
        var taskId = created.Value!.Item.Id;

        var notif = Assert.Single(await ctx.Notifications.AsNoTracking()
            .Where(n => n.RecipientTenantUserId == seed.TenantUserId).ToListAsync());
        Assert.Equal(Ecorex.Domain.Enums.NotificationKind.TaskAssigned, notif.Kind);
        Assert.False(notif.IsRead);
        Assert.Equal(taskId, notif.RelatedTaskItemId);

        var assigneeEmail = await ctx.TenantUsers.AsNoTracking()
            .Where(u => u.Id == seed.TenantUserId).Select(u => u.Email).FirstAsync();
        Assert.Contains(email.Sent.ToList(),
            m => m.To == assigneeEmail && m.Subject.Contains("Te asignaron la tarea"));
    }

    // ---- Helpers ----

    /// <summary>Construye el servicio con el motor de flujos real y broadcaster no-op (sin SignalR).</summary>
    private static TaskItemService BuildService(EcorexDbContext ctx, ITenantContext tenantContext)
        => BuildService(ctx, tenantContext, new NoOpEmailSender());

    private static TaskItemService BuildService(EcorexDbContext ctx, ITenantContext tenantContext, IEmailSender emailSender)
        => new(ctx, tenantContext, new SequenceService(ctx, tenantContext),
            new WorkflowEngine(ctx, tenantContext, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster()),
            emailSender, new NodeAssigneeResolver(ctx));

    private async Task<TaskCoreResult<TaskItemDetailDto>> CreateTaskAsync(SeedData seed, string title)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildService(ctx, tenantContext);
        return await service.CreateAsync(new CreateTaskItemRequest(title, seed.ActivityTypeId), seed.PlatformUserId, "Tester");
    }

    private async Task<TaskCoreResult<TaskItemDetailDto>> UpdateTitleAsync(SeedData seed, Guid taskId, string title, long version)
    {
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildService(ctx, tenantContext);
        return await service.UpdateAsync(taskId, new UpdateTaskItemRequest(
            title, null, seed.ActivityTypeId, TaskPriority.Medium, null, null, null, null, null, null, null, version),
            seed.PlatformUserId, "Tester");
    }

    /// <summary>
    /// Siembra un tenant fresco (GUIDs nuevos, seguro ante el contenedor compartido) con un
    /// TenantUser y un ActivityType, minimo necesario para crear TaskItems.
    /// </summary>
    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid tenantUserId;
        Guid platformUserId;
        Guid activityTypeId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var platformUser = new PlatformUser
            {
                Email = $"user-{tenantId:N}@taskcore.test",
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            ctx.PlatformUsers.Add(platformUser);
            var tenantUser = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = platformUser.Id,
                Email = platformUser.Email
            };
            ctx.TenantUsers.Add(tenantUser);
            var activityType = new ActivityType
            {
                TenantId = tenantId,
                Category = "General",
                Name = "Prueba"
            };
            ctx.ActivityTypes.Add(activityType);
            await ctx.SaveChangesAsync();
            tenantUserId = tenantUser.Id;
            platformUserId = platformUser.Id;
            activityTypeId = activityType.Id;
        }

        return new SeedData(tenantId, tenantUserId, platformUserId, activityTypeId);
    }

    private sealed record SeedData(Guid TenantId, Guid TenantUserId, Guid PlatformUserId, Guid ActivityTypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class TaskCoreTests_Postgres
    : TaskCoreTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public TaskCoreTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class TaskCoreTests_SqlServer
    : TaskCoreTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public TaskCoreTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
