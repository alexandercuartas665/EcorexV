using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0158 Ola 4 (puro, sin BD): contexto de embebido por fuente, segunda agrupacion (series / columnas
/// del mapa de calor), formas de los tipos nuevos y lectura de los widgets fijados en el Inicio.
/// </summary>
public class AnalisisOla4UnitTests
{
    private static readonly ReportSourceDescriptor Tareas = new TaskItemReportSource(null!).Describe();
    private static readonly ReportSourceDescriptor Directorio = new TerceroReportSource(null!).Describe();
    private static readonly ReportSourceDescriptor Contenedor = new("container:abc", "Ventas", ReportSourceKind.Container, new[]
    {
        new ReportField("c1", "Cliente", ReportFieldType.Text),
        new ReportField("c2", "Mes", ReportFieldType.Text),
        new ReportField("tercero", "Tercero", ReportFieldType.Text)
    });

    private static readonly Dictionary<string, string> CtxTercero = new() { [AnalisisContexto.Tercero] = "abc-123" };

    [Fact]
    public void Contexto_MapsTerceroPerSource()
    {
        Assert.Equal("TerceroId", AnalisisContexto.Resolver(AnalisisContexto.Tercero, Tareas, null));
        Assert.Equal("Id", AnalisisContexto.Resolver(AnalisisContexto.Tercero, Directorio, null));
        // Fuente sin mapa: un campo cuya clave es el nombre del parametro.
        Assert.Equal("tercero", AnalisisContexto.Resolver(AnalisisContexto.Tercero, Contenedor, null));
        // Override del widget: otro campo, o "" para no aplicar.
        Assert.Equal("c1", AnalisisContexto.Resolver(AnalisisContexto.Tercero, Contenedor, "{\"tercero\":\"c1\"}"));
        Assert.Null(AnalisisContexto.Resolver(AnalisisContexto.Tercero, Tareas, "{\"tercero\":\"\"}"));
        Assert.Null(AnalisisContexto.Resolver("proyecto", Directorio, null));
    }

    [Fact]
    public void Contexto_Filters_AreEquality_AndFlagIgnored()
    {
        var (filtros, ignorado) = AnalisisContexto.Filtros(CtxTercero, Tareas, null);
        Assert.False(ignorado);
        var f = Assert.Single(filtros);
        Assert.Equal(ReportFilter.Eq("TerceroId", "abc-123").FieldKey, f.FieldKey);
        Assert.Equal(ReportFilterOperator.Equals, f.Operator);
        Assert.Equal("abc-123", f.Values[0]);

        var (vacio, ignorado2) = AnalisisContexto.Filtros(new Dictionary<string, string> { ["proyecto"] = "x" }, Directorio, null);
        Assert.Empty(vacio);
        Assert.True(ignorado2);

        Assert.Empty(AnalisisContexto.Filtros(null, Tareas, null).Filtros);
        Assert.Empty(AnalisisContexto.Filtros(new Dictionary<string, string> { [AnalisisContexto.Tercero] = " " }, Tareas, null).Filtros);
    }

    private static WidgetDiseno Linea() => new()
    {
        Tipo = AnalisisTipoWidget.Linea,
        Titulo = "Tareas por mes",
        SourceKey = Tareas.Key,
        Dimension = "CreatedMonth",
        Dimension2 = "Status",
        OrdenarPorMedida = false
    };

    [Fact]
    public void SecondDimension_GoesToGroupBy_SortedByAxes()
    {
        var spec = WidgetDisenoMapper.ToSpec(Linea());
        Assert.Equal(new[] { "CreatedMonth", "Status" }, spec.GroupBy);
        Assert.Equal(new[] { "CreatedMonth", "Status" }, spec.Sort.Select(s => s.FieldKey));
        Assert.Empty(WidgetDisenoMapper.Validar(Linea(), Tareas));

        var dto = new AnalisisWidgetDto(Guid.NewGuid(), Guid.NewGuid(), AnalisisTipoWidget.Linea, "T", AnalisisJson.Serialize(spec), null, null, 0, 2, 1);
        var back = WidgetDisenoMapper.FromWidget(dto);
        Assert.Equal("Status", back.Dimension2);
        Assert.False(back.OrdenarPorMedida);

        // Barras no admite segunda agrupacion: se descarta al construir el spec.
        Assert.Single(WidgetDisenoMapper.ToSpec(Linea() with { Tipo = AnalisisTipoWidget.Barras }).GroupBy);
    }

    [Fact]
    public void SecondDimension_Validation()
    {
        Assert.Contains(WidgetDisenoMapper.Validar(Linea() with { Tipo = AnalisisTipoWidget.Heatmap, Dimension2 = null }, Tareas), e => e.Contains("columnas"));
        Assert.Contains(WidgetDisenoMapper.Validar(Linea() with { Dimension2 = "CreatedMonth" }, Tareas), e => e.Contains("distintos"));
        Assert.Contains(WidgetDisenoMapper.Validar(Linea() with { SourceKey = Contenedor.Key, Dimension = "c1", Dimension2 = "c2" }, Contenedor), e => e.Contains("una agrupacion"));
        Assert.Contains(WidgetDisenoMapper.Validar(Linea() with { Dimension2 = "TerceroId" }, Tareas), e => e.Contains("TerceroId"));
        Assert.True(WidgetDisenoMapper.OrdenPorEjePorDefecto(AnalisisTipoWidget.Area));
        Assert.False(WidgetDisenoMapper.OrdenPorEjePorDefecto(AnalisisTipoWidget.Barras));
    }

    [Fact]
    public void NewTypes_MapToTheirShapes()
    {
        var ds = new ReportDataSet(
            new[]
            {
                new ReportColumn("Mes", "Mes", ReportFieldType.Text),
                new ReportColumn("Estado", "Estado", ReportFieldType.Text),
                new ReportColumn("Count", "Conteo", ReportFieldType.Number)
            },
            new List<IReadOnlyList<object?>>
            {
                new object?[] { "2026-01", "Abierta", 2L },
                new object?[] { "2026-01", "Cerrada", 1L },
                new object?[] { "2026-02", "Abierta", 4L }
            });
        AnalisisWidgetDto W(AnalisisTipoWidget t) => new(Guid.NewGuid(), Guid.NewGuid(), t, "W", null, null, null, 0, 1, 1);

        var serie = Assert.IsType<SerieShape>(AnalisisWidgetRunner.AFormar(ds, W(AnalisisTipoWidget.Area)));
        Assert.Equal(new[] { "2026-01", "2026-02" }, serie.Ejes);
        Assert.Equal(2, serie.Series.Count);
        Assert.IsType<HeatmapShape>(AnalisisWidgetRunner.AFormar(ds, W(AnalisisTipoWidget.Heatmap)));
        Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(ds, W(AnalisisTipoWidget.Semaforo)));
        Assert.IsType<TablaShape>(AnalisisWidgetRunner.AFormar(ds, W(AnalisisTipoWidget.Tabla)));
        // 8 tipos de la Ola 4 + 7 de la Ola 7b (el reservado Echarts no se ofrece).
        Assert.Equal(15, WidgetDisenoMapper.Tipos.Count);
        Assert.DoesNotContain(WidgetDisenoMapper.Tipos, t => t.Tipo == AnalisisTipoWidget.Echarts);
    }

    [Fact]
    public void PinnedIds_AreParsedDefensively()
    {
        var a = Guid.NewGuid();
        Assert.Equal(new[] { a }, AnalisisAnfitrionService.ParseIds($"[\"{a}\",\"{a}\"]"));
        Assert.Empty(AnalisisAnfitrionService.ParseIds(null));
        Assert.Empty(AnalisisAnfitrionService.ParseIds("{roto"));
    }
}
