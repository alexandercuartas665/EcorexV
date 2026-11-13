using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.SuperAdmin.Agents;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// Paso "Crear actividad" del dron (2026-11-12): partes puras (resolucion de rutas/plantillas, filas de la
/// grilla, normalizacion por tipo de control) y el calculo de los pasos que consume un bucle en la corrida
/// por pasos. La creacion de la tarea y el anti-duplicado contra BD se validan E2E en local.
/// </summary>
public class ScrapeCrearActividadTests
{
    private static ScrapeRunContext CtxConDetalle()
    {
        var ctx = new ScrapeRunContext();
        ctx.Set("DETALLE", """
            {"cabecera":{"petof":"1501899999","solicitud":"SOL-7","fechaCotizacion":"13/10/2026","estado":"Pendiente"},
             "items":[{"item":"10","codigo":"A1","material":"Tornillo","cntSol":"5"},
                      {"item":"20","codigo":"B2","material":"Tuerca","cntSol":"12"}]}
            """);
        return ctx;
    }

    [Fact]
    public void Resolve_ruta_y_plantilla()
    {
        var ctx = CtxConDetalle();
        Assert.Equal("1501899999", ScrapeCrearActividad.Resolve(ctx, "DETALLE.cabecera.petof"));
        Assert.Equal("Pedido 1501899999 - SOL-7", ScrapeCrearActividad.Resolve(ctx, "Pedido {{DETALLE.cabecera.petof}} - {{DETALLE.cabecera.solicitud}}"));
        // Lo no resuelto de una plantilla se quita (no queda "{{...}}" en el titulo de la tarea).
        Assert.Equal("Pedido", ScrapeCrearActividad.Resolve(ctx, "Pedido {{NO.EXISTE}}"));
        Assert.Equal("", ScrapeCrearActividad.Resolve(ctx, "NO.EXISTE"));
        Assert.Equal("", ScrapeCrearActividad.Resolve(ctx, null));
    }

    [Fact]
    public void BuildGridRows_mapea_columnas_por_item()
    {
        var cfg = new ScrapeCrearActividad.Config
        {
            ItemsRuta = "DETALLE.items",
            GrillaColumnas = new() { ["codigo"] = "codigo", ["material"] = "material", ["cnt_sol"] = "cntSol", ["marca"] = "marca" },
        };

        var filas = ScrapeCrearActividad.BuildGridRows(CtxConDetalle(), cfg);

        Assert.Equal(2, filas.Count);
        Assert.Equal("A1", filas[0]["codigo"]);
        Assert.Equal("Tuerca", filas[1]["material"]);
        Assert.Equal("12", filas[1]["cnt_sol"]);
        Assert.Null(filas[0]["marca"]); // propiedad que el item no trae
    }

    [Fact]
    public void BuildGridRows_sin_items_devuelve_vacio()
    {
        var cfg = new ScrapeCrearActividad.Config { ItemsRuta = "DETALLE.noHay", GrillaColumnas = new() { ["a"] = "a" } };
        Assert.Empty(ScrapeCrearActividad.BuildGridRows(CtxConDetalle(), cfg));
    }

    [Theory]
    [InlineData(FormControlType.Date, "13/10/2026", "2026-10-13")]
    [InlineData(FormControlType.Date, "2026-10-13", "2026-10-13")]
    [InlineData(FormControlType.Date, "no es fecha", "no es fecha")]
    [InlineData(FormControlType.Number, "478000", "478000")]
    [InlineData(FormControlType.Number, "1.234,5", "1234.5")]
    [InlineData(FormControlType.Number, "12.5", "12.5")]
    [InlineData(FormControlType.Toggle, "Si", "true")]
    [InlineData(FormControlType.Toggle, "0", "false")]
    [InlineData(FormControlType.Text, "  tal cual ", "  tal cual ")]
    public void Coerce_por_tipo_de_control(FormControlType tipo, string valor, string esperado)
    {
        Assert.Equal(esperado, ScrapeCrearActividad.Coerce(tipo, valor));
    }

    [Fact]
    public void Config_round_trip_json()
    {
        var cfg = new ScrapeCrearActividad.Config
        {
            SubcategoriaId = Guid.NewGuid(),
            Titulo = "Pedido {{DETALLE.cabecera.petof}}",
            Clave = "DETALLE.cabecera.petof",
            ContenedorId = Guid.NewGuid(),
            ColumnaClave = "PETOF",
            ColumnaTarea = "TAREA",
            Campos = new() { ["petof"] = "DETALLE.cabecera.petof" },
            GrillaCampo = "items",
            ItemsRuta = "DETALLE.items",
            GrillaColumnas = new() { ["codigo"] = "codigo" },
        };
        var back = ScrapeCrearActividad.Parse(ScrapeCrearActividad.Serialize(cfg));
        Assert.NotNull(back);
        Assert.Equal(cfg.SubcategoriaId, back!.SubcategoriaId);
        Assert.Equal("TAREA", back.ColumnaTarea);
        Assert.Equal("DETALLE.cabecera.petof", back.Campos!["petof"]);
        Assert.Null(ScrapeCrearActividad.Parse("no es json"));
    }

    // Pasos del DRON PLATAFORMAS INGENIOS (ordenes reales): dos bucles paginados.
    private static List<ScrapeStep> PasosIngenios()
    {
        ScrapeStep S(int o, string? loop = null, bool end = false, bool page = false)
            => new() { Id = Guid.NewGuid(), Order = o, Name = o.ToString(), LoopOverVar = loop, IsLoopEnd = end, IsPageNext = page };
        return
        [
            S(100000), S(101021),
            S(102011, loop: "LISTADO.compras"), S(102021), S(102521), S(102531), S(103011, end: true),
            S(104011, page: true), S(104021, page: true),
            S(105000), S(105021),
            S(105521, loop: "LISTADO_HST.compras"), S(105541), S(105561, end: true),
            S(105611, page: true), S(105621, page: true),
        ];
    }

    [Fact]
    public void LoopMemberIds_toma_cuerpo_y_avance_de_SU_bucle()
    {
        var pasos = PasosIngenios();
        var ids = BrowserRunService.LoopMemberIds(pasos, 2);
        var ordenes = pasos.Where(p => ids.Contains(p.Id)).Select(p => p.Order).ToList();

        Assert.Equal(new[] { 102011, 102021, 102521, 102531, 103011, 104011, 104021 }, ordenes);
    }

    [Fact]
    public void LoopMemberIds_segundo_bucle_no_toca_el_primero()
    {
        var pasos = PasosIngenios();
        var ids = BrowserRunService.LoopMemberIds(pasos, 11);
        var ordenes = pasos.Where(p => ids.Contains(p.Id)).Select(p => p.Order).ToList();

        Assert.Equal(new[] { 105521, 105541, 105561, 105611, 105621 }, ordenes);
    }

    [Fact]
    public void NeedsStepwise_solo_con_bucles_o_crear_actividad()
    {
        var conBucle = new ScrapeFlow { Name = "a", StartUrl = "x" };
        conBucle.Steps.Add(new ScrapeStep { Name = "b", LoopOverVar = "L.x" });
        var conActividad = new ScrapeFlow { Name = "a", StartUrl = "x" };
        conActividad.Steps.Add(new ScrapeStep { Name = "c", Kind = ScrapeStepKind.CrearActividad });
        var lineal = new ScrapeFlow { Name = "a", StartUrl = "x" };
        lineal.Steps.Add(new ScrapeStep { Name = "n", Kind = ScrapeStepKind.Navigate, Url = "https://x" });

        Assert.True(BrowserRunService.NeedsStepwise(conBucle));
        Assert.True(BrowserRunService.NeedsStepwise(conActividad));
        Assert.False(BrowserRunService.NeedsStepwise(lineal));
    }
}
