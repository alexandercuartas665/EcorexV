using Ecorex.Application.Analisis;
using Ecorex.Application.Common;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Integration.Tests;

/// <summary>
/// ADR-0158 Ola 2 en matriz dual PostgreSQL / SQL Server: CRUD de libros/hojas/widgets (soft-delete,
/// orden, ancho, concurrencia optimista), reglas de acceso (dueno / admin / compartido), ejecucion de
/// los widgets de ejemplo contra datos reales y AISLAMIENTO cross-tenant de los libros.
/// </summary>
public abstract class AnalisisLibrosTestsBase
{
    private readonly TenantIsolationDbFixture _fixture;

    protected AnalisisLibrosTestsBase(TenantIsolationDbFixture fixture) => _fixture = fixture;

    private static readonly Guid Duena = Guid.CreateVersion7();
    private static readonly AnalisisActor ActorDuena = new(Duena, EsAdmin: false);
    private static readonly AnalisisActor ActorOtro = new(Guid.CreateVersion7(), EsAdmin: false);
    private static readonly AnalisisActor ActorAdmin = new(Guid.CreateVersion7(), EsAdmin: true);

    [Fact]
    public async Task Libro_NaceConHoja_YAdministraHojasYWidgets()
    {
        var tenantId = await SeedTenantAsync("Libros CRUD");
        var svc = Service(tenantId);

        var creado = await svc.CrearLibroAsync("  Comercial  ", "KPIs del area", ActorDuena);
        Assert.True(creado.Ok);
        var libro = (await svc.ObtenerLibroAsync(creado.Id!.Value, ActorDuena))!;
        Assert.Equal("Comercial", libro.Nombre);
        Assert.True(libro.PuedeEditar);
        var hoja1 = Assert.Single(libro.Hojas);
        Assert.Equal("Hoja 1", hoja1.Nombre);

        var hoja2 = await svc.CrearHojaAsync(libro.Id, "Ventas", ActorDuena);
        Assert.True(hoja2.Ok);
        Assert.True((await svc.RenombrarHojaAsync(hoja2.Id!.Value, "Ventas 2026", ActorDuena)).Ok);

        var w1 = await svc.AgregarWidgetAsync(hoja1.Id, AnalisisEjemplos.Find("tareas-abiertas")!.Widget, ActorDuena);
        var w2 = await svc.AgregarWidgetAsync(hoja1.Id, AnalisisEjemplos.Find("semaforo")!.Widget, ActorDuena);
        var w3 = await svc.AgregarWidgetAsync(hoja1.Id, AnalisisEjemplos.Find("horas-usuario")!.Widget, ActorDuena);
        Assert.True(w1.Ok && w2.Ok && w3.Ok);

        // Mover el tercero al frente (dos pasos) y ensanchar el primero (acotado a 4).
        Assert.True((await svc.MoverWidgetAsync(w3.Id!.Value, -1, ActorDuena)).Ok);
        Assert.True((await svc.MoverWidgetAsync(w3.Id!.Value, -1, ActorDuena)).Ok);
        Assert.True((await svc.MoverWidgetAsync(w3.Id!.Value, -1, ActorDuena)).Ok); // ya es el primero: no-op
        Assert.True((await svc.CambiarAnchoWidgetAsync(w1.Id!.Value, 9, ActorDuena)).Ok);

        libro = (await svc.ObtenerLibroAsync(libro.Id, ActorDuena))!;
        Assert.Equal(new[] { "Hoja 1", "Ventas 2026" }, libro.Hojas.Select(h => h.Nombre));
        var widgets = libro.Hojas[0].Widgets;
        Assert.Equal(new[] { w3.Id, w1.Id, w2.Id }, widgets.Select(w => (Guid?)w.Id));
        Assert.Equal(new[] { 0, 1, 2 }, widgets.Select(w => w.Orden));
        Assert.Equal(4, widgets.Single(w => w.Id == w1.Id).ColSpan);
        Assert.Equal(SemaforoVencimientoIndicador.IndicadorKey, widgets.Single(w => w.Id == w2.Id).IndicadorKey);

        Assert.True((await svc.EliminarWidgetAsync(w2.Id!.Value, ActorDuena)).Ok);
        Assert.True((await svc.EliminarHojaAsync(hoja2.Id!.Value, ActorDuena)).Ok);
        libro = (await svc.ObtenerLibroAsync(libro.Id, ActorDuena))!;
        Assert.Single(libro.Hojas);
        Assert.Equal(2, libro.Hojas[0].Widgets.Count);

        // La ultima hoja no se puede borrar.
        Assert.False((await svc.EliminarHojaAsync(hoja1.Id, ActorDuena)).Ok);
    }

    [Fact]
    public async Task Widget_Invalido_SeRechaza()
    {
        var tenantId = await SeedTenantAsync("Libros invalido");
        var svc = Service(tenantId);
        var libro = (await svc.ObtenerLibroAsync((await svc.CrearLibroAsync("L", null, ActorDuena)).Id!.Value, ActorDuena))!;
        var hoja = libro.Hojas[0].Id;

        Assert.False((await svc.AgregarWidgetAsync(hoja, new AnalisisNuevoWidget(AnalisisTipoWidget.Kpi, "X", null, null), ActorDuena)).Ok);
        Assert.False((await svc.AgregarWidgetAsync(hoja, new AnalisisNuevoWidget(AnalisisTipoWidget.Kpi, "X", null, "no.existe"), ActorDuena)).Ok);
        Assert.False((await svc.AgregarWidgetAsync(hoja, new AnalisisNuevoWidget(AnalisisTipoWidget.Kpi, " ", null, SemaforoVencimientoIndicador.IndicadorKey), ActorDuena)).Ok);
        Assert.False((await svc.CrearLibroAsync("   ", null, ActorDuena)).Ok);
    }

    [Fact]
    public async Task Acceso_DuenoAdminYCompartido()
    {
        var tenantId = await SeedTenantAsync("Libros acceso");
        var svc = Service(tenantId);
        var id = (await svc.CrearLibroAsync("Privado", null, ActorDuena)).Id!.Value;

        // Privado: otro usuario no lo ve ni lo edita; el admin si.
        Assert.Null(await svc.ObtenerLibroAsync(id, ActorOtro));
        Assert.DoesNotContain(await svc.ListarLibrosAsync(ActorOtro), l => l.Id == id);
        Assert.False((await svc.CrearHojaAsync(id, "Intrusa", ActorOtro)).Ok);
        var vistoAdmin = await svc.ObtenerLibroAsync(id, ActorAdmin);
        Assert.NotNull(vistoAdmin);
        Assert.True(vistoAdmin!.PuedeEditar);

        // Compartido en el tenant: el otro lo ve, pero solo en lectura.
        await using (var db = _fixture.CreateContext(tenantId))
        {
            var l = await db.AnalisisLibros.SingleAsync(x => x.Id == id);
            l.Visibilidad = AnalisisVisibilidad.CompartidoEnTenant;
            await db.SaveChangesAsync();
        }

        var visto = await svc.ObtenerLibroAsync(id, ActorOtro);
        Assert.NotNull(visto);
        Assert.False(visto!.PuedeEditar);
        Assert.False((await svc.EliminarLibroAsync(id, ActorOtro)).Ok);
    }

    [Fact]
    public async Task Renombrar_ConVersionVieja_FallaPorConcurrencia()
    {
        var tenantId = await SeedTenantAsync("Libros version");
        var svc = Service(tenantId);
        var id = (await svc.CrearLibroAsync("V1", null, ActorDuena)).Id!.Value;
        var leido = (await svc.ObtenerLibroAsync(id, ActorDuena))!;

        Assert.True((await svc.RenombrarLibroAsync(id, "V2", null, leido.Version, ActorDuena)).Ok);
        // Segundo editor con la version ya superada.
        var stale = await svc.RenombrarLibroAsync(id, "V3", null, leido.Version, ActorDuena);
        Assert.False(stale.Ok);
        Assert.Equal("V2", (await svc.ObtenerLibroAsync(id, ActorDuena))!.Nombre);
    }

    [Fact]
    public async Task EliminarLibro_EsSoftDelete_EnCascadaDeHojas()
    {
        var tenantId = await SeedTenantAsync("Libros borrar");
        var svc = Service(tenantId);
        var id = (await svc.CrearLibroAsync("Temporal", null, ActorDuena)).Id!.Value;
        await svc.CrearHojaAsync(id, "Otra", ActorDuena);

        Assert.True((await svc.EliminarLibroAsync(id, ActorDuena)).Ok);
        Assert.Null(await svc.ObtenerLibroAsync(id, ActorDuena));
        Assert.DoesNotContain(await svc.ListarLibrosAsync(ActorDuena), l => l.Id == id);

        await using var db = _fixture.CreateContext(tenantId);
        var fila = await db.AnalisisLibros.IgnoreQueryFilters().SingleAsync(l => l.Id == id);
        Assert.True(fila.IsDeleted);
        var hojas = await db.AnalisisHojas.IgnoreQueryFilters().Where(h => h.LibroId == id).ToListAsync();
        Assert.Equal(2, hojas.Count);
        Assert.All(hojas, h => Assert.True(h.IsDeleted));
    }

    [Fact]
    public async Task Ejemplos_SeEjecutanSinError_ContraDatosReales()
    {
        var tenantId = await SeedTenantAsync("Libros ejemplos", conDatos: true);
        var svc = Service(tenantId);
        var libro = (await svc.ObtenerLibroAsync((await svc.CrearLibroAsync("Demo", null, ActorDuena)).Id!.Value, ActorDuena))!;
        foreach (var e in AnalisisEjemplos.Catalogo)
        {
            Assert.True((await svc.AgregarWidgetAsync(libro.Hojas[0].Id, e.Widget, ActorDuena)).Ok, e.Key);
        }

        libro = (await svc.ObtenerLibroAsync(libro.Id, ActorDuena))!;
        var resultados = new Dictionary<string, AnalisisWidgetResultado>();
        await RunWithRunnerAsync(tenantId, async runner =>
        {
            foreach (var w in libro.Hojas[0].Widgets)
            {
                resultados[w.Titulo] = await runner.RunAsync(w, new ReportContext(tenantId));
            }
        });

        Assert.Equal(AnalisisEjemplos.Catalogo.Count, resultados.Count);
        Assert.All(resultados, kv => Assert.True(kv.Value.Ok, $"{kv.Key}: {kv.Value.Error}"));
        Assert.Equal(2m, ((EscalarShape)resultados["Tareas abiertas"].Forma!).Valor);
        Assert.Equal(1m, ((EscalarShape)resultados["Clientes en el directorio"].Forma!).Valor);
        Assert.Equal(2.5m, ((EscalarShape)resultados["Horas trabajadas"].Forma!).Valor);
        Assert.IsType<CategoriasShape>(resultados["Semaforo de vencimiento"].Forma);
    }

    [Fact]
    public async Task Libros_SonAisladosPorTenant()
    {
        var a = await SeedTenantAsync("Libros iso A");
        var b = await SeedTenantAsync("Libros iso B");
        var svcA = Service(a);
        var svcB = Service(b);

        var idA = (await svcA.CrearLibroAsync("Solo A", null, ActorAdmin)).Id!.Value;
        var hojaA = (await svcA.ObtenerLibroAsync(idA, ActorAdmin))!.Hojas[0].Id;

        // Ni siquiera un admin de B ve, edita o agrega widgets al libro de A.
        Assert.Empty(await svcB.ListarLibrosAsync(ActorAdmin));
        Assert.Null(await svcB.ObtenerLibroAsync(idA, ActorAdmin));
        Assert.False((await svcB.CrearHojaAsync(idA, "Cruzada", ActorAdmin)).Ok);
        Assert.False((await svcB.AgregarWidgetAsync(hojaA, AnalisisEjemplos.Catalogo[0].Widget, ActorAdmin)).Ok);
        Assert.False((await svcB.EliminarLibroAsync(idA, ActorAdmin)).Ok);

        Assert.Single(await svcA.ListarLibrosAsync(ActorAdmin));
    }

    // ---- Helpers ----

    private AnalisisService Service(Guid tenantId) =>
        new(new ApplicationDbContextFactory(() => _fixture.CreateContext(tenantId)), Registry());

    private static IndicadorCuradoRegistry Registry(IReportDataSource? data = null) =>
        new(new IIndicadorCurado[]
        {
            new SemaforoVencimientoIndicador(data!),
            new TiempoPorEtapaIndicador(data!)
        });

    private async Task RunWithRunnerAsync(Guid tenantId, Func<IAnalisisWidgetRunner, Task> action)
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
        var data = new ReportDataSource(catalog, native, containers, external, forms, tenantCtx);
        await action(new AnalisisWidgetRunner(data, Registry(data)));
    }

    /// <summary>
    /// Fila de tenant y, con <paramref name="conDatos"/>: un cliente + un contacto, dos tareas abiertas
    /// (una vencida del cliente) + una terminada y 2.5 h trabajadas sobre la tarea del cliente.
    /// </summary>
    private async Task<Guid> SeedTenantAsync(string name, bool conDatos = false)
    {
        var tenantId = Guid.CreateVersion7();
        await using (var root = _fixture.CreateContext(tenantId: null))
        {
            root.Tenants.Add(new Tenant { Id = tenantId, Name = name });
            await root.SaveChangesAsync();
        }

        if (!conDatos)
        {
            return tenantId;
        }

        var now = DateTimeOffset.UtcNow;
        await using var ctx = _fixture.CreateContext(tenantId);
        var platformUser = new PlatformUser { Email = $"lib-{tenantId:N}@analisis.test", EmailVerified = true, Status = PlatformUserStatus.Active };
        ctx.PlatformUsers.Add(platformUser);
        var tenantUser = new TenantUser { TenantId = tenantId, PlatformUserId = platformUser.Id, Email = platformUser.Email };
        ctx.TenantUsers.Add(tenantUser);

        var cliente = new Tercero { TenantId = tenantId, Nombre = "ACME", Tipo = TerceroTipo.Empresa, Perfiles = TerceroPerfil.Cliente };
        ctx.Terceros.AddRange(cliente, new Tercero { TenantId = tenantId, Nombre = "Contacto", Tipo = TerceroTipo.Persona });

        var vencida = new TaskItem
        {
            TenantId = tenantId,
            Number = "LB-1",
            Title = "Vencida",
            Status = TaskItemStatus.Active,
            Priority = TaskPriority.Medium,
            DueDate = now.AddDays(-2),
            TerceroId = cliente.Id
        };
        ctx.TaskItems.AddRange(
            vencida,
            new TaskItem { TenantId = tenantId, Number = "LB-2", Title = "Abierta", Status = TaskItemStatus.Pending, Priority = TaskPriority.Medium },
            new TaskItem { TenantId = tenantId, Number = "LB-3", Title = "Terminada", Status = TaskItemStatus.Done, Priority = TaskPriority.Medium });

        ctx.TaskWorkLogs.AddRange(
            new TaskWorkLog { TenantId = tenantId, TaskItemId = vencida.Id, TenantUserId = tenantUser.Id, Seconds = 5400, LoggedAt = now },
            new TaskWorkLog { TenantId = tenantId, TaskItemId = vencida.Id, TenantUserId = tenantUser.Id, Seconds = 3600, LoggedAt = now });

        await ctx.SaveChangesAsync();
        return tenantId;
    }

    private sealed class TestTenantContext(Guid? tenantId, Guid? userId = null) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserId { get; } = userId;
    }
}

/// <summary>Matriz dual, motor PostgreSQL.</summary>
public sealed class AnalisisLibrosTests_Postgres
    : AnalisisLibrosTestsBase, IClassFixture<PostgresTenantIsolationFixture>
{
    public AnalisisLibrosTests_Postgres(PostgresTenantIsolationFixture fixture) : base(fixture)
    {
    }
}

/// <summary>Matriz dual, motor SQL Server.</summary>
public sealed class AnalisisLibrosTests_SqlServer
    : AnalisisLibrosTestsBase, IClassFixture<SqlServerTenantIsolationFixture>
{
    public AnalisisLibrosTests_SqlServer(SqlServerTenantIsolationFixture fixture) : base(fixture)
    {
    }
}
