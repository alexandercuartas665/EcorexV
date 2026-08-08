using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de los campos personalizados de la tarea POR TABLERO (ADR-0065) en la
/// matriz dual PostgreSQL / SQL Server, reutilizando los fixtures de TenantIsolation. Cubre:
/// (a) las definiciones estan alcanzadas por tablero (un tablero no ve las del otro),
/// (b) round-trip de valores en TaskItem.CustomFieldsJson (guardar -> releer),
/// (c) los campos Calculated se recalculan reusando el motor compartido,
/// (d) aislamiento cross-tenant (un tenant no ve los campos del otro).
/// </summary>
public abstract class TaskFieldTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected TaskFieldTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Definitions_AreScopedPerBoard()
    {
        var seed = await SeedTenantAsync("Campos scope tablero");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var boards = BuildBoardService(ctx, tenantContext);
        var fields = new TaskFieldService(ctx, tenantContext);

        var boardA = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Tablero A"), seed.PlatformUserId, "Tester")).Value!;
        var boardB = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Tablero B"), seed.PlatformUserId, "Tester")).Value!;

        var inA = await fields.CreateFieldAsync(new CreateTaskFieldRequest(boardA.Id, "Monto aprobado", TerceroFieldType.Currency));
        Assert.NotNull(inA);
        await fields.CreateFieldAsync(new CreateTaskFieldRequest(boardB.Id, "Referencia externa", TerceroFieldType.Text));

        var listA = await fields.ListByBoardAsync(boardA.Id);
        var listB = await fields.ListByBoardAsync(boardB.Id);

        Assert.Single(listA);
        Assert.Equal("Monto aprobado", listA[0].Label);
        Assert.Single(listB);
        Assert.Equal("Referencia externa", listB[0].Label);
        // El campo del tablero A NO aparece en el tablero B (alcance por BoardId).
        Assert.DoesNotContain(listB, f => f.FieldKey == inA!.FieldKey);

        // Creacion contra un tablero inexistente se rechaza (null).
        Assert.Null(await fields.CreateFieldAsync(new CreateTaskFieldRequest(Guid.NewGuid(), "Fantasma", TerceroFieldType.Text)));
    }

    [Fact]
    public async Task CustomFieldValues_RoundTrip_ThroughCustomFieldsJson()
    {
        var seed = await SeedTenantAsync("Campos round-trip");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var boards = BuildBoardService(ctx, tenantContext);
        var tasks = BuildTaskService(ctx, tenantContext);
        var fields = new TaskFieldService(ctx, tenantContext);

        var board = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Round trip"), seed.PlatformUserId, "Tester")).Value!;
        var firstColumn = await ctx.TaskBoardColumns.AsNoTracking()
            .Where(c => c.BoardId == board.Id).OrderBy(c => c.SortOrder).FirstAsync();
        var refField = (await fields.CreateFieldAsync(new CreateTaskFieldRequest(board.Id, "Referencia", TerceroFieldType.Text)))!;

        var task = (await tasks.CreateAsync(
            new CreateTaskItemRequest("Tarea con campos", seed.ActivityTypeId, BoardId: board.Id, ColumnId: firstColumn.Id),
            seed.PlatformUserId, "Tester")).Value!;

        var payload = System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string?> { [refField.FieldKey] = "ABC-123" });
        var saved = await tasks.UpdateCustomFieldsAsync(task.Item.Id, payload, seed.PlatformUserId, "Tester");
        Assert.True(saved.IsOk, saved.Error);

        // Releer desde el detalle: el valor sobrevive el guardado.
        var reloaded = await tasks.GetDetailAsync(task.Item.Id);
        Assert.NotNull(reloaded!.CustomFieldsJson);
        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(reloaded.CustomFieldsJson!)!;
        Assert.Equal("ABC-123", dict[refField.FieldKey]);
    }

    [Fact]
    public async Task Calculated_IsRecomputed_FromInputs()
    {
        var seed = await SeedTenantAsync("Campos calculado");
        await using var ctx = _fixture.CreateContext(seed.TenantId);
        var tenantContext = new TestTenantContext(seed.TenantId, seed.PlatformUserId);
        var boards = BuildBoardService(ctx, tenantContext);
        var fields = new TaskFieldService(ctx, tenantContext);

        var board = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Calculo"), seed.PlatformUserId, "Tester")).Value!;
        var horas = (await fields.CreateFieldAsync(new CreateTaskFieldRequest(board.Id, "Horas", TerceroFieldType.Number)))!;
        var tarifa = (await fields.CreateFieldAsync(new CreateTaskFieldRequest(board.Id, "Tarifa", TerceroFieldType.Currency)))!;
        var total = await fields.CreateFieldAsync(new CreateTaskFieldRequest(
            board.Id, "Total", TerceroFieldType.Calculated, Formula: $"{{{horas.FieldKey}}} * {{{tarifa.FieldKey}}}"));
        Assert.NotNull(total);

        var values = new Dictionary<string, string?>
        {
            [horas.FieldKey] = "8",
            [tarifa.FieldKey] = "150"
        };
        var computed = await fields.ComputeCalculatedAsync(board.Id, values);

        Assert.True(computed.TryGetValue(total!.FieldKey, out var result));
        Assert.Equal(1200d, double.Parse(result!, System.Globalization.CultureInfo.InvariantCulture), 3);

        // Una formula que referencia una clave inexistente se rechaza al crear (null).
        Assert.Null(await fields.CreateFieldAsync(new CreateTaskFieldRequest(
            board.Id, "Rota", TerceroFieldType.Calculated, Formula: "{no_existe} + 1")));
    }

    [Fact]
    public async Task Fields_AreIsolatedPerTenant()
    {
        var seedA = await SeedTenantAsync("Campos tenant A");
        var seedB = await SeedTenantAsync("Campos tenant B");

        Guid boardAId;
        await using (var ctxA = _fixture.CreateContext(seedA.TenantId))
        {
            var ctxTenant = new TestTenantContext(seedA.TenantId, seedA.PlatformUserId);
            var boards = BuildBoardService(ctxA, ctxTenant);
            var fields = new TaskFieldService(ctxA, ctxTenant);
            var board = (await boards.CreateBoardAsync(new CreateActivityBoardRequest("Board A"), seedA.PlatformUserId, "Tester")).Value!;
            boardAId = board.Id;
            await fields.CreateFieldAsync(new CreateTaskFieldRequest(board.Id, "Secreto A", TerceroFieldType.Text));
        }

        // El tenant B, con su propio contexto, no ve ningun campo (ni el board del tenant A).
        await using (var ctxB = _fixture.CreateContext(seedB.TenantId))
        {
            var fieldsB = new TaskFieldService(ctxB, new TestTenantContext(seedB.TenantId, seedB.PlatformUserId));
            Assert.Empty(await fieldsB.ListAllAsync());
            // Aun conociendo el Id del board del tenant A, no puede leer sus campos.
            Assert.Empty(await fieldsB.ListByBoardAsync(boardAId));
        }
    }

    // ---- Helpers (mismos patrones que ActivityBoardTests) ----

    private static ActivityBoardService BuildBoardService(EcorexDbContext ctx, ITenantContext tenantContext)
        => new(ctx, tenantContext, new SequenceService(ctx, tenantContext),
            BuildTaskService(ctx, tenantContext), new AuditWriter(ctx),
            new Ecorex.Application.Organization.NodeAssigneeResolver(ctx));

    private static TaskItemService BuildTaskService(EcorexDbContext ctx, ITenantContext tenantContext)
        => new(ctx, tenantContext, new SequenceService(ctx, tenantContext),
            new WorkflowEngine(ctx, tenantContext, new NoOpWorkflowRuleHook(), new NoOpTaskBroadcaster()), new NoOpEmailSender(),
            new Ecorex.Application.Organization.NodeAssigneeResolver(ctx));

    private async Task<SeedData> SeedTenantAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await ctx.SaveChangesAsync();
        }

        Guid platformUserId, activityTypeId;
        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            var ownerPlatform = new PlatformUser
            {
                Email = $"owner-{tenantId:N}@fields.test",
                DisplayName = "Owner Fields",
                EmailVerified = true,
                Status = PlatformUserStatus.Active
            };
            ctx.PlatformUsers.Add(ownerPlatform);
            ctx.TenantUsers.Add(new TenantUser
            {
                TenantId = tenantId,
                PlatformUserId = ownerPlatform.Id,
                Email = ownerPlatform.Email
            });
            var activityType = new ActivityType { TenantId = tenantId, Category = "General", Name = "Prueba" };
            ctx.ActivityTypes.Add(activityType);
            await ctx.SaveChangesAsync();
            platformUserId = ownerPlatform.Id;
            activityTypeId = activityType.Id;
        }

        return new SeedData(tenantId, platformUserId, activityTypeId);
    }

    private sealed record SeedData(Guid TenantId, Guid PlatformUserId, Guid ActivityTypeId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class TaskFieldTests_Postgres
    : TaskFieldTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public TaskFieldTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class TaskFieldTests_SqlServer
    : TaskFieldTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public TaskFieldTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
