using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0158 Ola 3 (puro, sin BD): diseno del widget &lt;-&gt; spec, validacion contra el catalogo,
/// presentacion (formato/tono/umbrales), filtros globales de hoja y su enlace a cada fuente, y la
/// agregacion en memoria del KPI sin dimension.
/// </summary>
public class AnalisisOla3UnitTests
{
    private static readonly ReportSourceDescriptor Tareas = new TaskItemReportSource(null!).Describe();
    private static readonly ReportSourceDescriptor Horas = new WorkLogReportSource(null!).Describe();

    // Fuente tipo contenedor (campos con nombres del usuario): sin mapa conocido.
    private static readonly ReportSourceDescriptor Contenedor = new("container:abc", "Ventas Siigo", ReportSourceKind.Container, new[]
    {
        new ReportField("c1", "Cliente", ReportFieldType.Text),
        new ReportField("c2", "Fecha factura", ReportFieldType.Date),
        new ReportField("c3", "Vendedor asignado", ReportFieldType.Text),
        new ReportField("c4", "Total", ReportFieldType.Decimal, CanAggregate: true)
    });

    private static WidgetDiseno Diseno() => new()
    {
        Tipo = AnalisisTipoWidget.Barras,
        Titulo = "Horas por usuario",
        SourceKey = Horas.Key,
        Funcion = ReportAggregateFunction.Sum,
        CampoMedida = "Horas",
        Dimension = "Usuario",
        Filtros = new[]
        {
            new WidgetFiltroDiseno("Fecha", ReportFilterOperator.Between, "2026-01-01", "2026-06-30"),
            new WidgetFiltroDiseno("Tipo", ReportFilterOperator.In, "Manual, Timer")
        },
        ColSpan = 2,
        Config = new WidgetConfig { Top = 5, Formato = "decimal", Tono = "green" }
    };

    [Fact]
    public void ToSpec_BuildsAggregatedSpec_SortedByMeasure()
    {
        var spec = WidgetDisenoMapper.ToSpec(Diseno());
        Assert.Equal(Horas.Key, spec.SourceKey);
        Assert.Equal(new[] { "Usuario" }, spec.GroupBy);
        Assert.Equal(new ReportAggregate("Horas", ReportAggregateFunction.Sum), spec.Aggregates.Single());
        Assert.Equal(new ReportSort("Sum_Horas", Descending: true), spec.Sort.Single());
        Assert.Equal(new[] { "Manual", "Timer" }, spec.Filters[1].Values);
        Assert.Equal(new[] { "2026-01-01", "2026-06-30" }, spec.Filters[0].Values);
    }

    [Fact]
    public void FromWidget_RoundTripsTheDesign()
    {
        var original = Diseno() with { OrdenarPorMedida = false };
        var nuevo = WidgetDisenoMapper.ToNuevoWidget(original);
        var dto = new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), nuevo.Tipo, nuevo.Titulo,
            AnalisisJson.Serialize(nuevo.Spec!), null, nuevo.ConfigJson, 0, nuevo.ColSpan, 1);

        var back = WidgetDisenoMapper.FromWidget(dto);
        Assert.Equal(original.SourceKey, back.SourceKey);
        Assert.Equal(original.Funcion, back.Funcion);
        Assert.Equal(original.CampoMedida, back.CampoMedida);
        Assert.Equal(original.Dimension, back.Dimension);
        Assert.False(back.OrdenarPorMedida);
        Assert.Equal("Manual, Timer", back.Filtros[1].Valor);
        Assert.Equal("2026-06-30", back.Filtros[0].Valor2);
        Assert.Equal(original.Config, back.Config);
        Assert.Equal(2, back.ColSpan);
    }

    [Fact]
    public void Kpi_WithoutDimension_HasNoGroupNorSort()
    {
        var spec = WidgetDisenoMapper.ToSpec(Diseno() with { Tipo = AnalisisTipoWidget.Kpi, Dimension = null });
        Assert.Empty(spec.GroupBy);
        Assert.Empty(spec.Sort);
        Assert.Empty(WidgetDisenoMapper.Validar(Diseno() with { Tipo = AnalisisTipoWidget.Kpi, Dimension = null }, Horas));
    }

    [Fact]
    public void Validar_ReportsEachProblem()
    {
        Assert.Empty(WidgetDisenoMapper.Validar(Diseno(), Horas));
        Assert.Contains(WidgetDisenoMapper.Validar(Diseno(), null), e => e.Contains("fuente"));
        Assert.Contains(WidgetDisenoMapper.Validar(Diseno() with { Titulo = " " }, Horas), e => e.Contains("titulo"));
        Assert.Contains(WidgetDisenoMapper.Validar(Diseno() with { CampoMedida = "Usuario" }, Horas), e => e.Contains("numerico"));
        Assert.Contains(WidgetDisenoMapper.Validar(Diseno() with { Dimension = null }, Horas), e => e.Contains("agrupar"));
        Assert.Contains(WidgetDisenoMapper.Validar(Diseno() with { Dimension = "TareaTitulo" }, Horas), e => e.Contains("TareaTitulo"));
        Assert.Contains(WidgetDisenoMapper.Validar(Diseno() with { Filtros = new[] { new WidgetFiltroDiseno("NoExiste", ReportFilterOperator.Equals, "x") } }, Horas), e => e.Contains("NoExiste"));
        Assert.Contains(WidgetDisenoMapper.Validar(Diseno() with { Filtros = new[] { new WidgetFiltroDiseno("Fecha", ReportFilterOperator.Between, "2026-01-01") } }, Horas), e => e.Contains("Completa"));
    }

    [Fact]
    public void Config_RoundTrips_AndDropsInvalidValues()
    {
        var cfg = new WidgetConfig { Top = 5, Formato = "moneda", Tono = "violet", UmbralBajo = 10, UmbralAlto = 20, MayorEsMejor = false };
        Assert.Equal(cfg, WidgetConfig.Leer(cfg.ToJson()));
        Assert.Null(new WidgetConfig().ToJson());
        var raro = WidgetConfig.Leer(new WidgetConfig { Formato = "raro", Tono = "fucsia", Top = 0 }.ToJson());
        Assert.Equal(new WidgetConfig(), raro);
        Assert.Equal(new WidgetConfig(), WidgetConfig.Leer("{roto"));
        Assert.Equal(new WidgetConfig(), WidgetConfig.Leer("[1]"));
        Assert.Equal(7, WidgetConfig.Leer("{\"top\":7}").Top); // compatible con el ConfigJson de la Ola 2
    }

    [Theory]
    [InlineData(5, true, "rose")]
    [InlineData(15, true, "amber")]
    [InlineData(25, true, "green")]
    [InlineData(5, false, "green")]
    [InlineData(25, false, "rose")]
    public void TonoPorUmbral_FollowsDirection(int valor, bool mayorEsMejor, string esperado)
    {
        var cfg = new WidgetConfig { UmbralBajo = 10, UmbralAlto = 20, MayorEsMejor = mayorEsMejor };
        Assert.Equal(esperado, cfg.TonoPorUmbral(valor));
        Assert.Null(new WidgetConfig().TonoPorUmbral(valor));
    }

    [Fact]
    public void Adaptar_AppliesThresholdToneToKpi()
    {
        var dto = new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Kpi, "K", null, null,
            new WidgetConfig { UmbralBajo = 10, UmbralAlto = 20 }.ToJson(), 0, 1, 1);
        var kpi = Assert.IsType<EscalarShape>(AnalisisWidgetRunner.Adaptar(new EscalarShape(3, "K"), dto));
        Assert.Equal("rose", kpi.Tono);
    }

    [Theory]
    [InlineData(ReportAggregateFunction.Count, 4)]
    [InlineData(ReportAggregateFunction.Sum, 6)]
    [InlineData(ReportAggregateFunction.Avg, 2)]
    [InlineData(ReportAggregateFunction.Min, 1)]
    [InlineData(ReportAggregateFunction.Max, 3)]
    public void Agregar_InMemory_IgnoresNulls(ReportAggregateFunction fn, int esperado)
    {
        var valores = new object?[] { 1m, 2L, null, "3" };
        Assert.Equal(esperado, AnalisisWidgetRunner.Agregar(fn, valores));
    }

    [Fact]
    public void Agregar_Empty_SumIsZero_OthersNull()
    {
        Assert.Equal(0m, AnalisisWidgetRunner.Agregar(ReportAggregateFunction.Sum, Array.Empty<object?>()));
        Assert.Null(AnalisisWidgetRunner.Agregar(ReportAggregateFunction.Avg, Array.Empty<object?>()));
        Assert.Equal(0m, AnalisisWidgetRunner.Agregar(ReportAggregateFunction.Count, Array.Empty<object?>()));
    }

    [Fact]
    public void ToEscalar_EmptyDataset_IsZero()
    {
        var vacio = new ReportDataSet(new[] { new ReportColumn("Status", "Estado", ReportFieldType.Text), new ReportColumn("Count", "Conteo", ReportFieldType.Number) },
            Array.Empty<IReadOnlyList<object?>>());
        Assert.Equal(0m, WidgetShapes.ToEscalar(vacio, "K").Valor);
    }

    // ---- Filtros de hoja ----

    [Fact]
    public void HojaFiltros_Json_RoundTrip_AndEmptyIsNull()
    {
        var f = new HojaFiltros { Desde = new DateOnly(2026, 1, 1), Hasta = new DateOnly(2026, 1, 31), Vendedor = " Laura ", Tablero = "" };
        var back = HojaFiltros.Leer(f.ToJson());
        Assert.Equal(new DateOnly(2026, 1, 1), back.Desde);
        Assert.Equal("Laura", back.Vendedor);
        Assert.Null(back.Tablero);
        Assert.Null(new HojaFiltros { Vendedor = "  " }.ToJson());
        Assert.True(HojaFiltros.Leer("{roto").Vacio);
    }

    [Fact]
    public void Binder_UsesKnownMap_ConventionAndExplicitOverride()
    {
        Assert.Equal("CreatedAt", HojaFiltroBinder.Resolver(HojaFiltroBinder.Fecha, Tareas, null));
        Assert.Equal("Board", HojaFiltroBinder.Resolver(HojaFiltroBinder.Tablero, Tareas, null));
        Assert.Null(HojaFiltroBinder.Resolver(HojaFiltroBinder.Vendedor, Horas, null)); // horas no tiene vendedor

        // Convencion en contenedores: primer campo fecha y nombre que contiene "vendedor".
        Assert.Equal("c2", HojaFiltroBinder.Resolver(HojaFiltroBinder.Fecha, Contenedor, null));
        Assert.Equal("c3", HojaFiltroBinder.Resolver(HojaFiltroBinder.Vendedor, Contenedor, null));
        Assert.Null(HojaFiltroBinder.Resolver(HojaFiltroBinder.Tablero, Contenedor, null));

        // Override explicito, "" = no aplicar, campo invalido = no aplicar.
        Assert.Equal("DueDate", HojaFiltroBinder.Resolver(HojaFiltroBinder.Fecha, Tareas, "{\"fecha\":\"DueDate\"}"));
        Assert.Null(HojaFiltroBinder.Resolver(HojaFiltroBinder.Fecha, Tareas, "{\"fecha\":\"\"}"));
        Assert.Null(HojaFiltroBinder.Resolver(HojaFiltroBinder.Fecha, Tareas, "{\"fecha\":\"Status\"}")); // no es fecha
    }

    [Fact]
    public void Binder_Filtros_DateRangeIsInclusiveByDay()
    {
        var hoja = new HojaFiltros { Desde = new DateOnly(2026, 3, 1), Hasta = new DateOnly(2026, 3, 31), Vendedor = "Laura", Tablero = "Comercial" };

        var tareas = HojaFiltroBinder.Filtros(hoja, Tareas, null);
        Assert.Contains(tareas, f => f.FieldKey == "CreatedAt" && f.Operator == ReportFilterOperator.GreaterThanOrEqual && f.Values[0] == "2026-03-01");
        Assert.Contains(tareas, f => f.FieldKey == "CreatedAt" && f.Operator == ReportFilterOperator.LessThan && f.Values[0] == "2026-04-01");
        Assert.Contains(tareas, f => f.FieldKey == "Vendedor" && f.Operator == ReportFilterOperator.Contains);
        Assert.Contains(tareas, f => f.FieldKey == "Board");

        var horas = HojaFiltroBinder.Filtros(hoja, Horas, null);
        Assert.DoesNotContain(horas, f => f.FieldKey == "Vendedor");
        Assert.Contains(horas, f => f.FieldKey == "Tablero");

        Assert.Empty(HojaFiltroBinder.Filtros(new HojaFiltros(), Tareas, null));
        Assert.Single(HojaFiltroBinder.Filtros(new HojaFiltros { Hasta = new DateOnly(2026, 3, 31) }, Tareas, null));
    }
}
