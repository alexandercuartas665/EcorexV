using Ecorex.Application.Analisis;
using Ecorex.Application.Reporting;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>Indicadores de categorias agrupados por PERIODO conservan el orden cronologico (ADR-0206).</summary>
public class WidgetPeriodoOrdenTests
{
    private static ReportDataSet Semanas() => new(
        new[] { new ReportColumn("fecha", "Semana de fecha", ReportFieldType.Text), new ReportColumn("Sum_total", "Suma", ReportFieldType.Decimal) },
        new List<IReadOnlyList<object?>>
        {
            new object?[] { "2026-W02", 100.5m },
            new object?[] { "2026-W04", 250m },
            new object?[] { "2026-W06", 50m },
            new object?[] { "2026-W11", 300m }
        });

    private static AnalisisWidgetDto Widget(ReportDatePeriod? periodo) => new(Guid.Empty, Guid.Empty, AnalisisTipoWidget.Barras, "Ventas",
        AnalisisJson.Serialize(new ReportQuerySpec
        {
            SourceKey = "container:00000000-0000-0000-0000-000000000001",
            GroupBy = new[] { "fecha" },
            GroupPeriod = periodo
        }), null, null, 0, 2, 1);

    [Fact]
    public void Barras_PorPeriodo_OrdenCronologico()
    {
        var forma = Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(Semanas(), Widget(ReportDatePeriod.Week)));
        Assert.Equal(new[] { "2026-W02", "2026-W04", "2026-W06", "2026-W11" }, forma.Items.Select(i => i.Etiqueta).ToArray());
    }

    [Fact]
    public void Barras_SinPeriodo_SiguenPorValor()
    {
        var forma = Assert.IsType<CategoriasShape>(AnalisisWidgetRunner.AFormar(Semanas(), Widget(null)));
        Assert.Equal("2026-W11", forma.Items[0].Etiqueta);
    }
}
