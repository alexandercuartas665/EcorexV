using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0158 Ola 1 (puro, sin BD): motor en memoria de las fuentes nuevas, normalizacion de NIT,
/// formas de widget y la clasificacion del semaforo de vencimiento.
/// </summary>
public class AnalisisWidgetShapesTests
{
    private static readonly ReportSourceDescriptor Desc = new("test:ventas", "Ventas", ReportSourceKind.Native, new[]
    {
        new ReportField("Region", "Region", ReportFieldType.Text),
        new ReportField("Mes", "Mes", ReportFieldType.Text),
        new ReportField("Monto", "Monto", ReportFieldType.Decimal, CanAggregate: true),
        new ReportField("Activo", "Activo", ReportFieldType.Boolean),
        new ReportField("Fecha", "Fecha", ReportFieldType.Date)
    });

    private static readonly List<object?[]> Rows = new()
    {
        new object?[] { "Norte", "2026-01", 100m, true, new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero) },
        new object?[] { "Norte", "2026-02", 50m, true, new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero) },
        new object?[] { "Sur", "2026-01", 200m, false, new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero) },
        new object?[] { null, "2026-02", 10m, true, null }
    };

    // ---- InMemoryReportEngine ----

    [Fact]
    public void Engine_GroupBySum_SortsDescending()
    {
        var ds = InMemoryReportEngine.Run(Desc, Rows, new ReportQuerySpec
        {
            SourceKey = Desc.Key,
            GroupBy = new[] { "Region" },
            Aggregates = new[] { new ReportAggregate("Monto", ReportAggregateFunction.Sum) },
            Sort = new[] { new ReportSort("Sum_Monto", Descending: true) }
        });

        Assert.Equal(new[] { "Region", "Sum_Monto" }, ds.Columns.Select(c => c.Key));
        Assert.Equal("Sur", ds.Rows[0][0]);
        Assert.Equal(200m, ds.Rows[0][1]);
        Assert.Equal(150m, ds.Rows[1][1]);
    }

    [Fact]
    public void Engine_TwoGroupFields_AndCount()
    {
        var ds = InMemoryReportEngine.Run(Desc, Rows, new ReportQuerySpec
        {
            SourceKey = Desc.Key,
            GroupBy = new[] { "Mes", "Region" },
            Aggregates = new[] { new ReportAggregate(string.Empty, ReportAggregateFunction.Count) }
        });

        Assert.Equal(4, ds.RowCount);
        Assert.Equal("Count", ds.Columns[2].Key);
    }

    [Fact]
    public void Engine_Filters_TypedOperators()
    {
        var spec = new ReportQuerySpec
        {
            SourceKey = Desc.Key,
            Fields = new[] { "Region" },
            Filters = new[]
            {
                new ReportFilter("Monto", ReportFilterOperator.GreaterThanOrEqual, new[] { "50" }),
                ReportFilter.Eq("Activo", "true"),
                ReportFilter.Between("Fecha", "2026-01-01", "2026-03-01")
            }
        };

        var ds = InMemoryReportEngine.Run(Desc, Rows, spec);
        Assert.Equal(2, ds.RowCount);
        Assert.All(ds.Rows, r => Assert.Equal("Norte", r[0]));
    }

    [Fact]
    public void Engine_NonNumericAggregate_IsRejected()
    {
        Assert.Throws<ReportValidationException>(() => InMemoryReportEngine.Run(Desc, Rows, new ReportQuerySpec
        {
            SourceKey = Desc.Key,
            GroupBy = new[] { "Mes" },
            Aggregates = new[] { new ReportAggregate("Region", ReportAggregateFunction.Sum) }
        }));
    }

    [Theory]
    [InlineData("900.123.456-7", "900123456")]
    [InlineData(" 900123456 ", "900123456")]
    [InlineData("cc 1.020.304", "CC1020304")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Nit_IsNormalized(string? raw, string? expected) =>
        Assert.Equal(expected, NitNormalizer.Normalize(raw));

    // ---- WidgetShapes ----

    private static ReportDataSet Agg(params string[] groups)
    {
        var spec = new ReportQuerySpec
        {
            SourceKey = Desc.Key,
            GroupBy = groups,
            Aggregates = new[] { new ReportAggregate("Monto", ReportAggregateFunction.Sum) }
        };
        return InMemoryReportEngine.Run(Desc, Rows, spec);
    }

    [Fact]
    public void Escalar_SumsTheMeasure()
    {
        var kpi = WidgetShapes.ToEscalar(Agg("Region"), "Ventas");
        Assert.Equal(360m, kpi.Valor);
        Assert.Equal("Ventas", kpi.Etiqueta);
    }

    [Fact]
    public void Categorias_LabelsNull_AndGroupsOthers()
    {
        var cat = WidgetShapes.ToCategorias(Agg("Region"), top: 2);
        Assert.Equal(new[] { "Sur", "Norte", "Otros" }, cat.Items.Select(i => i.Etiqueta));
        Assert.Equal(10m, cat.Items[2].Valor);
        Assert.Equal(360m, cat.Total);

        var all = WidgetShapes.ToCategorias(Agg("Region"));
        Assert.Contains(all.Items, i => i.Etiqueta == WidgetShapes.SinDato);
    }

    [Fact]
    public void Serie_AlignsSeriesOnSharedAxis()
    {
        var serie = WidgetShapes.ToSerie(Agg("Mes", "Region"));
        Assert.Equal(new[] { "2026-01", "2026-02" }, serie.Ejes);

        var norte = serie.Series.Single(s => s.Nombre == "Norte");
        Assert.Equal(new decimal?[] { 100m, 50m }, norte.Valores);

        var sur = serie.Series.Single(s => s.Nombre == "Sur");
        Assert.Equal(new decimal?[] { 200m, null }, sur.Valores);
    }

    [Fact]
    public void Serie_SingleSeries_UsesMeasureName()
    {
        var serie = WidgetShapes.ToSerie(Agg("Mes"));
        Assert.Single(serie.Series);
        Assert.Equal(new decimal?[] { 300m, 60m }, serie.Series[0].Valores);
    }

    [Fact]
    public void Heatmap_PivotsRowsAndColumns()
    {
        var hm = WidgetShapes.ToHeatmap(Agg("Region", "Mes"));
        Assert.Equal(new[] { "2026-01", "2026-02" }, hm.Columnas);
        var norte = hm.Filas.ToList().IndexOf("Norte");
        Assert.Equal(100m, hm.Valores[norte][0]);
        Assert.Equal(50m, hm.Valores[norte][1]);
    }

    // ---- Semaforo ----

    [Fact]
    public void Semaforo_ClassifiesOpenTasks_AndSkipsFinished()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        var rows = new (DateTimeOffset?, string?, DateTimeOffset?)[]
        {
            (now.AddDays(-1), "Active", null),     // vencida
            (now.AddDays(3), "Pending", null),     // por vencer
            (now.AddDays(30), "InProgress", null), // al dia
            (null, "Pending", null),               // sin fecha
            (now.AddDays(-5), "Done", null),       // terminada: no cuenta
            (now.AddDays(-5), "Active", now)       // con cierre: no cuenta
        };

        var shape = SemaforoVencimientoIndicador.Clasificar(rows, now);
        var byLabel = shape.Items.ToDictionary(i => i.Etiqueta, i => i.Valor);
        Assert.Equal(1m, byLabel["Vencidas"]);
        Assert.Equal(1m, byLabel["Por vencer"]);
        Assert.Equal(1m, byLabel["Al dia"]);
        Assert.Equal(1m, byLabel["Sin fecha"]);
        Assert.Equal("rose", shape.Items[0].Tono);
    }

    // ---- Catalogo de fuentes nuevas (Describe es puro) ----

    [Fact]
    public void NewSources_HaveStableKeys_AndAggregableMeasures()
    {
        var tercero = new TerceroReportSource(null!).Describe();
        var nota = new TerceroNotaReportSource(null!).Describe();
        var work = new WorkLogReportSource(null!).Describe();

        Assert.Equal("native:tercero", tercero.Key);
        Assert.Equal("native:tercero-nota", nota.Key);
        Assert.Equal("native:worklog", work.Key);
        Assert.True(nota.FindField("Valor")!.CanAggregate);
        Assert.True(work.FindField("Horas")!.CanAggregate);
        Assert.NotNull(tercero.FindField("NitNormalizado"));
    }

    [Fact]
    public void TaskItem_ExposesExtendedFields()
    {
        var d = new TaskItemReportSource(null!).Describe();
        foreach (var key in new[] { "Cliente", "ClienteNit", "Vendedor", "Proyecto", "CloseReason", "Vencida", "CreatedMonth", "DiasEnEtapa" })
        {
            Assert.NotNull(d.FindField(key));
        }

        Assert.True(d.FindField("DiasEnEtapa")!.CanAggregate);
    }
}
