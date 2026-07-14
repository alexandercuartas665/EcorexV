using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de los tableros de actividades unificados (ADR-0020) en matriz
/// dual PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// creacion de tablero Activities con code PRY autogenerado y columnas default, quick-create
/// que cuelga la tarea con consecutivo T unico, filtros combinables del detalle (columna,
/// asignado encargado-O-assignment, tag, fecha hoy) con contadores por alcance, transicion
/// oportunista a Done al mover a columna IsDone (y movimiento sin cambio cuando la maquina
/// no lo permite), progreso por checklist y aislamiento cross-tenant.
/// </summary>
public abstract class ActivityBoardTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ActivityBoardTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateBoard_AutogeneratesCode_AndSeedsDefaultColumns()
    {
        var seed = await SeedTenantAsync("Boards Codigo");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var service = BuildBoardService(ctx, new TestTenantContext(seed.TenantId, seed.PlatformUserId));

        var first = await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Comercial - Infraestructura",
                Description: "Tablero demo", DueDate: DateTimeOffset.UtcNow.AddDays(8)),
            seed.PlatformUserId, "Tester");
        Assert.True(first.IsOk, first.Error);
        Assert.Equal("PRY-0001", first.Value!.Code);
        Assert.Equal(TaskBoardStatus.InProgress, first.Value.Status);

        // Columnas default del prototipo, la ultima IsDone.
        var columns = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == first.Value.Id)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
        Assert.Equal(new[] { "Por hacer", "En progreso", "En revision", "Completado" },
            columns.Select(c => c.Name).ToArray());
        Assert.Equal(new[] { false, false, false, true }, columns.Select(c => c.IsDone).ToArray());

        // El consecutivo avanza y el tablero queda Kind = Activities (no toca el CRM legacy).
        var second = await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Segundo tablero"), seed.PlatformUserId, "Tester");
        Assert.True(second.IsOk, second.Error);
        Assert.Equal("PRY-0002", second.Value!.Code);
        var kinds = await ctx.TaskBoards.AsNoTracking().Select(b => b.Kind).Distinct().ToListAsync();
        Assert.Equal(new[] { TaskBoardKind.Activities }, kinds);

        // Codigo explicito y codigo duplicado.
        var custom = await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Con codigo", Code: "PRY-9999"), seed.PlatformUserId, "Tester");
        Assert.Equal("PRY-9999", custom.Value!.Code);
        var duplicate = await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Duplicado", Code: "PRY-9999"), seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, duplicate.Status);
    }

    [Fact]
    public async Task QuickCreate_HangsTaskOnBoardColumn_WithUniqueConsecutive()
    {
        var seed = await SeedTenantAsync("Boards QuickCreate");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildBoardService(ctx, tenantContext);

        var board = (await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Tablero quick"), seed.PlatformUserId, "Tester")).Value!;
        var firstColumn = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).FirstAsync();

        var t1 = await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, firstColumn.Id, "Cotizar equipos de red",
            Priority: TaskPriority.High, AssigneeTenantUserId: seed.OwnerUserId,
            DueDate: DateTimeOffset.UtcNow.AddDays(3)), seed.PlatformUserId, "Tester");
        var t2 = await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, firstColumn.Id, "Migrar formulario a EAV"), seed.PlatformUserId, "Tester");
        Assert.True(t1.IsOk, t1.Error);
        Assert.True(t2.IsOk, t2.Error);

        // Consecutivo T unico y correlativo, misma secuencia que CreateAsync.
        Assert.Equal("T00001", t1.Value!.Item.Number);
        Assert.Equal("T00002", t2.Value!.Item.Number);

        // Colgadas del tablero/columna con orden incremental.
        Assert.Equal(board.Id, t1.Value.Item.BoardId);
        Assert.Equal(firstColumn.Id, t1.Value.Item.ColumnId);
        var rows = await ctx.TaskItems.AsNoTracking()
            .Where(t => t.BoardId == board.Id)
            .OrderBy(t => t.BoardSortOrder)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 0, 1 }, rows.Select(r => r.BoardSortOrder).ToArray());

        // Columna de otro tablero -> Invalid (coherencia ColumnId/BoardId).
        var otherBoard = (await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Otro"), seed.PlatformUserId, "Tester")).Value!;
        var wrongColumn = await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            otherBoard.Id, firstColumn.Id, "Columna ajena"), seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, wrongColumn.Status);
    }

    [Fact]
    public async Task BoardDetail_CombinableFilters_AndScopeCounters()
    {
        var seed = await SeedTenantAsync("Boards Filtros");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildBoardService(ctx, tenantContext);
        var tasks = BuildTaskService(ctx, tenantContext);

        var board = (await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Tablero filtros"), seed.PlatformUserId, "Tester")).Value!;
        var columns = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).ToListAsync();
        var today = DateTimeOffset.UtcNow;

        // t1: encargado OWNER, tag, vence HOY, columna 0.
        var tag = (await tasks.CreateTagAsync("Infraestructura", "#3b82f6")).Value!;
        var t1 = (await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, columns[0].Id, "T1 del owner", AssigneeTenantUserId: seed.OwnerUserId,
            DueDate: today, TagIds: [tag.Id]), seed.PlatformUserId, "Tester")).Value!;
        // t2: encargado OTRO usuario, pero el OWNER va de asignado M:N; columna 1, vence en 5 dias.
        var t2 = (await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, columns[1].Id, "T2 con owner asignado", AssigneeTenantUserId: seed.SecondUserId,
            DueDate: today.AddDays(5)), seed.PlatformUserId, "Tester")).Value!;
        Assert.True((await tasks.AddAssigneeAsync(t2.Item.Id, seed.OwnerUserId, seed.PlatformUserId, "Tester")).IsOk);
        // t3: SIN encargado y SIN asignados, columna 0, sin fecha.
        var t3 = (await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, columns[0].Id, "T3 sin asignar"), seed.PlatformUserId, "Tester")).Value!;
        // t4: encargado OTRO usuario, columna 2.
        var t4 = (await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, columns[2].Id, "T4 de otro", AssigneeTenantUserId: seed.SecondUserId),
            seed.PlatformUserId, "Tester")).Value!;

        // Sin filtros: 4 tarjetas y contadores team 4 / mine(owner) 2 / unassigned 1.
        var all = await service.GetBoardDetailAsync(board.Id,
            new ActivityBoardDetailFilter(CurrentTenantUserId: seed.OwnerUserId));
        Assert.True(all.IsOk, all.Error);
        Assert.Equal(4, all.Value!.Columns.Sum(c => c.Cards.Count));
        Assert.Equal(new ActivityScopeCountersDto(4, 2, 1), all.Value.ScopeCounters);

        // Filtro por columna.
        var byColumn = await service.GetBoardDetailAsync(board.Id,
            new ActivityBoardDetailFilter(ColumnIds: [columns[0].Id], CurrentTenantUserId: seed.OwnerUserId));
        Assert.Equal(new[] { "T1 del owner", "T3 sin asignar" },
            byColumn.Value!.Columns.SelectMany(c => c.Cards).Select(c => c.Title).OrderBy(t => t).ToArray());

        // Filtro por asignado: matchea encargado (t1) Y asignacion M:N (t2).
        var byAssignee = await service.GetBoardDetailAsync(board.Id,
            new ActivityBoardDetailFilter(AssigneeTenantUserIds: [seed.OwnerUserId], CurrentTenantUserId: seed.OwnerUserId));
        Assert.Equal(new[] { t1.Item.Id, t2.Item.Id }.OrderBy(id => id).ToArray(),
            byAssignee.Value!.Columns.SelectMany(c => c.Cards).Select(c => c.Id).OrderBy(id => id).ToArray());

        // Filtro por tag.
        var byTag = await service.GetBoardDetailAsync(board.Id,
            new ActivityBoardDetailFilter(TagIds: [tag.Id], CurrentTenantUserId: seed.OwnerUserId));
        var tagCard = Assert.Single(byTag.Value!.Columns.SelectMany(c => c.Cards));
        Assert.Equal(t1.Item.Id, tagCard.Id);
        Assert.Equal("Infraestructura", Assert.Single(tagCard.Tags).Name);

        // Filtro fecha limite HOY (corte de dia UTC).
        var byToday = await service.GetBoardDetailAsync(board.Id,
            new ActivityBoardDetailFilter(Due: ActivityDueFilter.Today, CurrentTenantUserId: seed.OwnerUserId));
        var todayCard = Assert.Single(byToday.Value!.Columns.SelectMany(c => c.Cards));
        Assert.Equal(t1.Item.Id, todayCard.Id);
        // ... y los contadores de alcance respetan los filtros aplicados.
        Assert.Equal(new ActivityScopeCountersDto(1, 1, 0), byToday.Value.ScopeCounters);

        // Alcances: mine trae t1 (encargado) y t2 (asignado); unassigned trae t3.
        var mine = await service.GetBoardDetailAsync(board.Id,
            new ActivityBoardDetailFilter(Scope: ActivityBoardScope.Mine, CurrentTenantUserId: seed.OwnerUserId));
        Assert.Equal(new[] { t1.Item.Id, t2.Item.Id }.OrderBy(id => id).ToArray(),
            mine.Value!.Columns.SelectMany(c => c.Cards).Select(c => c.Id).OrderBy(id => id).ToArray());
        var unassigned = await service.GetBoardDetailAsync(board.Id,
            new ActivityBoardDetailFilter(Scope: ActivityBoardScope.Unassigned));
        Assert.Equal(t3.Item.Id, Assert.Single(unassigned.Value!.Columns.SelectMany(c => c.Cards)).Id);

        // Filtros combinables AND: columna 0 + asignado owner = solo t1.
        var combined = await service.GetBoardDetailAsync(board.Id, new ActivityBoardDetailFilter(
            ColumnIds: [columns[0].Id], AssigneeTenantUserIds: [seed.OwnerUserId],
            CurrentTenantUserId: seed.OwnerUserId));
        Assert.Equal(t1.Item.Id, Assert.Single(combined.Value!.Columns.SelectMany(c => c.Cards)).Id);
        _ = t4; // t4 solo aporta al conteo del equipo
    }

    [Fact]
    public async Task MoveTask_ToDoneColumn_CompletesStatusOnlyWhenMachineAllows()
    {
        var seed = await SeedTenantAsync("Boards MoveTask");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildBoardService(ctx, tenantContext);

        var board = (await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Tablero move"), seed.PlatformUserId, "Tester")).Value!;
        var columns = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).ToListAsync();
        var doneColumn = columns.Single(c => c.IsDone);

        // Caso 1: tarea ACTIVA (nace asignada) -> Active -> Done es transicion valida.
        var active = (await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, columns[0].Id, "Tarea activa", AssigneeTenantUserId: seed.OwnerUserId),
            seed.PlatformUserId, "Tester")).Value!;
        Assert.Equal(TaskItemStatus.Active, active.Item.Status);
        var moved = await service.MoveTaskAsync(active.Item.Id, doneColumn.Id, 0, seed.PlatformUserId, "Tester");
        Assert.True(moved.IsOk, moved.Error);
        Assert.True(moved.Value!.StatusChangedToDone);
        Assert.Equal(TaskItemStatus.Done, moved.Value.Status);
        Assert.Null(moved.Value.StatusNote);

        // Caso 2: tarea PENDING -> Pending -> Done NO esta permitida: la tarjeta se mueve
        // igual, el estado queda intacto y el resultado lo reporta (no rompe).
        var pending = (await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, columns[0].Id, "Tarea pendiente"), seed.PlatformUserId, "Tester")).Value!;
        Assert.Equal(TaskItemStatus.Pending, pending.Item.Status);
        var movedPending = await service.MoveTaskAsync(pending.Item.Id, doneColumn.Id, 1, seed.PlatformUserId, "Tester");
        Assert.True(movedPending.IsOk, movedPending.Error);
        Assert.False(movedPending.Value!.StatusChangedToDone);
        Assert.Equal(TaskItemStatus.Pending, movedPending.Value.Status);
        Assert.NotNull(movedPending.Value.StatusNote);

        // Ambas tarjetas quedaron en la columna final; la actividad registro el movimiento.
        var inDone = await ctx.TaskItems.AsNoTracking()
            .Where(t => t.ColumnId == doneColumn.Id).CountAsync();
        Assert.Equal(2, inDone);
        var texts = await ctx.TaskItemActivities.AsNoTracking()
            .Where(a => a.TaskItemId == active.Item.Id).Select(a => a.Text).ToListAsync();
        Assert.Contains(texts, t => t.StartsWith("movio la tarea a la columna"));
        Assert.Contains(texts, t => t.Contains("cambio el estado de Active a Done"));

        // Columna de otro tablero -> Invalid.
        var otherBoard = (await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Otro board"), seed.PlatformUserId, "Tester")).Value!;
        var foreignColumn = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == otherBoard.Id).OrderBy(c => c.SortOrder).FirstAsync();
        var wrong = await service.MoveTaskAsync(active.Item.Id, foreignColumn.Id, 0, seed.PlatformUserId, "Tester");
        Assert.Equal(TaskCoreStatus.Invalid, wrong.Status);
    }

    [Fact]
    public async Task ChecklistToggle_UpdatesCardAndBoardProgress()
    {
        var seed = await SeedTenantAsync("Boards Checklist");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var service = BuildBoardService(ctx, tenantContext);
        var tasks = BuildTaskService(ctx, tenantContext);

        var board = (await service.CreateBoardAsync(
            new CreateActivityBoardRequest("Tablero checklist"), seed.PlatformUserId, "Tester")).Value!;
        var firstColumn = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).FirstAsync();
        var task = (await service.QuickCreateTaskAsync(new QuickCreateTaskRequest(
            board.Id, firstColumn.Id, "Con checklist"), seed.PlatformUserId, "Tester")).Value!;

        var i1 = (await tasks.AddChecklistItemAsync(task.Item.Id, "Pedir cotizaciones", seed.PlatformUserId, "Tester")).Value!;
        var i2 = (await tasks.AddChecklistItemAsync(task.Item.Id, "Comparar proveedores", seed.PlatformUserId, "Tester")).Value!;
        Assert.Equal(new[] { 0, 1 }, new[] { i1.SortOrder, i2.SortOrder });

        // Toggle del primero: progreso de tarjeta 1/2 (50%) y del indice 50% (via checklist).
        var toggled = await tasks.ToggleChecklistItemAsync(i1.Id, true, seed.OwnerUserId, seed.PlatformUserId, "Tester");
        Assert.True(toggled.IsOk, toggled.Error);
        Assert.True(toggled.Value!.IsCompleted);
        Assert.NotNull(toggled.Value.CompletedAt);
        Assert.Equal(seed.OwnerUserId, toggled.Value.CompletedByTenantUserId);

        var detail = await service.GetBoardDetailAsync(board.Id, new ActivityBoardDetailFilter());
        var card = Assert.Single(detail.Value!.Columns.SelectMany(c => c.Cards));
        Assert.Equal((1, 2, 50), (card.ChecklistDone, card.ChecklistTotal, card.ChecklistPct));

        var index = await service.ListBoardsAsync(new ActivityBoardIndexFilter());
        var boardRow = Assert.Single(index.Boards, b => b.Id == board.Id);
        Assert.Equal(50, boardRow.ProgressPct);

        // Completar la tarea via actividad quedo trazada.
        var activityTexts = await ctx.TaskItemActivities.AsNoTracking()
            .Where(a => a.TaskItemId == task.Item.Id).Select(a => a.Text).ToListAsync();
        Assert.Contains(activityTexts, t => t.Contains("completo el item de checklist"));

        // Destoggle: vuelve a 0/2 y limpia el rastro de completado.
        var untoggled = await tasks.ToggleChecklistItemAsync(i1.Id, false, null, seed.PlatformUserId, "Tester");
        Assert.False(untoggled.Value!.IsCompleted);
        Assert.Null(untoggled.Value.CompletedAt);
        Assert.Null(untoggled.Value.CompletedByTenantUserId);
    }

    [Fact]
    public async Task Boards_Checklists_Assignments_AreIsolatedBetweenTenants()
    {
        var seedA = await SeedTenantAsync("Boards Tenant A");
        var seedB = await SeedTenantAsync("Boards Tenant B");

        Guid boardId, taskId;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var tenantContextA = new TestTenantContext(seedA.TenantId, seedA.PlatformUserId);
            var serviceA = BuildBoardService(ctxA, tenantContextA);
            var tasksA = BuildTaskService(ctxA, tenantContextA);
            var board = (await serviceA.CreateBoardAsync(
                new CreateActivityBoardRequest("Privado de A"), seedA.PlatformUserId, "Tester")).Value!;
            var column = await ctxA.TaskBoardColumns.AsNoTracking()
                .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).FirstAsync();
            var task = (await serviceA.QuickCreateTaskAsync(new QuickCreateTaskRequest(
                board.Id, column.Id, "Tarea privada de A"), seedA.PlatformUserId, "Tester")).Value!;
            boardId = board.Id;
            taskId = task.Item.Id;
            Assert.True((await tasksA.AddChecklistItemAsync(taskId, "Item privado", seedA.PlatformUserId, "Tester")).IsOk);
            Assert.True((await tasksA.AddAssigneeAsync(taskId, seedA.OwnerUserId, seedA.PlatformUserId, "Tester")).IsOk);
        }

        // El tenant B no ve NADA de A: ni el indice, ni el detalle por id, ni las tablas.
        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            var tenantContextB = new TestTenantContext(seedB.TenantId, seedB.PlatformUserId);
            var serviceB = BuildBoardService(ctxB, tenantContextB);
            var index = await serviceB.ListBoardsAsync(new ActivityBoardIndexFilter(IncludeArchived: true));
            Assert.Empty(index.Boards);
            Assert.Equal(0, index.Kpis.TotalBoards);
            var detail = await serviceB.GetBoardDetailAsync(boardId, new ActivityBoardDetailFilter());
            Assert.Equal(TaskCoreStatus.NotFound, detail.Status);
            Assert.Empty(await ctxB.TaskBoards.ToListAsync());
            Assert.Empty(await ctxB.TaskItemChecklistItems.ToListAsync());
            Assert.Empty(await ctxB.TaskItemAssignments.ToListAsync());
            // Y tampoco puede operar sobre ellas.
            var move = await serviceB.MoveTaskAsync(taskId, Guid.CreateVersion7(), 0, seedB.PlatformUserId, "Tester");
            Assert.Equal(TaskCoreStatus.NotFound, move.Status);
        }

        // El tenant A si ve lo suyo.
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            Assert.Single(await ctxA.TaskBoards.Where(b => b.Id == boardId).ToListAsync());
            Assert.Single(await ctxA.TaskItemChecklistItems.ToListAsync());
            Assert.Single(await ctxA.TaskItemAssignments.ToListAsync());
        }
    }

    // ---- Helpers ----

    private static ActivityBoardService BuildBoardService(EcorexDbContext ctx, ITenantContext tenantContext)
        => new(ctx, tenantContext, new SequenceService(ctx, tenantContext),
            BuildTaskService(ctx, tenantContext), new AuditWriter(ctx),
            new Ecorex.Application.Organization.NodeAssigneeResolver(ctx));

    /// <summary>Servicio de tareas con motor de flujos real y broadcaster no-op (sin SignalR).</summary>
    private static TaskItemService BuildTaskService(EcorexDbContext ctx, ITenantContext tenantContext)
        => new(ctx, tenantContext, new SequenceService(ctx, tenantContext),
            new WorkflowEngine(ctx, tenantContext, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster()), new NoOpEmailSender(),
            new Ecorex.Application.Organization.NodeAssigneeResolver(ctx));

    /// <summary>
    /// Siembra un tenant fresco con DOS TenantUsers (owner y segundo miembro) y un
    /// ActivityType, minimo necesario para tableros de actividades.
    /// </summary>
    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid ownerUserId, secondUserId, platformUserId, activityTypeId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var ownerPlatform = new PlatformUser
            {
                Email = $"owner-{tenantId:N}@boards.test",
                DisplayName = "Owner Boards",
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            var secondPlatform = new PlatformUser
            {
                Email = $"second-{tenantId:N}@boards.test",
                DisplayName = "Second Boards",
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            ctx.PlatformUsers.AddRange(ownerPlatform, secondPlatform);
            var ownerUser = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = ownerPlatform.Id,
                Email = ownerPlatform.Email
            };
            var secondUser = new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = secondPlatform.Id,
                Email = secondPlatform.Email
            };
            ctx.TenantUsers.AddRange(ownerUser, secondUser);
            var activityType = new ActivityType
            {
                TenantId = tenantId,
                Category = "General",
                Name = "Prueba"
            };
            ctx.ActivityTypes.Add(activityType);
            await ctx.SaveChangesAsync();
            ownerUserId = ownerUser.Id;
            secondUserId = secondUser.Id;
            platformUserId = ownerPlatform.Id;
            activityTypeId = activityType.Id;
        }

        return new SeedData(tenantId, ownerUserId, secondUserId, platformUserId, activityTypeId);
    }

    private sealed record SeedData(Guid TenantId, Guid OwnerUserId, Guid SecondUserId, Guid PlatformUserId, Guid ActivityTypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ActivityBoardTests_Postgres
    : ActivityBoardTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ActivityBoardTests_Postgres(PostgresTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ActivityBoardTests_SqlServer
    : ActivityBoardTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ActivityBoardTests_SqlServer(SqlServerTenantIsolationFixture fixture)
        : base(fixture)
    {
    }
}
