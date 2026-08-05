using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Tests de integracion del MOTOR DE REPORTES (Ola 1, ADR-0051) en matriz dual PostgreSQL / SQL Server.
///
/// Lo que fijan estas pruebas:
/// - El <see cref="IReportDataSource"/> traduce un spec declarativo (referencia SOLO el catalogo) a
///   una consulta EF que se resuelve EN EL SERVIDOR, tanto en modo tabular como agregado (group by +
///   Count sobre una entidad nativa, y group by + Sum sobre un contenedor EAV).
/// - AISLAMIENTO cross-tenant "imposible por construccion": un tenant que pide los datos de otro
///   obtiene VACIO (nativas: el filtro global no devuelve filas) o un error de fuente desconocida
///   (contenedores: la fuente del otro tenant ni siquiera existe en su catalogo). Corre en AMBOS motores.
/// - El limite de seguridad del catalogo: un campo fuera del catalogo se RECHAZA antes de tocar la BD.
/// </summary>
public abstract class ReportDataSourceTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected ReportDataSourceTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    // ---- Fuente NATIVA (TaskItem) ----

    [Fact]
    public async Task Native_Tabular_ReturnsRows_ForOwningTenant()
    {
        var a = await SeedTasksAsync("Native A", new[]
        {
            ("T-1", "Alfa", TaskItemStatus.Pending),
            ("T-2", "Beta", TaskItemStatus.Done)
        });

        var spec = new ReportQuerySpec
        {
            SourceKey = TaskItemReportSource.SourceKey,
            Fields = new[] { "Number", "Title", "Status" },
            Sort = new[] { new ReportSort("Number") }
        };

        var ds = await QueryAsync(a, spec);
        Assert.Equal(3, ds.Columns.Count);
        Assert.Equal(2, ds.RowCount);
        Assert.Equal("T-1", ds.Rows[0][0]);
        Assert.Equal("Alfa", ds.Rows[0][1]);
        Assert.Equal("Pending", ds.Rows[0][2]);
    }

    [Fact]
    public async Task Native_GroupByStatus_CountsInTheServer()
    {
        var a = await SeedTasksAsync("Native Agg", new[]
        {
            ("G-1", "a", TaskItemStatus.Pending),
            ("G-2", "b", TaskItemStatus.Pending),
            ("G-3", "c", TaskItemStatus.Done),
            ("G-4", "d", TaskItemStatus.InProgress)
        });

        var spec = new ReportQuerySpec
        {
            SourceKey = TaskItemReportSource.SourceKey,
            GroupBy = new[] { "Status" },
            Aggregates = new[] { new ReportAggregate("Status", ReportAggregateFunction.Count) }
        };

        var ds = await QueryAsync(a, spec);
        var counts = ds.Rows.ToDictionary(r => (string?)r[0], r => Convert.ToInt64(r[1]));
        Assert.Equal(2, counts["Pending"]);
        Assert.Equal(1, counts["Done"]);
        Assert.Equal(1, counts["InProgress"]);
    }

    [Fact]
    public async Task Native_IsTenantIsolated()
    {
        var a = await SeedTasksAsync("Native Tenant A", new[] { ("A-1", "Solo A", TaskItemStatus.Pending) });
        var b = await SeedTasksAsync("Native Tenant B", new[] { ("B-1", "Solo B", TaskItemStatus.Done) });

        var spec = new ReportQuerySpec
        {
            SourceKey = TaskItemReportSource.SourceKey,
            Fields = new[] { "Number", "Title" }
        };

        // La fuente nativa existe para todos, pero el tenant B solo ve SUS filas.
        var seenByB = await QueryAsync(b, spec);
        Assert.Single(seenByB.Rows);
        Assert.Equal("B-1", seenByB.Rows[0][0]);

        // Y no aparece ninguna fila de A entre las que ve B.
        Assert.DoesNotContain(seenByB.Rows, r => Equals(r[0], "A-1"));
    }

    // ---- Fuente CONTENEDOR (EAV) ----

    [Fact]
    public async Task Container_GroupBySum_AggregatesEavValues()
    {
        var c = await SeedContainerAsync("Cont Agg", new[]
        {
            ("Norte", "100"),
            ("Norte", "50"),
            ("Sur", "200")
        });

        var spec = new ReportQuerySpec
        {
            SourceKey = ContainerReportReader.KeyFor(c.ContainerId),
            GroupBy = new[] { c.RegionColId.ToString() },
            Aggregates = new[] { new ReportAggregate(c.MontoColId.ToString(), ReportAggregateFunction.Sum) }
        };

        var ds = await QueryAsync(c.TenantId, spec);
        var sums = ds.Rows.ToDictionary(r => (string?)r[0], r => Convert.ToDecimal(r[1]));
        Assert.Equal(150m, sums["Norte"]);
        Assert.Equal(200m, sums["Sur"]);
    }

    [Fact]
    public async Task Container_CrossTenant_SourceDoesNotExist()
    {
        var a = await SeedContainerAsync("Cont A", new[] { ("Norte", "10") });
        var b = await SeedContainerAsync("Cont B", new[] { ("Sur", "20") });

        var spec = new ReportQuerySpec
        {
            SourceKey = ContainerReportReader.KeyFor(a.ContainerId),
            Fields = new[] { a.RegionColId.ToString() }
        };

        // El tenant B pide el contenedor de A: su catalogo NO lo contiene -> fuente desconocida.
        await Assert.ThrowsAsync<ReportValidationException>(() => QueryAsync(b.TenantId, spec));

        // Y el catalogo del tenant B no lista el contenedor de A.
        await using var ctx = _fixture.CreateContext(b.TenantId);
        var catalog = BuildCatalog(ctx, b.TenantId);
        var sources = await catalog.GetSourcesAsync();
        Assert.DoesNotContain(sources, s => s.Key == ContainerReportReader.KeyFor(a.ContainerId));
    }

    // ---- Limite de seguridad del catalogo ----

    [Fact]
    public async Task UnknownField_IsRejected_BeforeHittingTheDb()
    {
        var a = await SeedTasksAsync("Guard", new[] { ("Z-1", "x", TaskItemStatus.Pending) });

        var spec = new ReportQuerySpec
        {
            SourceKey = TaskItemReportSource.SourceKey,
            Fields = new[] { "SalarioSecreto" } // no existe en el catalogo
        };

        await Assert.ThrowsAsync<ReportValidationException>(() => QueryAsync(a, spec));
    }

    // ---- Helpers ----

    private Task<ReportDataSet> QueryAsync(Guid tenantId, ReportQuerySpec spec) => RunAsync(tenantId, ds => ds.QueryAsync(spec, new ReportContext(tenantId)));

    private async Task<T> RunAsync<T>(Guid tenantId, Func<IReportDataSource, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var tenantCtx = new TestTenantContext(tenantId);
        var native = new IReportableSource[] { new TaskItemReportSource(ctx) };
        var containers = new ContainerReportReader(ctx);
        var external = ExternalTestDoubles.Reader(ctx);
        var catalog = new ReportCatalog(native, containers, external, tenantCtx, ctx);
        var datasource = new ReportDataSource(catalog, native, containers, external, tenantCtx);
        return await action(datasource);
    }

    private ReportCatalog BuildCatalog(EcorexDbContext ctx, Guid tenantId)
    {
        var native = new IReportableSource[] { new TaskItemReportSource(ctx) };
        return new ReportCatalog(native, new ContainerReportReader(ctx), ExternalTestDoubles.Reader(ctx), new TestTenantContext(tenantId), ctx);
    }

    private async Task<Guid> SeedTasksAsync(string tenantName, (string Number, string Title, TaskItemStatus Status)[] tasks)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var ctx = _fixture.CreateContext(tenantId: null))
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = tenantName });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _fixture.CreateContext(tenantId))
        {
            foreach (var (number, title, status) in tasks)
            {
                ctx.TaskItems.Add(new TaskItem
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Number = number,
                    Title = title,
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
            ctx.DataContainerColumns.Add(new DataContainerColumn
            {
                Id = regionCol,
                TenantId = tenantId,
                ContainerId = containerId,
                Name = "Region",
                Type = DataContainerColumnType.Text,
                SortOrder = 0
            });
            ctx.DataContainerColumns.Add(new DataContainerColumn
            {
                Id = montoCol,
                TenantId = tenantId,
                ContainerId = containerId,
                Name = "Monto",
                Type = DataContainerColumnType.Decimal,
                SortOrder = 1
            });

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
}

/// <summary>Matriz dual, motor PostgreSQL (contenedor efimero postgres:16-alpine).</summary>
public sealed class ReportDataSourceTests_Postgres
    : ReportDataSourceTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public ReportDataSourceTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server (contenedor efimero mssql/server:2022-latest).</summary>
public sealed class ReportDataSourceTests_SqlServer
    : ReportDataSourceTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public ReportDataSourceTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
