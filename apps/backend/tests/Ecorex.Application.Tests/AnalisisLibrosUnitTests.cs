using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Application.Reporting.Sources;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// ADR-0158 Ola 2 (puro, sin BD): serializacion de specs, forma segun el tipo de widget, lectura de
/// config, reglas de acceso del libro y que los widgets de ejemplo solo referencian campos del catalogo.
/// </summary>
public class AnalisisLibrosUnitTests
{
    private static readonly ReportDataSet PorEtapa = new(
        new[]
        {
            new ReportColumn("Stage", "Etapa", ReportFieldType.Text),
            new ReportColumn("Count", "Conteo", ReportFieldType.Number)
        },
        new List<IReadOnlyList<object?>>
        {
            new object?[] { "Nueva", 5L },
            new object?[] { "En curso", 3L },
            new object?[] { null, 2L }
        });

    private static AnalisisWidgetDto Widget(AnalisisTipoWidget tipo, string? config = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), tipo, "Mi widget", null, null, config, 0, 1, 1);

    [Fact]
    public void Spec_RoundTrips_ThroughJson()
    {
        var spec = AnalisisEjemplos.Find("tareas-abiertas")!.Widget.Spec!;
        var json = AnalisisJson.Serialize(spec);
        var back = AnalisisJson.DeserializeSpec(json)!;

        Assert.Equal(spec.SourceKey, back.SourceKey);
        Assert.Equal(spec.GroupBy, back.GroupBy);
        Assert.Equal(ReportFilterOperator.In, back.Filters[1].Operator);
        Assert.Equal(spec.Filters[1].Values, back.Filters[1].Values);
        Assert.Equal(ReportAggregateFunction.Count, back.Aggregates[0].Function);
        Assert.Contains("\"In\"", json); // enums como texto: legible y estable ante reordenamientos
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{no es json")]
    public void Spec_Invalid_ReturnsNull(string? json) => Assert.Null(AnalisisJson.DeserializeSpec(json));

    [Fact]
    public void AFormar_Kpi_SumsMeasure()
    {
        var kpi = Assert.IsType<EscalarShape>(AnalisisWidgetRunner.AFormar(PorEtapa, Widget(AnalisisTipoWidget.Kpi)));
        Assert.Equal(10m, kpi.Valor);
        Assert.Equal("Mi widget", kpi.Etiqueta);
    }

    [Theory]
    [InlineData(AnalisisTipoWidget.Barras)]
    [InlineData(AnalisisTipoWidget.Dona)]
    public void AFormar_BarrasYDona_AreCategorias_WithTopFromConfig(AnalisisTipoWidget tipo)
    {
        var cat = Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(PorEtapa, Widget(tipo, "{\"top\":1}")));
        Assert.Equal(new[] { "Nueva", "Otros" }, cat.Items.Select(i => i.Etiqueta));
        Assert.Equal(10m, cat.Total);
    }

    [Fact]
    public void AFormar_Tabla_IsDefault()
    {
        Assert.IsType<TablaShape>(AnalisisWidgetRunner.AFormar(PorEtapa, Widget(AnalisisTipoWidget.Tabla)));
    }

    [Fact]
    public void Adaptar_KpiSobreCurado_UsesTotal()
    {
        var curado = new CategoriasShape(new[] { new CategoriaItem("Vencidas", 2m, "rose"), new CategoriaItem("Al dia", 5m, "green") });
        var kpi = Assert.IsType<EscalarShape>(AnalisisWidgetRunner.Adaptar(curado, Widget(AnalisisTipoWidget.Kpi)));
        Assert.Equal(7m, kpi.Valor);
        Assert.Same(curado, AnalisisWidgetRunner.Adaptar(curado, Widget(AnalisisTipoWidget.Dona)));
    }

    [Theory]
    [InlineData(null, AnalisisWidgetRunner.TopCategoriasPorDefecto)]
    [InlineData("{\"top\":5}", 5)]
    [InlineData("{\"top\":0}", AnalisisWidgetRunner.TopCategoriasPorDefecto)]
    [InlineData("[1,2]", AnalisisWidgetRunner.TopCategoriasPorDefecto)]
    [InlineData("roto", AnalisisWidgetRunner.TopCategoriasPorDefecto)]
    public void Top_ReadsConfig(string? config, int expected) => Assert.Equal(expected, AnalisisWidgetRunner.Top(config));

    [Fact]
    public void PuedeVer_RespectsVisibilityOwnerAndAllowList()
    {
        var owner = Guid.NewGuid();
        var invitado = Guid.NewGuid();
        var extrano = new AnalisisActor(Guid.NewGuid(), false);
        var allow = $"[\"{invitado}\"]";

        Assert.True(AnalisisService.PuedeVer(owner, AnalisisVisibilidad.Privado, null, new AnalisisActor(owner, false)));
        Assert.False(AnalisisService.PuedeVer(owner, AnalisisVisibilidad.Privado, null, extrano));
        Assert.True(AnalisisService.PuedeVer(owner, AnalisisVisibilidad.Privado, null, new AnalisisActor(null, true)));
        Assert.True(AnalisisService.PuedeVer(owner, AnalisisVisibilidad.Privado, allow, new AnalisisActor(invitado, false)));
        Assert.True(AnalisisService.PuedeVer(owner, AnalisisVisibilidad.CompartidoEnTenant, null, extrano));
        Assert.False(AnalisisService.PuedeVer(owner, AnalisisVisibilidad.Privado, "corrupto", extrano));
        Assert.False(AnalisisService.PuedeVer(owner, AnalisisVisibilidad.Privado, null, new AnalisisActor(null, false)));
    }

    [Fact]
    public void Ejemplos_OnlyReferenceCatalogFields()
    {
        var sources = new IReportableSource[]
        {
            new TaskItemReportSource(null!),
            new TerceroReportSource(null!),
            new TerceroNotaReportSource(null!),
            new WorkLogReportSource(null!)
        }.ToDictionary(s => s.Describe().Key, s => s.Describe());
        var curados = new[] { SemaforoVencimientoIndicador.IndicadorKey, TiempoPorEtapaIndicador.IndicadorKey };

        Assert.Equal(AnalisisEjemplos.Catalogo.Count, AnalisisEjemplos.Catalogo.Select(e => e.Key).Distinct().Count());
        foreach (var e in AnalisisEjemplos.Catalogo)
        {
            var w = e.Widget;
            Assert.True((w.Spec is null) != (w.IndicadorKey is null), e.Key);
            if (w.IndicadorKey is not null)
            {
                Assert.Contains(w.IndicadorKey, curados);
                continue;
            }

            var d = sources[w.Spec!.SourceKey];
            foreach (var f in w.Spec.Filters)
            {
                Assert.True(d.FindField(f.FieldKey)?.CanFilter == true, $"{e.Key}: filtro {f.FieldKey}");
            }

            foreach (var g in w.Spec.GroupBy)
            {
                Assert.True(d.FindField(g)?.CanGroup == true, $"{e.Key}: grupo {g}");
            }

            foreach (var a in w.Spec.Aggregates.Where(a => a.Function != ReportAggregateFunction.Count))
            {
                Assert.True(d.FindField(a.FieldKey)?.CanAggregate == true, $"{e.Key}: agregado {a.FieldKey}");
            }
        }
    }
}
