using System.Text.Json;
using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Domain.Enums;
using O = System.Collections.Generic.Dictionary<string, object?>;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0158 Ola 7a/7b (puro, sin BD ni interop): periodos del KPI (variacion al mismo corte y tendencia),
/// fechas en la zona del tenant, calendario, la "option" de ECharts de cada tipo, el mapeo dataset -> forma
/// de los tipos nuevos y la validacion del campo de fecha.
/// </summary>
public class AnalisisOla7UnitTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 12);
    private static readonly TimeZoneInfo MenosCinco = TimeZoneInfo.CreateCustomTimeZone("T-5", TimeSpan.FromHours(-5), "T-5", "T-5");

    private static readonly ReportSourceDescriptor ConFecha = new("container:abc", "Ventas", ReportSourceKind.Container, new[]
    {
        new ReportField("c1", "Cliente", ReportFieldType.Text),
        new ReportField("c2", "Fecha factura", ReportFieldType.Date),
        new ReportField("c4", "Total", ReportFieldType.Decimal, CanAggregate: true)
    });

    private static readonly ReportSourceDescriptor SinFecha = new("container:xyz", "Clientes", ReportSourceKind.Container, new[]
    {
        new ReportField("c1", "Cliente", ReportFieldType.Text),
        new ReportField("c4", "Total", ReportFieldType.Decimal, CanAggregate: true)
    });

    private static string F(decimal? v) => v?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    // ---------------------------------------------------------------- periodos

    [Fact]
    public void Mes_ActualHastaHoy_ComparaAlMismoCorte_YTendenciaDeSeisMeses()
    {
        var plan = KpiPeriodos.Planear(KpiPeriodos.Mes, null, Hoy)!;

        Assert.Equal(new VentanaFechas(new(2026, 11, 1), new(2026, 11, 13), "nov 26"), plan.Actual);
        Assert.Equal(new DateOnly(2026, 10, 1), plan.Anterior.Desde);
        Assert.Equal(new DateOnly(2026, 10, 13), plan.Anterior.HastaExclusivo); // 12 dias, igual que el actual
        Assert.Equal(6, plan.Serie.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), plan.Serie[0].Desde);
        Assert.Equal(new DateOnly(2026, 7, 1), plan.Serie[0].HastaExclusivo); // meses previos completos
        Assert.Equal("vs mes anterior", plan.Comparacion);
        Assert.Equal(new DateOnly(2026, 6, 1), plan.Desde);
    }

    [Fact]
    public void Mes_AlFinalDeUnMesLargo_LaComparacionNoPasaDelMesAnterior()
    {
        var plan = KpiPeriodos.Planear(KpiPeriodos.Mes, null, new DateOnly(2026, 3, 31))!;
        Assert.Equal(new DateOnly(2026, 2, 1), plan.Anterior.Desde);
        Assert.Equal(new DateOnly(2026, 3, 1), plan.Anterior.HastaExclusivo); // febrero completo, no se mete en marzo
    }

    [Fact]
    public void Rodante_Trimestre_Y_RangoDeLaHoja()
    {
        var d30 = KpiPeriodos.Planear(KpiPeriodos.Ultimos30, null, Hoy)!;
        Assert.Equal(new DateOnly(2026, 10, 14), d30.Actual.Desde);
        Assert.Equal(new DateOnly(2026, 11, 13), d30.Actual.HastaExclusivo);
        Assert.Equal(d30.Serie[^2], d30.Anterior);
        Assert.Equal(new DateOnly(2026, 9, 14), d30.Anterior.Desde);

        var tri = KpiPeriodos.Planear(KpiPeriodos.Trimestre, null, Hoy)!;
        Assert.Equal(new DateOnly(2026, 10, 1), tri.Actual.Desde);
        Assert.Equal("T4 26", tri.Actual.Etiqueta);

        // Un rango COMPLETO de la hoja manda: se compara con el rango anterior de igual largo.
        var hoja = new HojaFiltros { Desde = new DateOnly(2026, 10, 1), Hasta = new DateOnly(2026, 10, 10) };
        var rango = KpiPeriodos.Planear(KpiPeriodos.Mes, hoja, Hoy)!;
        Assert.Equal(new DateOnly(2026, 10, 1), rango.Actual.Desde);
        Assert.Equal(new DateOnly(2026, 10, 11), rango.Actual.HastaExclusivo);
        Assert.Equal(new DateOnly(2026, 9, 21), rango.Anterior.Desde);
        Assert.Equal("vs periodo anterior", rango.Comparacion);

        Assert.Null(KpiPeriodos.Planear(null, hoja, Hoy));
        Assert.Null(KpiPeriodos.Planear("semestre", null, Hoy));
    }

    [Fact]
    public void Variacion_EsRelativa_YSinBaseEsNula()
    {
        Assert.Equal(0.2m, KpiPeriodos.Variacion(120, 100));
        Assert.Equal(-0.2m, KpiPeriodos.Variacion(80, 100));
        Assert.Null(KpiPeriodos.Variacion(5, 0));
        Assert.Null(KpiPeriodos.Variacion(null, 3));
    }

    // ---------------------------------------------------------------- fechas y calendario

    [Fact]
    public void Dia_UsaLaZonaDelTenant()
    {
        var madrugadaUtc = new DateTimeOffset(2026, 11, 12, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 11), AnalisisFechas.Dia(madrugadaUtc, MenosCinco));
        Assert.Equal(new DateOnly(2026, 11, 12), AnalisisFechas.Dia(madrugadaUtc, null));
        Assert.Equal(new DateOnly(2026, 11, 5), AnalisisFechas.Dia("2026-11-05", MenosCinco));
        Assert.Equal(new DateOnly(2026, 11, 11), AnalisisFechas.Dia("2026-11-12T03:00:00Z", MenosCinco));
        Assert.Equal(new DateOnly(2026, 11, 12), AnalisisFechas.Dia("2026-11-12T03:00:00", MenosCinco)); // sin offset: dia calendario
        Assert.Null(AnalisisFechas.Dia("no es fecha", null));
        Assert.Equal(new DateOnly(2026, 11, 11), AnalisisFechas.Hoy(madrugadaUtc, MenosCinco));
    }

    [Fact]
    public void Kpi_CalculaActualAnteriorDeltaYTendencia()
    {
        var plan = KpiPeriodos.Planear(KpiPeriodos.Mes, null, Hoy)!;
        var filas = new List<(DateOnly, object?)>
        {
            (new(2026, 11, 2), 1), (new(2026, 11, 10), 1), (new(2026, 11, 12), 1), // 3 este mes (hasta hoy)
            (new(2026, 10, 3), 1), (new(2026, 10, 25), 1),                         // octubre: 1 al mismo corte, 2 en total
            (new(2026, 6, 15), 1)
        };

        var kpi = AnalisisFechas.Kpi("Creadas", plan, filas, ReportAggregateFunction.Count);
        Assert.Equal(3m, kpi.Valor);
        Assert.Equal(1m, kpi.ValorAnterior);
        Assert.Equal(2m, kpi.Delta); // +200 %
        Assert.Equal("vs mes anterior", kpi.Comparacion);
        Assert.Equal(new[] { "1", "0", "0", "0", "2", "3" }, kpi.Tendencia.Select(p => F(p.Valor)));
        Assert.Equal("nov 26", kpi.Tendencia[^1].Etiqueta);
    }

    [Fact]
    public void PorDia_AgregaPorDiaDentroDelRango()
    {
        var filas = new List<(DateOnly, object?)>
        {
            (new(2026, 11, 1), 10m), (new(2026, 11, 1), 5m), (new(2026, 11, 3), 2m), (new(2026, 12, 1), 99m)
        };

        var cal = AnalisisFechas.PorDia(new(2026, 10, 1), new(2026, 11, 30), filas, ReportAggregateFunction.Sum);
        Assert.Equal(new[] { new DiaValor(new(2026, 11, 1), 15m), new DiaValor(new(2026, 11, 3), 2m) }, cal.Dias);
        Assert.Equal(17m, cal.Total);

        var (desde, hasta) = AnalisisFechas.RangoCalendario(null, Hoy);
        Assert.Equal(Hoy, hasta);
        Assert.Equal(365, hasta.DayNumber - desde.DayNumber + 1);

        var largo = new HojaFiltros { Desde = new DateOnly(2020, 1, 1), Hasta = Hoy };
        var (d2, h2) = AnalisisFechas.RangoCalendario(largo, Hoy);
        Assert.Equal(AnalisisFechas.MaxDiasCalendario, h2.DayNumber - d2.DayNumber + 1); // acotado a un ano
    }

    // ---------------------------------------------------------------- option de ECharts

    private static readonly CategoriasShape Cats = new(new[]
    {
        new CategoriaItem("Por hacer", 30), new CategoriaItem("En curso", 15), new CategoriaItem("Hecho", 5)
    });

    private static readonly SerieShape Serie = new(new[] { "2026-09", "2026-10" }, new[]
    {
        new SerieDatos("Abiertas", new decimal?[] { 4, 6 }), new SerieDatos("Cerradas", new decimal?[] { 2, null })
    });

    private static O Op(WidgetShape forma, AnalisisTipoWidget tipo, WidgetConfig? cfg = null) =>
        AnalisisGraficos.Opcion(forma, tipo, cfg ?? new WidgetConfig(), F)!;

    private static O[] Series(O o) => ((object[])(o.TryGetValue("baseOption", out var b) ? (O)b! : o)["series"]!).Cast<O>().ToArray();

    [Fact]
    public void SoloLosGraficosTienenOption()
    {
        Assert.False(AnalisisGraficos.EsGrafico(AnalisisTipoWidget.Kpi));
        Assert.False(AnalisisGraficos.EsGrafico(AnalisisTipoWidget.Semaforo));
        Assert.False(AnalisisGraficos.EsGrafico(AnalisisTipoWidget.Tabla));
        Assert.True(AnalisisGraficos.EsGrafico(AnalisisTipoWidget.Embudo));
        Assert.Null(AnalisisGraficos.Opcion(new EscalarShape(1, "K"), AnalisisTipoWidget.Kpi, new WidgetConfig(), F));
        Assert.Null(AnalisisGraficos.Opcion(new CategoriasShape(Array.Empty<CategoriaItem>()), AnalisisTipoWidget.Barras, new WidgetConfig(), F));
    }

    [Fact]
    public void Barras_VerticalesYHorizontales_ConTonosYFormato()
    {
        var o = Op(Cats, AnalisisTipoWidget.Barras, new WidgetConfig { Formato = "moneda", Tono = "green" });
        Assert.Equal("moneda", o["__fmt"]);
        Assert.Equal("bar", Series(o)[0]["type"]);
        Assert.Equal(new[] { "Por hacer", "En curso", "Hecho" }, (IEnumerable<string>)((O)o["xAxis"]!)["data"]!);

        var h = Op(Cats, AnalisisTipoWidget.BarrasHorizontales);
        var y = (O)h["yAxis"]!;
        Assert.Equal("category", y["type"]);
        Assert.Equal(true, y["inverse"]);
        Assert.Equal("numero", h["__fmt"]);

        Assert.Equal("var(--t-green)", AnalisisGraficos.Color("green", 0));
        Assert.Equal("var(--t-amber)", AnalisisGraficos.Color("green", 1));
        Assert.Equal("var(--t-blue)", AnalisisGraficos.Color("slate", 1)); // la paleta da la vuelta
    }

    [Fact]
    public void Apiladas_Pareto_Y_BarrasConLinea()
    {
        Assert.All(Series(Op(Serie, AnalisisTipoWidget.BarrasApiladas)), s => Assert.Equal("total", s["stack"]));

        var pareto = Series(Op(Cats, AnalisisTipoWidget.BarrasLinea));
        Assert.Equal("bar", pareto[0]["type"]);
        Assert.Equal("line", pareto[1]["type"]);
        Assert.Equal(1, pareto[1]["yAxisIndex"]);
        Assert.Equal(new[] { 60m, 90m, 100m }, (IEnumerable<decimal>)pareto[1]["data"]!);

        var combo = Series(Op(Serie, AnalisisTipoWidget.BarrasLinea));
        Assert.Equal("bar", combo[0]["type"]);
        Assert.Equal("line", combo[1]["type"]);
        Assert.Equal(1, combo[1]["yAxisIndex"]);
    }

    [Fact]
    public void Dona_Embudo_Y_MapaDeArbol()
    {
        var dona = Op(Cats, AnalisisTipoWidget.Dona, new WidgetConfig { Formato = "decimal" });
        Assert.Equal("pie", dona["__kind"]);
        Assert.True(dona.ContainsKey("media")); // leyenda abajo en tarjetas angostas
        Assert.Equal("50", ((O)((O)dona["baseOption"]!)["title"]!)["text"]); // total formateado
        Assert.Equal(3, ((System.Collections.ICollection)Series(dona)[0]["data"]!).Count);

        var embudo = Series(Op(Cats, AnalisisTipoWidget.Embudo))[0];
        Assert.Equal("funnel", embudo["type"]);
        Assert.Equal("descending", embudo["sort"]);

        var plano = Series(Op(Cats, AnalisisTipoWidget.Treemap))[0];
        Assert.Equal(false, plano["nodeClick"]);
        var dos = Series(Op(Serie, AnalisisTipoWidget.Treemap))[0];
        Assert.Equal("zoomToNode", dos["nodeClick"]);
        var padres = ((System.Collections.IEnumerable)dos["data"]!).Cast<O>().ToList();
        Assert.Equal(2, padres.Count);
        Assert.Single((System.Collections.ICollection)padres[1]["children"]!); // "Cerradas" sin dato en 2026-10 no entra
    }

    [Fact]
    public void Lineas_MapaDeCalor_Y_Calendario()
    {
        var ejes = Enumerable.Range(1, 30).Select(i => $"d{i:00}").ToList();
        var larga = new SerieShape(ejes, new[] { new SerieDatos("V", ejes.Select(_ => (decimal?)1).ToList()) });
        var area = Op(larga, AnalisisTipoWidget.Area);
        Assert.True(area.ContainsKey("dataZoom")); // series largas: zoom
        Assert.Equal(false, Series(area)[0]["showSymbol"]);
        Assert.True(Series(area)[0].ContainsKey("areaStyle"));
        Assert.False(Op(Serie, AnalisisTipoWidget.Linea).ContainsKey("dataZoom"));

        var hm = new HeatmapShape(new[] { "Alta", "Baja" }, new[] { "Abierta", "Cerrada" },
            new[] { new decimal?[] { 3, null }, new decimal?[] { 1, 7 } });
        var heat = Op(hm, AnalisisTipoWidget.Heatmap);
        Assert.Equal(3, ((System.Collections.ICollection)Series(heat)[0]["data"]!).Count);
        Assert.Equal(7m, ((O)heat["visualMap"]!)["max"]);

        var cal = new CalendarioShape(new(2026, 1, 1), new(2026, 3, 31), new[] { new DiaValor(new(2026, 2, 3), 4) });
        var c = Op(cal, AnalisisTipoWidget.Calendario);
        Assert.Equal("calendar", c["__kind"]);
        Assert.Equal(new[] { "2026-01-01", "2026-03-31" }, (IEnumerable<string>)((O)c["calendar"]!)["range"]!);
        Assert.Equal(190, AnalisisGraficos.Alto(cal, AnalisisTipoWidget.Calendario));
    }

    [Fact]
    public void Velocimetro_UsaLaMetaOEscalaRedonda_YBandasDeUmbral()
    {
        var conMeta = Series(Op(new EscalarShape(70, "Ventas"), AnalisisTipoWidget.Velocimetro, new WidgetConfig { Meta = 120 }))[0];
        Assert.Equal("gauge", conMeta["type"]);
        Assert.Equal(120m, conMeta["max"]);

        var auto = Series(Op(new EscalarShape(83, "Ventas"), AnalisisTipoWidget.Velocimetro))[0];
        Assert.Equal(100m, auto["max"]);

        var bandas = Series(Op(new EscalarShape(50, "V"), AnalisisTipoWidget.Velocimetro,
            new WidgetConfig { Meta = 100, UmbralBajo = 30, UmbralAlto = 70 }))[0];
        var color = (object[])((O)((O)bandas["axisLine"]!)["lineStyle"]!)["color"]!;
        Assert.Equal(3, color.Length);

        Assert.Equal(100m, AnalisisGraficos.EscalaRedonda(83));
        Assert.Equal(5m, AnalisisGraficos.EscalaRedonda(4));
        Assert.Equal(2000m, AnalisisGraficos.EscalaRedonda(1200));
        Assert.Equal(1m, AnalisisGraficos.EscalaRedonda(0)); // escala minima 1
    }

    [Fact]
    public void Tendencia_SoloConDosPuntosOMas_YSerializaComoJson()
    {
        Assert.Null(AnalisisGraficos.Tendencia(new EscalarShape(1, "K"), new WidgetConfig()));
        var kpi = new EscalarShape(5, "K") { Tendencia = new[] { new PuntoSerie("oct 26", 3), new PuntoSerie("nov 26", 5) } };
        var spark = AnalisisGraficos.Tendencia(kpi, new WidgetConfig())!;
        Assert.Equal("spark", spark["__kind"]);

        // La option completa se serializa (es lo que viaja al navegador).
        var json = JsonSerializer.Serialize(Op(Cats, AnalisisTipoWidget.Dona));
        Assert.Contains("\"type\":\"pie\"", json);
        Assert.Contains("var(--t-blue)", json);
    }

    // ---------------------------------------------------------------- mapeo, config y validacion

    [Fact]
    public void AFormar_TiposNuevos()
    {
        var unGrupo = new ReportDataSet(
            new[] { new ReportColumn("Stage", "Etapa", ReportFieldType.Text), new ReportColumn("Count", "Conteo", ReportFieldType.Number) },
            new IReadOnlyList<object?>[] { new object?[] { "A", 5L }, new object?[] { "B", 9L } });
        var dosGrupos = new ReportDataSet(
            new[]
            {
                new ReportColumn("Mes", "Mes", ReportFieldType.Text), new ReportColumn("Estado", "Estado", ReportFieldType.Text),
                new ReportColumn("Count", "Conteo", ReportFieldType.Number)
            },
            new IReadOnlyList<object?>[] { new object?[] { "2026-01", "Abierta", 3L }, new object?[] { "2026-01", "Cerrada", 1L } });
        AnalisisWidgetDto W(AnalisisTipoWidget t) => new(Guid.NewGuid(), Guid.NewGuid(), t, "W", null, null, null, 0, 1, 1);

        Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(unGrupo, W(AnalisisTipoWidget.Embudo)));
        Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(unGrupo, W(AnalisisTipoWidget.BarrasHorizontales)));
        Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(unGrupo, W(AnalisisTipoWidget.BarrasLinea)));
        Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(unGrupo, W(AnalisisTipoWidget.BarrasApiladas)));
        Assert.IsType<SerieShape>(AnalisisWidgetRunner.AFormar(dosGrupos, W(AnalisisTipoWidget.BarrasApiladas)));
        Assert.IsType<SerieShape>(AnalisisWidgetRunner.AFormar(dosGrupos, W(AnalisisTipoWidget.Treemap)));
        Assert.IsType<EscalarShape>(AnalisisWidgetRunner.AFormar(unGrupo, W(AnalisisTipoWidget.Velocimetro)));

        // Un curado de categorias pedido como velocimetro = su total.
        var v = Assert.IsType<EscalarShape>(AnalisisWidgetRunner.Adaptar(Cats, W(AnalisisTipoWidget.Velocimetro)));
        Assert.Equal(50m, v.Valor);
    }

    [Fact]
    public void Config_Y_TiposValidos()
    {
        Assert.Null(new WidgetConfig { Periodo = "semestre", Meta = -3 }.ToJson());
        var cfg = WidgetConfig.Leer(new WidgetConfig { Periodo = KpiPeriodos.Mes, Meta = 50 }.ToJson());
        Assert.Equal(KpiPeriodos.Mes, cfg.Periodo);
        Assert.Equal(50m, cfg.Meta);

        var barras = WidgetDisenoMapper.LimpiarConfig(AnalisisTipoWidget.Barras, cfg);
        Assert.Null(barras.Periodo);
        Assert.Null(barras.Meta);
        Assert.Equal(KpiPeriodos.Mes, WidgetDisenoMapper.LimpiarConfig(AnalisisTipoWidget.Kpi, cfg).Periodo);

        Assert.False(WidgetDisenoMapper.EsTipoValido(AnalisisTipoWidget.Echarts));
        Assert.False(WidgetDisenoMapper.EsTipoValido((AnalisisTipoWidget)99));
        Assert.True(WidgetDisenoMapper.EsTipoValido(AnalisisTipoWidget.Treemap));
        Assert.Equal(4, WidgetDisenoMapper.AnchoSugerido(AnalisisTipoWidget.Calendario));
    }

    [Fact]
    public void Validar_CalendarioYPeriodo_ExigenCampoDeFecha()
    {
        var calendario = new WidgetDiseno { Tipo = AnalisisTipoWidget.Calendario, Titulo = "Facturas por dia", SourceKey = ConFecha.Key };
        Assert.Empty(WidgetDisenoMapper.Validar(calendario, ConFecha)); // no pide dimension
        Assert.Contains(WidgetDisenoMapper.Validar(calendario with { SourceKey = SinFecha.Key }, SinFecha), e => e.Contains("campo de fecha"));

        // "No aplicar el rango" ("" en el binding) tambien deja al calendario sin fecha.
        var sinBinding = JsonSerializer.Serialize(new Dictionary<string, string> { [HojaFiltroBinder.Fecha] = string.Empty });
        Assert.NotEmpty(WidgetDisenoMapper.Validar(calendario, ConFecha, sinBinding));

        var kpi = new WidgetDiseno
        {
            Tipo = AnalisisTipoWidget.Kpi,
            Titulo = "Total",
            SourceKey = SinFecha.Key,
            Config = new WidgetConfig { Periodo = KpiPeriodos.Mes }
        };
        Assert.Contains(WidgetDisenoMapper.Validar(kpi, SinFecha), e => e.Contains("comparar periodos"));
        Assert.Empty(WidgetDisenoMapper.Validar(kpi with { Config = new WidgetConfig() }, SinFecha));
    }
}
