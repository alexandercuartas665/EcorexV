using Ecorex.Application.Analisis;
using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0158 Ola 1 en matriz dual PostgreSQL / SQL Server: las fuentes nuevas del modulo Analisis
/// (Directorio, bitacora del Directorio, horas trabajadas), los campos extendidos de Actividades y los
/// indicadores curados. Cada fuente se prueba con su AISLAMIENTO cross-tenant: el tenant B nunca ve
/// filas del tenant A, aunque las fuentes consulten en un contexto EFIMERO propio (fabrica).
/// </summary>
public abstract class AnalisisSourcesTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected AnalisisSourcesTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Tercero_GroupByPerfil_AndNormalizedNit()
    {
        var s = await SeedAsync("Analisis Dir");

        var ds = await QueryAsync(s.TenantId, new ReportQuerySpec
        {
            SourceKey = TerceroReportSource.SourceKey,
            GroupBy = new[] { "EsCliente" },
            Aggregates = new[] { new ReportAggregate(string.Empty, ReportAggregateFunction.Count) }
        });
        var counts = ds.Rows.ToDictionary(r => (bool)r[0]!, r => Convert.ToInt64(r[1]));
        Assert.Equal(1, counts[true]);
        Assert.Equal(1, counts[false]);

        var nit = await QueryAsync(s.TenantId, new ReportQuerySpec
        {
            SourceKey = TerceroReportSource.SourceKey,
            Fields = new[] { "Nombre", "NitNormalizado", "Perfiles" },
            Filters = new[] { ReportFilter.Eq("NitNormalizado", "900123456") }
        });
        Assert.Single(nit.Rows);
        Assert.Equal("ACME Industrial", nit.Rows[0][0]);
        Assert.Equal("Cliente, Proveedor", nit.Rows[0][2]);
    }

    [Fact]
    public async Task TerceroNota_SumsValorByAccion()
    {
        var s = await SeedAsync("Analisis Notas");

        var ds = await QueryAsync(s.TenantId, new ReportQuerySpec
        {
            SourceKey = TerceroNotaReportSource.SourceKey,
            GroupBy = new[] { "Accion" },
            Aggregates = new[] { new ReportAggregate("Valor", ReportAggregateFunction.Sum) }
        });
        var sums = ds.Rows.ToDictionary(r => (string)r[0]!, r => r[1] is null ? (decimal?)null : Convert.ToDecimal(r[1]));
        Assert.Equal(1500m, sums["Venta"]);
        Assert.Null(sums["Llamada"]);
    }

    [Fact]
    public async Task WorkLog_SumsHoursByCliente()
    {
        var s = await SeedAsync("Analisis Horas");

        var ds = await QueryAsync(s.TenantId, new ReportQuerySpec
        {
            SourceKey = WorkLogReportSource.SourceKey,
            GroupBy = new[] { "Cliente" },
            Aggregates = new[] { new ReportAggregate("Horas", ReportAggregateFunction.Sum) }
        });
        Assert.Single(ds.Rows);
        Assert.Equal("ACME Industrial", ds.Rows[0][0]);
        Assert.Equal(2.5m, Convert.ToDecimal(ds.Rows[0][1]));
    }

    [Fact]
    public async Task TaskItem_ExtendedFields_ClienteAndVencida()
    {
        var s = await SeedAsync("Analisis Tareas");

        var ds = await QueryAsync(s.TenantId, new ReportQuerySpec
        {
            SourceKey = TaskItemReportSource.SourceKey,
            Fields = new[] { "Number", "Cliente", "ClienteNit", "Vencida" },
            Sort = new[] { new ReportSort("Number") }
        });
        Assert.Equal(3, ds.RowCount);
        Assert.Equal("ACME Industrial", ds.Rows[0][1]);
        Assert.Equal("900123456", ds.Rows[0][2]);
        Assert.Equal(true, ds.Rows[0][3]);   // AN-1 vencida y abierta
        Assert.Equal(false, ds.Rows[2][3]);  // AN-3 terminada
    }

    [Fact]
    public async Task Curados_Semaforo_AndTiempoPorEtapa()
    {
        var s = await SeedAsync("Analisis Curados");

        var semaforo = (CategoriasShape)await RunAsync(s.TenantId, ds =>
            new SemaforoVencimientoIndicador(ds).QueryAsync(new IndicadorContexto(new ReportContext(s.TenantId))));
        var byLabel = semaforo.Items.ToDictionary(i => i.Etiqueta, i => i.Valor);
        Assert.Equal(1m, byLabel["Vencidas"]);
        Assert.Equal(1m, byLabel["Sin fecha"]);

        var etapas = (CategoriasShape)await RunAsync(s.TenantId, ds =>
            new TiempoPorEtapaIndicador(ds).QueryAsync(new IndicadorContexto(new ReportContext(s.TenantId))));
        var enCurso = Assert.Single(etapas.Items);
        Assert.Equal("En curso", enCurso.Etiqueta);
        Assert.True(enCurso.Valor >= 9.9m, $"Promedio esperado ~10 dias, fue {enCurso.Valor}");
    }

    [Fact]
    public async Task NewSources_AreTenantIsolated()
    {
        var a = await SeedAsync("Analisis Iso A");
        var b = await SeedAsync("Analisis Iso B", nombreCliente: "Solo B");

        foreach (var (key, field) in new[]
        {
            (TerceroReportSource.SourceKey, "Nombre"),
            (TerceroNotaReportSource.SourceKey, "Tercero"),
            (WorkLogReportSource.SourceKey, "Cliente"),
            (TaskItemReportSource.SourceKey, "Cliente")
        })
        {
            var seenByB = await QueryAsync(b.TenantId, new ReportQuerySpec { SourceKey = key, Fields = new[] { field } });
            Assert.NotEmpty(seenByB.Rows);
            Assert.DoesNotContain(seenByB.Rows, r => Equals(r[0], "ACME Industrial"));
        }

        // Y el tenant A sigue viendo lo suyo.
        var seenByA = await QueryAsync(a.TenantId, new ReportQuerySpec { SourceKey = TerceroReportSource.SourceKey, Fields = new[] { "Nombre" } });
        Assert.Contains(seenByA.Rows, r => Equals(r[0], "ACME Industrial"));
    }

    // ---- Helpers ----

    private Task<ReportDataSet> QueryAsync(Guid tenantId, ReportQuerySpec spec) =>
        RunAsync(tenantId, ds => ds.QueryAsync(spec, new ReportContext(tenantId)));

    private async Task<T> RunAsync<T>(Guid tenantId, Func<IReportDataSource, Task<T>> action)
    {
        await using var ctx = _fixture.CreateContext(tenantId);
        var factory = new ApplicationDbContextFactory(() => _fixture.CreateContext(tenantId));
        var tenantCtx = new TestTenantContext(tenantId);
        var native = new IReportableSource[]
        {
            new TaskItemReportSource(ctx, factory),
            new TerceroReportSource(factory),
            new TerceroNotaReportSource(factory),
            new WorkLogReportSource(factory)
        };
        var containers = new ContainerReportReader(ctx);
        var external = ExternalTestDoubles.Reader(ctx);
        var forms = new FormResponseReportReader(ctx);
        var catalog = new ReportCatalog(native, containers, external, forms, tenantCtx, ctx);
        return await action(new ReportDataSource(catalog, native, containers, external, forms, tenantCtx));
    }

    /// <summary>
    /// Por tenant: 2 terceros (uno cliente+proveedor con NIT con DV, uno sin perfil), 2 notas, un tablero
    /// con etapa "En curso", 3 tareas (vencida abierta del cliente, sin fecha en etapa hace 10 dias,
    /// terminada) y 2 registros de horas (1.5h + 1h) sobre la tarea del cliente.
    /// </summary>
    private async Task<Guid> SeedTenantRowAsync(string name)
    {
        var tenantId = Guid.CreateVersion7();
        await using var ctx = _fixture.CreateContext(tenantId: null);
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = name });
        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private async Task<Seed> SeedAsync(string tenantName, string nombreCliente = "ACME Industrial")
    {
        var tenantId = await SeedTenantRowAsync(tenantName);
        var now = DateTimeOffset.UtcNow;

        await using var ctx = _fixture.CreateContext(tenantId);

        var platformUser = new PlatformUser { Email = $"an-{tenantId:N}@analisis.test", EmailVerified = true, Status = PlatformUserStatus.Active };
        ctx.PlatformUsers.Add(platformUser);
        var tenantUser = new TenantUser { TenantId = tenantId, PlatformUserId = platformUser.Id, Email = platformUser.Email };
        ctx.TenantUsers.Add(tenantUser);

        var cliente = new Tercero
        {
            TenantId = tenantId,
            Nombre = nombreCliente,
            Tipo = TerceroTipo.Empresa,
            Perfiles = TerceroPerfil.Cliente | TerceroPerfil.Proveedor,
            IdValor = "900.123.456-7",
            Vendedor = "Laura"
        };
        var otro = new Tercero { TenantId = tenantId, Nombre = nombreCliente + " contacto", Tipo = TerceroTipo.Persona };
        ctx.Terceros.AddRange(cliente, otro);

        ctx.TerceroNotas.AddRange(
            new TerceroNota { TenantId = tenantId, TerceroId = cliente.Id, Texto = "Cierre", Accion = "Venta", Valor = 1500m },
            new TerceroNota { TenantId = tenantId, TerceroId = cliente.Id, Texto = "Seguimiento", Accion = "Llamada" });

        var board = new TaskBoard { TenantId = tenantId, Name = "Comercial", Kind = TaskBoardKind.Activities };
        ctx.TaskBoards.Add(board);
        var col = new TaskBoardColumn { TenantId = tenantId, BoardId = board.Id, Name = "En curso", SortOrder = 0 };
        ctx.TaskBoardColumns.Add(col);

        var vencida = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Number = "AN-1",
            Title = "Vencida",
            Status = TaskItemStatus.Active,
            Priority = TaskPriority.Medium,
            DueDate = now.AddDays(-2),
            TerceroId = cliente.Id
        };
        var enEtapa = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Number = "AN-2",
            Title = "Sin fecha",
            Status = TaskItemStatus.InProgress,
            Priority = TaskPriority.Medium,
            BoardId = board.Id,
            ColumnId = col.Id,
            ColumnEnteredAt = now.AddDays(-10)
        };
        var terminada = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Number = "AN-3",
            Title = "Lista",
            Status = TaskItemStatus.Done,
            Priority = TaskPriority.Medium,
            DueDate = now.AddDays(-5),
            ClosedAt = now.AddDays(-1)
        };
        ctx.TaskItems.AddRange(vencida, enEtapa, terminada);

        ctx.TaskWorkLogs.AddRange(
            new TaskWorkLog { TenantId = tenantId, TaskItemId = vencida.Id, TenantUserId = tenantUser.Id, Seconds = 5400, LoggedAt = now },
            new TaskWorkLog { TenantId = tenantId, TaskItemId = vencida.Id, TenantUserId = tenantUser.Id, Seconds = 3600, LoggedAt = now });

        await ctx.SaveChangesAsync();
        return new Seed(tenantId);
    }

    private sealed record Seed(Guid TenantId);

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class AnalisisSourcesTests_Postgres
    : AnalisisSourcesTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public AnalisisSourcesTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class AnalisisSourcesTests_SqlServer
    : AnalisisSourcesTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public AnalisisSourcesTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
