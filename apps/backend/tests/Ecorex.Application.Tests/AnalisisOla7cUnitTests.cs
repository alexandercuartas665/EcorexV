using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0158 Ola 7c (puro, sin BD ni interop): el elemento tocado de un grafico -> condiciones; las condiciones
/// -> filtros sobre la misma fuente y sobre OTRAS fuentes (por el parametro de hoja equivalente); y las
/// columnas del panel de registros.
/// </summary>
public class AnalisisOla7cUnitTests
{
    private static readonly TimeZoneInfo MenosCinco = TimeZoneInfo.CreateCustomTimeZone("T-5", TimeSpan.FromHours(-5), "T-5", "T-5");

    private static readonly ReportSourceDescriptor Tareas = new(TaskItemReportSource.SourceKey, "Actividades", ReportSourceKind.Native, new[]
    {
        new ReportField("Number", "Numero", ReportFieldType.Text),
        new ReportField("Title", "Titulo", ReportFieldType.Text),
        new ReportField("Status", "Estado", ReportFieldType.Text),
        new ReportField("Priority", "Prioridad", ReportFieldType.Text),
        new ReportField("CreatedAt", "Creada", ReportFieldType.Date),
        new ReportField("Board", "Tablero", ReportFieldType.Text),
        new ReportField("Vendedor", "Vendedor del cliente", ReportFieldType.Text),
        new ReportField("Vencida", "Vencida", ReportFieldType.Boolean),
        new ReportField("CreatedMonth", "Mes de creacion", ReportFieldType.Text),
        new ReportField("Id", "Id de la tarea", ReportFieldType.Text, CanGroup: false)
    });

    private static readonly ReportSourceDescriptor Directorio = new(TerceroReportSource.SourceKey, "Directorio", ReportSourceKind.Native, new[]
    {
        new ReportField("Id", "Id", ReportFieldType.Text, CanGroup: false),
        new ReportField("Nombre", "Nombre", ReportFieldType.Text),
        new ReportField("Tipo", "Tipo", ReportFieldType.Text),
        new ReportField("Vendedor", "Vendedor", ReportFieldType.Text),
        new ReportField("CreatedAt", "Creado", ReportFieldType.Date)
    });

    private static readonly ReportSourceDescriptor Contenedor = new("container:abc", "Ventas", ReportSourceKind.Container, new[]
    {
        new ReportField("c0", "Registro (ID)", ReportFieldType.Text, CanGroup: false),
        new ReportField("ClienteId", "Cliente", ReportFieldType.Text),
        new ReportField("c1", "Cliente", ReportFieldType.Text),
        new ReportField("c2", "Fecha factura", ReportFieldType.Date),
        new ReportField("c3", "Ciudad", ReportFieldType.Text),
        new ReportField("c4", "Total", ReportFieldType.Decimal, CanAggregate: true)
    });

    private static readonly ReportAggregate Conteo = new(string.Empty, ReportAggregateFunction.Count);

    private static AnalisisWidgetDto W(AnalisisTipoWidget tipo, params string[] grupos) =>
        new(Guid.NewGuid(), Guid.NewGuid(), tipo, "W",
            AnalisisJson.Serialize(new ReportQuerySpec { SourceKey = TaskItemReportSource.SourceKey, GroupBy = grupos, Aggregates = new[] { Conteo } }),
            null, null, 0, 1, 1);

    private static CategoriasShape Cats(params string[] etiquetas) =>
        new(etiquetas.Select((e, i) => new CategoriaItem(e, 10 - i)).ToList());

    // ---------------------------------------------------------------- elemento tocado -> condiciones

    [Fact]
    public void Barras_ElementoTocado_EsLaPrimeraAgrupacion()
    {
        var c = AnalisisCruce.Condiciones(W(AnalisisTipoWidget.Barras, "Status"), Cats("Active", "Done"), "Active", "Valor")!;

        var unica = Assert.Single(c);
        Assert.Equal("Status", unica.Campo);
        Assert.Equal("Active", unica.Valor);
    }

    [Fact]
    public void Otros_DelTop_Y_SinDato_NoSePuedenFiltrar_PeroUnValorRealLlamadoOtrosSi()
    {
        var w = W(AnalisisTipoWidget.Dona, "Status"); // top por defecto = 8
        var recortada = Cats("a", "b", "c", "d", "e", "f", "g", "h", "Otros");
        Assert.Null(AnalisisCruce.Condiciones(w, recortada, "Otros", null));
        Assert.Null(AnalisisCruce.Condiciones(w, Cats("a", WidgetShapes.SinDato), WidgetShapes.SinDato, null));

        // Tres categorias, una se llama "Otros" de verdad: no es el grupo sintetico.
        Assert.NotNull(AnalisisCruce.Condiciones(w, Cats("a", "Otros", "b"), "Otros", null));
    }

    [Fact]
    public void Serie_ConDosAgrupaciones_UsaEjeYSerie_ConUnaSoloElEje()
    {
        var forma = new SerieShape(new[] { "2026-10" }, new[] { new SerieDatos("Active", new decimal?[] { 3 }) });

        var dos = AnalisisCruce.Condiciones(W(AnalisisTipoWidget.BarrasApiladas, "CreatedMonth", "Status"), forma, "2026-10", "Active")!;
        Assert.Equal(new[] { "CreatedMonth", "Status" }, dos.Select(x => x.Campo));
        Assert.Equal(new[] { "2026-10", "Active" }, dos.Select(x => x.Valor));

        // Una agrupacion: el nombre de la serie es la medida ("Conteo"), no un valor a filtrar.
        var una = AnalisisCruce.Condiciones(W(AnalisisTipoWidget.Linea, "CreatedMonth"), forma, "2026-10", "Conteo")!;
        Assert.Equal("CreatedMonth", Assert.Single(una).Campo);

        // Serie "(sin dato)": no se puede filtrar.
        Assert.Null(AnalisisCruce.Condiciones(W(AnalisisTipoWidget.BarrasApiladas, "CreatedMonth", "Status"), forma, "2026-10", WidgetShapes.SinDato));
    }

    [Fact]
    public void MapaDeCalor_FilaYColumna_Y_Calendario_ElDiaPorParametroFecha()
    {
        var hm = new HeatmapShape(new[] { "Active" }, new[] { "High" }, new[] { (IReadOnlyList<decimal?>)new decimal?[] { 2 } });
        var c = AnalisisCruce.Condiciones(W(AnalisisTipoWidget.Heatmap, "Status", "Priority"), hm, "Active", "High")!;
        Assert.Equal(new[] { "Status", "Priority" }, c.Select(x => x.Campo));

        var cal = new CalendarioShape(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new[] { new DiaValor(new DateOnly(2026, 11, 3), 4) });
        var dia = Assert.Single(AnalisisCruce.Condiciones(W(AnalisisTipoWidget.Calendario), cal, "2026-11-03", null)!);
        Assert.Null(dia.Campo);
        Assert.Equal(HojaFiltroBinder.Fecha, dia.Parametro);
        Assert.Null(AnalisisCruce.Condiciones(W(AnalisisTipoWidget.Calendario), cal, "noviembre", null));
    }

    [Fact]
    public void Kpi_Tabla_Curados_Y_Combinados_NoOriginanSeleccion()
    {
        Assert.False(AnalisisCruce.AdmiteSeleccion(W(AnalisisTipoWidget.Kpi)));
        Assert.False(AnalisisCruce.AdmiteSeleccion(W(AnalisisTipoWidget.Velocimetro)));
        Assert.False(AnalisisCruce.AdmiteSeleccion(W(AnalisisTipoWidget.Tabla, "Status")));
        Assert.False(AnalisisCruce.AdmiteSeleccion(W(AnalisisTipoWidget.Semaforo, "Status")));
        var curado = new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Dona, "S", null,
            SemaforoVencimientoIndicador.IndicadorKey, null, 0, 1, 1);
        Assert.False(AnalisisCruce.AdmiteSeleccion(curado));
        Assert.Null(AnalisisCruce.Condiciones(curado, Cats("Al dia"), "Al dia", null));
        Assert.True(AnalisisCruce.AdmiteSeleccion(W(AnalisisTipoWidget.Treemap, "Status")));
    }

    // ---------------------------------------------------------------- resolver con la fuente de origen

    [Fact]
    public void Resolver_CompletaCampoNombreYParametroDeHoja()
    {
        var r = AnalisisCruce.Resolver(new[]
        {
            new CondicionCruzada("Vendedor", "Laura"),
            new CondicionCruzada("Status", "Active"),
            new CondicionCruzada(null, "2026-11-03", HojaFiltroBinder.Fecha)
        }, Tareas, null);

        Assert.Equal(HojaFiltroBinder.Vendedor, r[0].Parametro);
        Assert.Equal("Vendedor del cliente", r[0].CampoNombre);
        Assert.Null(r[1].Parametro);
        Assert.Equal("Estado", r[1].CampoNombre);
        Assert.Equal("CreatedAt", r[2].Campo);

        var sel = new SeleccionCruzada(Guid.NewGuid(), Tareas.Key, r.Take(2).ToList());
        Assert.Equal("Vendedor del cliente: Laura, Estado: Active", sel.Etiqueta);
        Assert.Equal(("Laura", "Active"), sel.Elemento);
        Assert.True(sel.MismosValores(new[] { new CondicionCruzada(null, "Laura"), new CondicionCruzada(null, "Active") }));
        Assert.False(sel.MismosValores(new[] { new CondicionCruzada(null, "Laura") }));
    }

    // ---------------------------------------------------------------- seleccion -> filtros de otro widget

    [Fact]
    public void MismaFuente_TextoBooleanoYFecha_SeTraducenAlTipoDelCampo()
    {
        var sel = Sel(Tareas, new CondicionCruzada("Status", "Active"), new CondicionCruzada("Vencida", "Si"));
        var f = AnalisisCruce.Filtros(sel, Tareas, null, null)!;
        Assert.Equal(("Status", ReportFilterOperator.Equals, "Active"), (f[0].FieldKey, f[0].Operator, f[0].Values[0]));
        Assert.Equal(("Vencida", "true"), (f[1].FieldKey, f[1].Values[0]));

        // Un dia (calendario): [00:00, 00:00 del dia siguiente) en la zona del tenant.
        var dia = AnalisisCruce.Filtros(Sel(Tareas, new CondicionCruzada(null, "2026-11-03", HojaFiltroBinder.Fecha)), Tareas, null, MenosCinco)!;
        Assert.Equal(2, dia.Count);
        Assert.Equal(("CreatedAt", ReportFilterOperator.GreaterThanOrEqual, "2026-11-03T00:00:00-05:00"), (dia[0].FieldKey, dia[0].Operator, dia[0].Values[0]));
        Assert.Equal((ReportFilterOperator.LessThan, "2026-11-04T00:00:00-05:00"), (dia[1].Operator, dia[1].Values[0]));
    }

    [Fact]
    public void OtraFuente_ViajaPorElParametroDeHoja_O_NoAplica()
    {
        // Vendedor de la tarea -> Vendedor del tercero (mismo parametro de hoja).
        var porVendedor = AnalisisCruce.Filtros(Sel(Tareas, new CondicionCruzada("Vendedor", "Laura")), Directorio, null, null)!;
        Assert.Equal(("Vendedor", "Laura"), (Assert.Single(porVendedor).FieldKey, porVendedor[0].Values[0]));

        // El estado de la tarea no existe en el Directorio: la seleccion no aplica (no se filtra a medias).
        Assert.Null(AnalisisCruce.Filtros(Sel(Tareas, new CondicionCruzada("Status", "Active")), Directorio, null, null));
        Assert.Null(AnalisisCruce.Filtros(Sel(Tareas, new CondicionCruzada("Vendedor", "Laura"), new CondicionCruzada("Status", "Active")), Directorio, null, null));

        // Un widget que pidio NO usar el parametro vendedor ("" en sus bindings) tampoco lo recibe.
        Assert.Null(AnalisisCruce.Filtros(Sel(Tareas, new CondicionCruzada("Vendedor", "Laura")), Directorio, "{\"vendedor\":\"\"}", null));
    }

    // ---------------------------------------------------------------- columnas del panel de registros

    [Fact]
    public void CamposDetalle_AgrupadosPrimero_IdDeEnlaceAlFinal_SinCamposId()
    {
        var spec = new ReportQuerySpec { SourceKey = Tareas.Key, GroupBy = new[] { "Priority" }, Aggregates = new[] { Conteo } };
        var campos = AnalisisCruce.CamposDetalle(Tareas, spec);
        Assert.Equal("Priority", campos[0]);
        Assert.Equal("Id", campos[^1]);
        Assert.Contains("Title", campos);
        Assert.Equal(("tarea", "Id"), AnalisisCruce.CampoEnlace(Tareas));

        var cont = AnalisisCruce.CamposDetalle(Contenedor, new ReportQuerySpec
        {
            SourceKey = Contenedor.Key,
            GroupBy = new[] { "c3" },
            Aggregates = new[] { new ReportAggregate("c4", ReportAggregateFunction.Sum) }
        });
        Assert.Equal(new[] { "c3", "c4", "c1", "c2" }, cont);
        Assert.Null(AnalisisCruce.CampoEnlace(Contenedor));
    }

    /// <summary>Seleccion como la arma la pagina: condiciones resueltas con la fuente de origen.</summary>
    private static SeleccionCruzada Sel(ReportSourceDescriptor fuente, params CondicionCruzada[] c) =>
        new(Guid.NewGuid(), fuente.Key, AnalisisCruce.Resolver(c, fuente, null));
}
