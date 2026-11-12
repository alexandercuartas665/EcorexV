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

    [Fact]
    public async Task ActualizarWidget_YFiltrosDeHoja_SePersisten()
    {
        var tenantId = await SeedTenantAsync("Libros ola3 persist");
        var svc = Service(tenantId);
        var libro = (await svc.ObtenerLibroAsync((await svc.CrearLibroAsync("L3", null, ActorDuena)).Id!.Value, ActorDuena))!;
        var hoja = libro.Hojas[0].Id;
        var w = (await svc.AgregarWidgetAsync(hoja, AnalisisEjemplos.Find("horas-usuario")!.Widget, ActorDuena)).Id!.Value;

        var diseno = new WidgetDiseno
        {
            Tipo = AnalisisTipoWidget.Kpi,
            Titulo = "Promedio de horas",
            SourceKey = WorkLogReportSource.SourceKey,
            Funcion = ReportAggregateFunction.Avg,
            CampoMedida = "Horas",
            ColSpan = 3,
            Config = new WidgetConfig { Formato = "decimal", Tono = "violet" }
        };
        Assert.True((await svc.ActualizarWidgetAsync(w, WidgetDisenoMapper.ToNuevoWidget(diseno, "{\"fecha\":\"\"}"), ActorDuena)).Ok);
        Assert.False((await svc.ActualizarWidgetAsync(w, WidgetDisenoMapper.ToNuevoWidget(diseno), ActorOtro)).Ok);

        var filtros = new HojaFiltros { Desde = new DateOnly(2026, 1, 1), Vendedor = "Laura" };
        Assert.True((await svc.GuardarFiltrosHojaAsync(hoja, filtros, ActorDuena)).Ok);
        Assert.False((await svc.GuardarFiltrosHojaAsync(hoja, new HojaFiltros { Desde = new DateOnly(2026, 2, 1), Hasta = new DateOnly(2026, 1, 1) }, ActorDuena)).Ok);
        Assert.False((await svc.GuardarFiltrosHojaAsync(hoja, filtros, ActorOtro)).Ok);

        var leido = (await svc.ObtenerLibroAsync(libro.Id, ActorDuena))!;
        var wd = Assert.Single(leido.Hojas[0].Widgets);
        Assert.Equal(w, wd.Id);
        Assert.Equal(0, wd.Orden); // conserva la posicion
        Assert.Equal(3, wd.ColSpan);
        Assert.Equal("{\"fecha\":\"\"}", wd.ContextBindingsJson);
        Assert.Equal(diseno.Funcion, WidgetDisenoMapper.FromWidget(wd).Funcion);
        Assert.Equal("violet", WidgetConfig.Leer(wd.ConfigJson).Tono);
        Assert.Equal("Laura", leido.Hojas[0].Filtros.Vendedor);
        Assert.Equal(new DateOnly(2026, 1, 1), leido.Hojas[0].Filtros.Desde);
    }

    [Fact]
    public async Task Kpi_SinDimension_YFiltrosDeHoja_SeAplicanPorFuente()
    {
        var tenantId = await SeedTenantAsync("Libros ola3 filtros", conDatos: true);
        var manana = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        AnalisisWidgetDto Kpi(string source, ReportAggregateFunction fn, string? campo = null, string? bindings = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Kpi, "K",
                AnalisisJson.Serialize(WidgetDisenoMapper.ToSpec(new WidgetDiseno { Tipo = AnalisisTipoWidget.Kpi, Titulo = "K", SourceKey = source, Funcion = fn, CampoMedida = campo })),
                null, null, 0, 1, 1, bindings);

        decimal? Valor(AnalisisWidgetResultado r)
        {
            Assert.True(r.Ok, r.Error);
            return ((EscalarShape)r.Forma!).Valor;
        }

        await RunWithRunnerAsync(tenantId, async runner =>
        {
            var ctx = new ReportContext(tenantId);
            // KPI total (sin agrupar): conteo y promedio en memoria sobre el modo tabular.
            Assert.Equal(3m, Valor(await runner.RunAsync(Kpi(TaskItemReportSource.SourceKey, ReportAggregateFunction.Count), ctx)));
            Assert.Equal(1.25m, Valor(await runner.RunAsync(Kpi(WorkLogReportSource.SourceKey, ReportAggregateFunction.Avg, "Horas"), ctx)));
            Assert.Equal(2m, Valor(await runner.RunAsync(Kpi(TerceroReportSource.SourceKey, ReportAggregateFunction.Count), ctx)));

            // Vendedor de la hoja: tareas -> solo la del cliente de Laura; directorio -> solo ACME;
            // horas no tiene campo vendedor -> lo ignora.
            var porVendedor = new HojaFiltros { Vendedor = "laur" };
            Assert.Equal(1m, Valor(await runner.RunAsync(Kpi(TaskItemReportSource.SourceKey, ReportAggregateFunction.Count), ctx, default, porVendedor)));
            Assert.Equal(1m, Valor(await runner.RunAsync(Kpi(TerceroReportSource.SourceKey, ReportAggregateFunction.Count), ctx, default, porVendedor)));
            Assert.Equal(2.5m, Valor(await runner.RunAsync(Kpi(WorkLogReportSource.SourceKey, ReportAggregateFunction.Sum, "Horas"), ctx, default, porVendedor)));

            // Rango de fechas en el futuro: nada; salvo el widget que pidio "no aplicar la fecha".
            var futuro = new HojaFiltros { Desde = manana };
            Assert.Equal(0m, Valor(await runner.RunAsync(Kpi(TaskItemReportSource.SourceKey, ReportAggregateFunction.Count), ctx, default, futuro)));
            Assert.Equal(0m, Valor(await runner.RunAsync(Kpi(WorkLogReportSource.SourceKey, ReportAggregateFunction.Sum, "Horas"), ctx, default, futuro)));
            Assert.Equal(3m, Valor(await runner.RunAsync(Kpi(TaskItemReportSource.SourceKey, ReportAggregateFunction.Count, bindings: "{\"fecha\":\"\"}"), ctx, default, futuro)));

            // El indicador curado tambien recibe los filtros de la hoja.
            var semaforo = new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Dona, "S", null,
                SemaforoVencimientoIndicador.IndicadorKey, null, 0, 1, 1);
            var sinFiltro = (CategoriasShape)(await runner.RunAsync(semaforo, ctx)).Forma!;
            var conFiltro = (CategoriasShape)(await runner.RunAsync(semaforo, ctx, default, porVendedor)).Forma!;
            Assert.Equal(2m, sinFiltro.Total);
            Assert.Equal(1m, conFiltro.Total);
        });
    }

    [Fact]
    public async Task Layout_Duplicar_YLecturaEmbebida()
    {
        var tenantId = await SeedTenantAsync("Libros ola4 layout");
        var svc = Service(tenantId);
        var libro = (await svc.ObtenerLibroAsync((await svc.CrearLibroAsync("L4", null, ActorDuena)).Id!.Value, ActorDuena))!;
        var hoja = libro.Hojas[0].Id;
        var a = (await svc.AgregarWidgetAsync(hoja, AnalisisEjemplos.Find("tareas-abiertas")!.Widget, ActorDuena)).Id!.Value;
        var b = (await svc.AgregarWidgetAsync(hoja, AnalisisEjemplos.Find("semaforo")!.Widget, ActorDuena)).Id!.Value;
        var c = (await svc.AgregarWidgetAsync(hoja, AnalisisEjemplos.Find("horas-total")!.Widget, ActorDuena)).Id!.Value;
        Assert.True((await svc.GuardarFiltrosHojaAsync(hoja, new HojaFiltros { Vendedor = "Laura" }, ActorDuena)).Ok);

        // Layout en lote: c primero y ancho 4; b al final con ancho fuera de rango (se acota).
        var layout = new[] { new AnalisisLayoutItem(c, 4), new AnalisisLayoutItem(a, 1), new AnalisisLayoutItem(b, 9) };
        Assert.True((await svc.GuardarLayoutAsync(hoja, layout, ActorDuena)).Ok);
        Assert.False((await svc.GuardarLayoutAsync(hoja, layout.Take(2).ToList(), ActorDuena)).Ok); // incompleto
        Assert.False((await svc.GuardarLayoutAsync(hoja, layout, ActorOtro)).Ok);
        var ws = (await svc.ObtenerHojaAsync(hoja, ActorDuena))!.Widgets;
        Assert.Equal(new[] { c, a, b }, ws.Select(w => w.Id));
        Assert.Equal(new[] { 4, 1, 4 }, ws.Select(w => w.ColSpan));

        // Duplicar widget: la copia queda justo despues del original.
        var copia = (await svc.DuplicarWidgetAsync(a, ActorDuena)).Id!.Value;
        ws = (await svc.ObtenerHojaAsync(hoja, ActorDuena))!.Widgets;
        Assert.Equal(new[] { c, a, copia, b }, ws.Select(w => w.Id));
        Assert.EndsWith("(copia)", ws[2].Titulo);
        Assert.Equal(ws[1].SpecJson, ws[2].SpecJson);

        // Duplicar hoja: copia filtros y widgets, al final del libro.
        var hoja2 = (await svc.DuplicarHojaAsync(hoja, ActorDuena)).Id!.Value;
        var dup = (await svc.ObtenerHojaAsync(hoja2, ActorDuena))!;
        Assert.Equal(4, dup.Widgets.Count);
        Assert.Equal("Laura", dup.Filtros.Vendedor);
        Assert.Equal(2, (await svc.ObtenerLibroAsync(libro.Id, ActorDuena))!.Hojas.Count);

        // Lectura embebida respeta la visibilidad del libro (privado).
        Assert.Null(await svc.ObtenerHojaAsync(hoja, ActorOtro));
        Assert.Empty(await svc.ObtenerWidgetsAsync(new[] { a, b }, ActorOtro));
        Assert.Equal(new[] { b, a }, (await svc.ObtenerWidgetsAsync(new[] { b, a, Guid.NewGuid() }, ActorDuena)).Select(w => w.Id));
        Assert.Equal(2, (await svc.ListarHojasAsync(ActorDuena)).Count);
        Assert.Empty(await svc.ListarHojasAsync(ActorOtro));
        // Destinos de "Crear indicador" desde otro modulo: solo libros que el actor puede editar.
        Assert.Equal(2, (await svc.ListarHojasAsync(ActorDuena, soloEditables: true)).Count);
        Assert.Equal(2, (await svc.ListarHojasAsync(ActorAdmin, soloEditables: true)).Count);
        Assert.Empty(await svc.ListarHojasAsync(ActorOtro, soloEditables: true));

        // Hoja borrada: sus widgets dejan de salir embebidos.
        Assert.True((await svc.EliminarHojaAsync(hoja2, ActorDuena)).Ok);
        Assert.Empty(await svc.ObtenerWidgetsAsync(dup.Widgets.Select(w => w.Id).ToList(), ActorDuena));
    }

    [Fact]
    public async Task Anfitriones_SoloAdmin_FichaEInicio()
    {
        var tenantId = await SeedTenantAsync("Libros ola4 anfitrion");
        var svc = Service(tenantId);
        var anf = new AnalisisAnfitrionService(new ApplicationDbContextFactory(() => _fixture.CreateContext(tenantId)));
        var libro = (await svc.ObtenerLibroAsync((await svc.CrearLibroAsync("Ficha", null, ActorAdmin)).Id!.Value, ActorAdmin))!;
        var hoja = libro.Hojas[0].Id;
        var w = (await svc.AgregarWidgetAsync(hoja, AnalisisEjemplos.Catalogo[0].Widget, ActorAdmin)).Id!.Value;

        Assert.Null(await anf.HojaFichaTerceroAsync());
        Assert.False((await anf.GuardarHojaFichaTerceroAsync(hoja, ActorDuena)).Ok); // no admin
        Assert.False((await anf.GuardarHojaFichaTerceroAsync(Guid.NewGuid(), ActorAdmin)).Ok);
        Assert.True((await anf.GuardarHojaFichaTerceroAsync(hoja, ActorAdmin)).Ok);
        Assert.Equal(hoja, await anf.HojaFichaTerceroAsync());
        Assert.True((await anf.GuardarHojaFichaTerceroAsync(null, ActorAdmin)).Ok);
        Assert.Null(await anf.HojaFichaTerceroAsync());

        Assert.False((await anf.FijarEnInicioAsync(w, true, ActorDuena)).Ok);
        Assert.True((await anf.FijarEnInicioAsync(w, true, ActorAdmin)).Ok);
        Assert.True((await anf.FijarEnInicioAsync(w, true, ActorAdmin)).Ok); // idempotente
        Assert.Equal(new[] { w }, await anf.WidgetsInicioAsync());
        Assert.True((await anf.FijarEnInicioAsync(w, false, ActorAdmin)).Ok);
        Assert.Empty(await anf.WidgetsInicioAsync());

        // Aislamiento: la configuracion es por tenant.
        var otro = await SeedTenantAsync("Libros ola4 anfitrion B");
        var anfB = new AnalisisAnfitrionService(new ApplicationDbContextFactory(() => _fixture.CreateContext(otro)));
        Assert.True((await anf.GuardarHojaFichaTerceroAsync(hoja, ActorAdmin)).Ok);
        Assert.Null(await anfB.HojaFichaTerceroAsync());
        Assert.False((await anfB.GuardarHojaFichaTerceroAsync(hoja, ActorAdmin)).Ok); // hoja de otro tenant
    }

    [Fact]
    public async Task Contexto_Tercero_FiltraCadaFuente_YSeriesConDosGrupos()
    {
        var tenantId = await SeedTenantAsync("Libros ola4 contexto", conDatos: true);
        Guid acme;
        Guid contacto;
        await using (var db = _fixture.CreateContext(tenantId))
        {
            acme = await db.Terceros.Where(t => t.Nombre == "ACME").Select(t => t.Id).SingleAsync();
            contacto = await db.Terceros.Where(t => t.Nombre == "Contacto").Select(t => t.Id).SingleAsync();
        }

        AnalisisWidgetDto Kpi(string source, ReportAggregateFunction fn, string? campo = null, string? bindings = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Kpi, "K",
                AnalisisJson.Serialize(WidgetDisenoMapper.ToSpec(new WidgetDiseno { Tipo = AnalisisTipoWidget.Kpi, Titulo = "K", SourceKey = source, Funcion = fn, CampoMedida = campo })),
                null, null, 0, 1, 1, bindings);
        Dictionary<string, string> Ctx(Guid id) => new() { [AnalisisContexto.Tercero] = id.ToString() };
        decimal? Valor(AnalisisWidgetResultado r)
        {
            Assert.True(r.Ok, r.Error);
            return ((EscalarShape)r.Forma!).Valor;
        }

        await RunWithRunnerAsync(tenantId, async runner =>
        {
            var ctx = new ReportContext(tenantId);
            Assert.Equal(1m, Valor(await runner.RunAsync(Kpi(TaskItemReportSource.SourceKey, ReportAggregateFunction.Count), ctx, default, null, Ctx(acme))));
            Assert.Equal(0m, Valor(await runner.RunAsync(Kpi(TaskItemReportSource.SourceKey, ReportAggregateFunction.Count), ctx, default, null, Ctx(contacto))));
            Assert.Equal(2.5m, Valor(await runner.RunAsync(Kpi(WorkLogReportSource.SourceKey, ReportAggregateFunction.Sum, "Horas"), ctx, default, null, Ctx(acme))));
            Assert.Equal(1m, Valor(await runner.RunAsync(Kpi(TerceroReportSource.SourceKey, ReportAggregateFunction.Count), ctx, default, null, Ctx(contacto))));

            // Widget que pidio NO filtrar por tercero: datos generales y marcado como fuera de contexto.
            var general = await runner.RunAsync(Kpi(TaskItemReportSource.SourceKey, ReportAggregateFunction.Count, bindings: "{\"tercero\":\"\"}"), ctx, default, null, Ctx(acme));
            Assert.Equal(3m, Valor(general));
            Assert.True(general.FueraDeContexto);

            // Curado (semaforo) filtrado por el tercero.
            var semaforo = new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Semaforo, "S", null,
                SemaforoVencimientoIndicador.IndicadorKey, null, 0, 1, 1);
            var sem = await runner.RunAsync(semaforo, ctx, default, null, Ctx(acme));
            Assert.False(sem.FueraDeContexto);
            Assert.Equal(1m, ((CategoriasShape)sem.Forma!).Total);

            // Linea con dos agrupaciones (mes x estado) sobre Actividades -> serie por estado.
            var linea = new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Linea, "L",
                AnalisisJson.Serialize(WidgetDisenoMapper.ToSpec(new WidgetDiseno
                {
                    Tipo = AnalisisTipoWidget.Linea,
                    Titulo = "L",
                    SourceKey = TaskItemReportSource.SourceKey,
                    Dimension = "CreatedMonth",
                    Dimension2 = "Status",
                    OrdenarPorMedida = false
                })), null, null, 0, 2, 1);
            var serie = Assert.IsType<SerieShape>((await runner.RunAsync(linea, ctx)).Forma);
            Assert.Single(serie.Ejes);
            Assert.Equal(3, serie.Series.Count); // Active, Pending, Done
            Assert.Equal(3m, serie.Series.Sum(s => s.Valores.Sum(v => v ?? 0)));
        });
    }

    [Fact]
    public async Task Combinado_CruzaDirectorioXTareasPorCliente_ConFormulaYCategoria()
    {
        var tenantId = await SeedTenantAsync("Libros ola5 combinado", conDatos: true);
        await using (var db = _fixture.CreateContext(tenantId))
        {
            var acme = await db.Terceros.SingleAsync(t => t.Nombre == "ACME");
            db.DirectorioCategorias.Add(new DirectorioCategoria { TenantId = tenantId, CategoriaKey = "comercial", Title = "Comercial" });
            db.TerceroCategorias.Add(new TerceroCategoria { TenantId = tenantId, TerceroId = acme.Id, CategoriaKey = "comercial" });
            await db.SaveChangesAsync();
        }

        var diseno = new CombinadoDiseno
        {
            Series = new[]
            {
                new SerieCombinada { Nombre = "Terceros", SourceKey = TerceroReportSource.SourceKey, Dimension = "Nombre" },
                new SerieCombinada { Nombre = "Tareas", SourceKey = TaskItemReportSource.SourceKey, Dimension = "Cliente" },
                new SerieCombinada { Nombre = "Horas", SourceKey = WorkLogReportSource.SourceKey, Dimension = "Cliente", Funcion = ReportAggregateFunction.Sum, CampoMedida = "Horas" }
            },
            Formulas = new[] { new FormulaCombinada("Horas por tarea", "C / B") },
            Llave = LlaveCombinado.Texto,
            SoloComunes = true
        };
        AnalisisWidgetDto Widget(AnalisisTipoWidget tipo) => new(Guid.NewGuid(), Guid.NewGuid(), tipo, "Cruce", null,
            CombinadoDiseno.IndicadorKey, new WidgetConfig { Combinado = diseno }.ToJson(), 0, 2, 1);

        await RunWithRunnerAsync(tenantId, async runner =>
        {
            var ctx = new ReportContext(tenantId);
            var tabla = await runner.RunAsync(Widget(AnalisisTipoWidget.Tabla), ctx);
            Assert.True(tabla.Ok, tabla.Error);
            var fila = Assert.Single(((TablaShape)tabla.Forma!).Filas); // solo ACME esta en las tres series
            Assert.Equal("ACME", fila[0]);
            Assert.Equal(1m, Convert.ToDecimal(fila[1]));
            Assert.Equal(1m, Convert.ToDecimal(fila[2]));
            Assert.Equal(2.5m, Convert.ToDecimal(fila[3]));
            Assert.Equal(2.5m, Convert.ToDecimal(fila[4]));

            var kpi = await runner.RunAsync(Widget(AnalisisTipoWidget.Kpi), ctx);
            Assert.Equal(2.5m, ((EscalarShape)kpi.Forma!).Valor);

            // Categoria del Directorio (motor Modular) visible como campo reportable.
            var porCategoria = await runner.RunAsync(new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Barras, "Cat",
                AnalisisJson.Serialize(WidgetDisenoMapper.ToSpec(new WidgetDiseno { Tipo = AnalisisTipoWidget.Barras, Titulo = "Cat", SourceKey = TerceroReportSource.SourceKey, Dimension = "Categoria" })),
                null, null, 0, 1, 1), ctx);
            Assert.True(porCategoria.Ok, porCategoria.Error);
            Assert.Equal(1m, ((CategoriasShape)porCategoria.Forma!).Items.Single(i => i.Etiqueta == "Comercial").Valor);
        });

        // El servicio acepta el combinado (definicion en el ConfigJson) y rechaza uno sin series.
        var svc = Service(tenantId);
        var libro = (await svc.CrearLibroAsync("Combinado", null, ActorDuena)).Id!.Value;
        var hoja = (await svc.ObtenerLibroAsync(libro, ActorDuena))!.Hojas[0].Id;
        Assert.True((await svc.AgregarWidgetAsync(hoja, new AnalisisNuevoWidget(AnalisisTipoWidget.Barras, "C", null, CombinadoDiseno.IndicadorKey, 2, new WidgetConfig { Combinado = diseno }.ToJson()), ActorDuena)).Ok);
        Assert.False((await svc.AgregarWidgetAsync(hoja, new AnalisisNuevoWidget(AnalisisTipoWidget.Barras, "C", null, CombinadoDiseno.IndicadorKey), ActorDuena)).Ok);
    }

    [Fact]
    public async Task Compartir_UsuariosYEnlacePublico_RotarRevocarVencer_YAislamiento()
    {
        var a = await SeedTenantAsync("Libros ola6 A");
        var b = await SeedTenantAsync("Libros ola6 B");
        var svcA = Service(a);
        var libro = (await svcA.CrearLibroAsync("Publico", null, ActorDuena)).Id!.Value;
        var otroLibroB = (await Service(b).CrearLibroAsync("De B", null, ActorDuena)).Id!.Value;

        var reloj = new RelojFijo(DateTimeOffset.UtcNow);
        IAnalisisCompartirService Comp(Guid? t) => new AnalisisCompartirService(new ApplicationDbContextFactory(() => _fixture.CreateContext(t)), reloj);
        var compA = Comp(a);

        // Lista de usuarios: el otro usuario ve el libro privado; un tercero no.
        Assert.False((await compA.CambiarVisibilidadAsync(libro, AnalisisVisibilidad.CompartidoEnTenant, Array.Empty<Guid>(), ActorOtro)).Ok);
        Assert.True((await compA.CambiarVisibilidadAsync(libro, AnalisisVisibilidad.Privado, new[] { ActorOtro.UserId!.Value }, ActorDuena)).Ok);
        Assert.NotNull(await svcA.ObtenerLibroAsync(libro, ActorOtro));
        Assert.Null(await svcA.ObtenerLibroAsync(libro, new AnalisisActor(Guid.CreateVersion7(), false)));

        // Solo un Admin publica con enlace.
        Assert.False((await compA.GenerarEnlaceAsync(libro, null, ActorDuena)).Ok);
        var e1 = await compA.GenerarEnlaceAsync(libro, null, ActorAdmin);
        Assert.True(e1.Ok, e1.Error);

        // El visor anonimo (sin tenant) resuelve el token al tenant y libro correctos.
        var anon = Comp(null);
        var r = await anon.ResolverAsync(e1.Token);
        Assert.Equal(new AnalisisEnlaceResuelto(a, libro), r);
        Assert.NotNull(await Comp(a).LibroPublicoAsync(libro));
        Assert.Null(await Comp(b).LibroPublicoAsync(libro)); // con el tenant B el filtro global lo oculta
        Assert.Null(await Comp(b).LibroPublicoAsync(otroLibroB)); // privado: no es publico
        Assert.Null(await anon.ResolverAsync("no-existe"));
        Assert.Null(await anon.ResolverAsync(new string('x', 200)));

        // Rotar invalida el anterior.
        var e2 = await compA.GenerarEnlaceAsync(libro, reloj.Ahora.AddDays(1), ActorAdmin);
        Assert.Null(await anon.ResolverAsync(e1.Token));
        Assert.NotNull(await anon.ResolverAsync(e2.Token));
        var estado = await compA.EstadoAsync(libro, ActorDuena);
        Assert.True(estado!.EnlaceActivo);
        Assert.NotNull(estado.EnlaceExpira);

        // Vence.
        reloj.Ahora = reloj.Ahora.AddDays(2);
        Assert.Null(await anon.ResolverAsync(e2.Token));

        // Revocar y pasar a privado apagan el enlace.
        reloj.Ahora = reloj.Ahora.AddDays(-2);
        var e3 = await compA.GenerarEnlaceAsync(libro, null, ActorAdmin);
        Assert.True((await compA.RevocarEnlaceAsync(libro, ActorDuena)).Ok);
        Assert.Null(await anon.ResolverAsync(e3.Token));
        var e4 = await compA.GenerarEnlaceAsync(libro, null, ActorAdmin);
        Assert.True((await compA.CambiarVisibilidadAsync(libro, AnalisisVisibilidad.Privado, Array.Empty<Guid>(), ActorDuena)).Ok);
        Assert.Null(await anon.ResolverAsync(e4.Token));
    }

    private sealed class RelojFijo(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    [Fact]
    public async Task Portabilidad_ExportaEnUnTenant_ImportaEnOtro_ConEstructuraYAislamiento()
    {
        var a = await SeedTenantAsync("Libros port A");
        var b = await SeedTenantAsync("Libros port B");

        IAnalisisPortabilidad Portab(Guid t) =>
            new AnalisisPortabilidad(Service(t), new ApplicationDbContextFactory(() => _fixture.CreateContext(t)), Registry());

        // Libro en A: 2 hojas, filtros en la hoja 1, dos widgets de spec y uno curado.
        var svcA = Service(a);
        var libroA = (await svcA.CrearLibroAsync("Comercial", "KPIs del area", ActorDuena)).Id!.Value;
        var detA = (await svcA.ObtenerLibroAsync(libroA, ActorDuena))!;
        var hoja1 = detA.Hojas[0].Id;
        var hoja2 = (await svcA.CrearHojaAsync(libroA, "Ventas", ActorDuena)).Id!.Value;
        Assert.True((await svcA.GuardarFiltrosHojaAsync(hoja1, new HojaFiltros { Vendedor = "Laura" }, ActorDuena)).Ok);
        Assert.True((await svcA.AgregarWidgetAsync(hoja1, AnalisisEjemplos.Find("tareas-abiertas")!.Widget, ActorDuena)).Ok);
        Assert.True((await svcA.AgregarWidgetAsync(hoja1, AnalisisEjemplos.Find("semaforo")!.Widget, ActorDuena)).Ok);
        Assert.True((await svcA.AgregarWidgetAsync(hoja2, AnalisisEjemplos.Find("horas-usuario")!.Widget, ActorDuena)).Ok);

        // El indicador curado y el spec esperados (para comparar tras el viaje).
        detA = (await svcA.ObtenerLibroAsync(libroA, ActorDuena))!;
        var claveCurada = detA.Hojas[0].Widgets.Single(w => w.IndicadorKey is not null).IndicadorKey;
        Assert.NotNull(claveCurada);

        // Exporta en A, importa en B (otro usuario, otro tenant).
        var paquete = await Portab(a).ExportarAsync(libroA, ActorDuena);
        Assert.NotNull(paquete);
        Assert.EndsWith(".ecorex-libro.json", paquete!.Value.FileName);
        var r = await Portab(b).ImportarAsync(paquete.Value.Json, ActorOtro);
        Assert.True(r.Ok, r.Error);
        Assert.Empty(r.Avisos); // solo fuentes nativas y un curado que existe en el destino

        // El libro importado en B: misma estructura, dueno = quien importo, id NUEVO, privado.
        var impo = (await Service(b).ObtenerLibroAsync(r.LibroId!.Value, ActorOtro))!;
        Assert.NotEqual(libroA, impo.Id);
        Assert.Equal("Comercial", impo.Nombre);
        Assert.True(impo.PuedeEditar); // ActorOtro es el dueno en B
        Assert.Equal(AnalisisVisibilidad.Privado, impo.Visibilidad);
        Assert.Equal(new[] { "Hoja 1", "Ventas" }, impo.Hojas.Select(h => h.Nombre));
        Assert.Equal("Laura", impo.Hojas[0].Filtros.Vendedor);
        Assert.Equal(2, impo.Hojas[0].Widgets.Count);
        var curadoImp = Assert.Single(impo.Hojas[0].Widgets, w => w.IndicadorKey is not null);
        Assert.Equal(claveCurada, curadoImp.IndicadorKey);
        Assert.Contains(impo.Hojas[0].Widgets, w => w.SpecJson is not null); // el spec viajo
        Assert.Single(impo.Hojas[1].Widgets);

        // Aislamiento: el libro importado vive en B; el tenant A no lo ve ni siendo Admin.
        Assert.Null(await svcA.ObtenerLibroAsync(impo.Id, ActorAdmin));

        // JSON ajeno -> error claro, sin crear nada.
        Assert.False((await Portab(b).ImportarAsync("{\"foo\":1}", ActorOtro)).Ok);
    }

    [Fact]
    public async Task Ola7_KpiConPeriodo_Calendario_Y_TiposNuevos_CorrenContraDatosReales()
    {
        var tenantId = await SeedTenantAsync("Libros ola 7", conDatos: true);
        var conteo = new ReportAggregate(string.Empty, ReportAggregateFunction.Count);
        AnalisisWidgetDto W(AnalisisTipoWidget tipo, string titulo, ReportQuerySpec spec, WidgetConfig? cfg = null) =>
            new(Guid.NewGuid(), Guid.NewGuid(), tipo, titulo, AnalisisJson.Serialize(spec), null, cfg?.ToJson(), 0, 1, 1);

        var tareas = new ReportQuerySpec { SourceKey = TaskItemReportSource.SourceKey, Aggregates = new[] { conteo } };
        var kpiMes = W(AnalisisTipoWidget.Kpi, "Creadas este mes", tareas, new WidgetConfig { Periodo = KpiPeriodos.Mes });
        var horas30 = W(AnalisisTipoWidget.Velocimetro, "Horas 30 dias",
            new ReportQuerySpec { SourceKey = WorkLogReportSource.SourceKey, Aggregates = new[] { new ReportAggregate("Horas", ReportAggregateFunction.Sum) } },
            new WidgetConfig { Periodo = KpiPeriodos.Ultimos30, Meta = 10 });
        var calendario = W(AnalisisTipoWidget.Calendario, "Tareas por dia", tareas);
        var embudo = W(AnalisisTipoWidget.Embudo, "Embudo por estado", tareas with { GroupBy = new[] { "Status" } });
        var apiladas = W(AnalisisTipoWidget.BarrasApiladas, "Estado x prioridad", tareas with { GroupBy = new[] { "Status", "Priority" } });

        var r = new Dictionary<string, AnalisisWidgetResultado>();
        await RunWithRunnerAsync(tenantId, async runner =>
        {
            foreach (var w in new[] { kpiMes, horas30, calendario, embudo, apiladas })
            {
                r[w.Titulo] = await runner.RunAsync(w, new ReportContext(tenantId));
            }
        });
        Assert.All(r, kv => Assert.True(kv.Value.Ok, $"{kv.Key}: {kv.Value.Error}"));

        // KPI del mes: las 3 tareas se crearon hoy; el mes anterior no tiene base -> sin delta, con tendencia.
        var kpi = Assert.IsType<EscalarShape>(r["Creadas este mes"].Forma);
        Assert.Equal(3m, kpi.Valor);
        Assert.Equal(0m, kpi.ValorAnterior);
        Assert.Null(kpi.Delta);
        Assert.Equal("vs mes anterior", kpi.Comparacion);
        Assert.Equal(6, kpi.Tendencia.Count);
        Assert.Equal(3m, kpi.Tendencia[^1].Valor);

        // Velocimetro sobre Horas (suma) en los ultimos 30 dias: 1.5 h + 1 h.
        Assert.Equal(2.5m, Assert.IsType<EscalarShape>(r["Horas 30 dias"].Forma).Valor);

        // Calendario: un solo dia con actividad (hoy) con las 3 tareas.
        var cal = Assert.IsType<CalendarioShape>(r["Tareas por dia"].Forma);
        Assert.Equal(3m, Assert.Single(cal.Dias).Valor);
        Assert.Equal(cal.Hasta, cal.Dias[0].Dia);

        Assert.Equal(3m, Assert.IsType<CategoriasShape>(r["Embudo por estado"].Forma).Total);
        Assert.NotEmpty(Assert.IsType<SerieShape>(r["Estado x prioridad"].Forma).Series);

        // Cada forma produce su grafico (lo que la tarjeta entrega a ECharts).
        Assert.NotNull(AnalisisGraficos.Opcion(r["Horas 30 dias"].Forma!, AnalisisTipoWidget.Velocimetro, WidgetConfig.Leer(horas30.ConfigJson), v => v?.ToString() ?? "-"));
        Assert.NotNull(AnalisisGraficos.Opcion(cal, AnalisisTipoWidget.Calendario, new WidgetConfig(), v => v?.ToString() ?? "-"));
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
        await action(new AnalisisWidgetRunner(data, Registry(data), catalog));
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

        var cliente = new Tercero { TenantId = tenantId, Nombre = "ACME", Tipo = TerceroTipo.Empresa, Perfiles = TerceroPerfil.Cliente, Vendedor = "Laura" };
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
