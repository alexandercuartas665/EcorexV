using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Authoring;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion de la autoria por IA (Ola 4, ADR-0051) en matriz dual PostgreSQL / SQL Server.
///
/// El LLM se falsea (IReportSpecGenerator) para probar el PIPELINE DETERMINISTA: instruccion -> JSON-spec
/// -> validacion contra el catalogo (guardrail) -> ejecucion via el datasource tenant-safe -> option de
/// ECharts. Cubre un caso sobre entidad NATIVA y otro sobre CONTENEDOR, el rechazo de un campo fuera del
/// catalogo, y la persistencia (ReportDefinition) con aislamiento cross-tenant.
/// </summary>
public abstract class ReportAuthoringTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ReportAuthoringTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Author_Native_BarByStatus_ProducesRunnableSpecAndOption()
    {
        var tenant = await SeedTasksAsync("Author Native", new[]
        {
            ("N-1", TaskItemStatus.Pending), ("N-2", TaskItemStatus.Pending), ("N-3", TaskItemStatus.Done)
        });

        var json = """
        { "title":"Actividades por estado", "sourceKey":"native:taskitem", "chart":"Bar",
          "groupBy":["Status"], "aggregates":[{"field":"Status","function":"Count"}] }
        """;

        var result = await AuthorAsync(tenant, json);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.Spec);
        Assert.Equal(ReportChartKind.Bar, result.Spec!.Chart);
        Assert.NotNull(result.DataSet);
        Assert.NotEmpty(result.DataSet!.Rows);
        Assert.NotNull(result.Option); // Bar -> hay option de ECharts
    }

    [Fact]
    public async Task Author_Container_SumByColumn_ProducesRunnableSpec()
    {
        var c = await SeedContainerAsync("Author Cont", new[] { ("Norte", "100"), ("Norte", "50"), ("Sur", "200") });

        var json = $$"""
        { "title":"Ventas por region", "sourceKey":"{{ContainerReportReader.KeyFor(c.ContainerId)}}",
          "chart":"Bar", "groupBy":["{{c.RegionColId}}"],
          "aggregates":[{"field":"{{c.MontoColId}}","function":"Sum"}] }
        """;

        var result = await AuthorAsync(c.TenantId, json);

        Assert.True(result.Ok, result.Error);
        Assert.NotNull(result.DataSet);
        var sums = result.DataSet!.Rows.ToDictionary(r => (string?)r[0], r => Convert.ToDecimal(r[1]));
        Assert.Equal(150m, sums["Norte"]);
        Assert.Equal(200m, sums["Sur"]);
    }

    [Fact]
    public async Task Author_UnknownField_IsRejected()
    {
        var tenant = await SeedTasksAsync("Author Guard", new[] { ("G-1", TaskItemStatus.Pending) });

        var json = """
        { "title":"x", "sourceKey":"native:taskitem", "chart":"Table", "fields":["SalarioSecreto"] }
        """;

        var result = await AuthorAsync(tenant, json);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SavedDefinition_RunsAndIsTenantIsolated()
    {
        var a = await SeedTasksAsync("Def A", new[] { ("A-1", TaskItemStatus.Pending), ("A-2", TaskItemStatus.Done) });
        var b = await SeedTasksAsync("Def B", new[] { ("B-1", TaskItemStatus.Pending) });

        var spec = new ReportSpec
        {
            Title = "Estados A",
            SourceKey = TaskItemReportSource.SourceKey,
            Chart = ReportChartKind.Bar,
            GroupBy = { "Status" },
            Aggregates = { new ReportAggregateSpec { Field = "Status", Function = ReportAggregateFunction.Count } }
        };

        // Guardar + ejecutar como tenant A.
        Guid savedId;
        await using (var ctx = _fixture.CreateContext(a))
        {
            var svc = BuildDefinitionService(ctx, a);
            savedId = await svc.SaveAsync(spec, "demo", default);
            var list = await svc.ListAsync(seeAll: true, rolId: null);
            Assert.Single(list);
            var run = await svc.RunAsync(savedId);
            Assert.NotNull(run);
            Assert.NotEmpty(run!.DataSet.Rows);
        }

        // Tenant B: el reporte de A no existe en su lista, ni se puede obtener ni ejecutar.
        await using (var ctx = _fixture.CreateContext(b))
        {
            var svc = BuildDefinitionService(ctx, b);
            Assert.Empty(await svc.ListAsync(seeAll: true, rolId: null));
            Assert.Null(await svc.GetAsync(savedId));
            Assert.Null(await svc.RunAsync(savedId));
        }
    }

    // ---- Helpers ----

    private async Task<ReportAuthoringResult> AuthorAsync(Guid tenantId, string cannedJson)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var tenantCtx = new TestTenantContext(tenantId);
        var native = new IReportableSource[] { new TaskItemReportSource(ctx) };
        var containers = new ContainerReportReader(ctx);
        var external = ExternalTestDoubles.Reader(ctx);
        var forms = new FormResponseReportReader(ctx);
        var catalog = new ReportCatalog(native, containers, external, forms, tenantCtx, ctx);
        var dataSource = new ReportDataSource(catalog, native, containers, external, forms, tenantCtx);
        var authoring = new ReportAuthoringService(catalog, dataSource, new FakeGenerator(cannedJson), tenantCtx);
        return await authoring.AuthorAsync("instruccion de prueba");
    }

    private ReportDefinitionService BuildDefinitionService(EcorexDbContext ctx, Guid tenantId)
    {
        var tenantCtx = new TestTenantContext(tenantId);
        var native = new IReportableSource[] { new TaskItemReportSource(ctx) };
        var containers = new ContainerReportReader(ctx);
        var external = ExternalTestDoubles.Reader(ctx);
        var forms = new FormResponseReportReader(ctx);
        var catalog = new ReportCatalog(native, containers, external, forms, tenantCtx, ctx);
        var dataSource = new ReportDataSource(catalog, native, containers, external, forms, tenantCtx);
        return new ReportDefinitionService(ctx, tenantCtx, dataSource);
    }

    private async Task<Guid> SeedTasksAsync(string tenantName, (string Number, TaskItemStatus Status)[] tasks)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            foreach (var (number, status) in tasks)
            {
                ctx.TaskItems.Add(new TaskItem
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Number = number,
                    Title = number,
                    Status = status,
                    Priority = TaskPriority.Medium
                });
            }

            await ctx.SaveChangesAsync();
        }

        return tenantId;
    }

    private async Task<ContainerSeed> SeedContainerAsync(string tenantName, (string Region, string Monto)[] rows)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        var modelId = Guid.CreateVersion7();
        var containerId = Guid.CreateVersion7();
        var regionCol = Guid.CreateVersion7();
        var montoCol = Guid.CreateVersion7();

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            ctx.DataModels.Add(new DataModel { Id = modelId, TenantId = tenantId, Name = "Modelo " + tenantName });
            ctx.DataContainers.Add(new DataContainer { Id = containerId, TenantId = tenantId, ModelId = modelId, Name = "Ventas" });
            ctx.DataContainerColumns.Add(new DataContainerColumn { Id = regionCol, TenantId = tenantId, ContainerId = containerId, Name = "Region", Type = DataContainerColumnType.Text, SortOrder = 0 });
            ctx.DataContainerColumns.Add(new DataContainerColumn { Id = montoCol, TenantId = tenantId, ContainerId = containerId, Name = "Monto", Type = DataContainerColumnType.Decimal, SortOrder = 1 });

            foreach (var (region, monto) in rows)
            {
                var rowId = Guid.CreateVersion7();
                ctx.DataContainerRows.Add(new DataContainerRow { Id = rowId, TenantId = tenantId, ContainerId = containerId });
                ctx.DataContainerCells.Add(new DataContainerCell { Id = Guid.CreateVersion7(), TenantId = tenantId, RowId = rowId, ColumnId = regionCol, Value = region });
                ctx.DataContainerCells.Add(new DataContainerCell { Id = Guid.CreateVersion7(), TenantId = tenantId, RowId = rowId, ColumnId = montoCol, Value = monto });
            }

            await ctx.SaveChangesAsync();
        }

        return new ContainerSeed(tenantId, containerId, regionCol, montoCol);
    }

    private sealed record ContainerSeed(Guid TenantId, Guid ContainerId, Guid RegionColId, Guid MontoColId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }

    /// <summary>Doble del LLM: devuelve un JSON-spec fijo, para probar el pipeline sin proveedor real.</summary>
    private sealed class FakeGenerator : IReportSpecGenerator
    {
        private readonly string _json;
        public FakeGenerator(string json) => _json = json;

        public Task<ReportGenerationResult> GenerateAsync(string instruction, string catalogText, CancellationToken ct = default) =>
            Task.FromResult(new ReportGenerationResult(true, _json, null));
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class ReportAuthoringTests_Postgres
    : ReportAuthoringTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ReportAuthoringTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture) { }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class ReportAuthoringTests_SqlServer
    : ReportAuthoringTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ReportAuthoringTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture) { }
}
