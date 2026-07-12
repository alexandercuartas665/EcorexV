using Ecorex.Application.Forms.Calc;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests del calculo de tablas GridDetail (ola F2, doc 01 D5): parseo de columnas con
/// calc/agg/rollup, formula por fila, agregados de columna y roll-up al encabezado.
/// </summary>
public class FormGridCalculatorTests
{
    private const string Cols =
        """[{"id":"cant","label":"Cantidad"},{"id":"precio","label":"Precio"},{"id":"sub","label":"Subtotal","calc":"{cant} * {precio}","agg":"Sum","rollup":"total"}]""";

    private static List<Dictionary<string, string?>> Rows(params (string cant, string precio)[] rs)
        => rs.Select(r => new Dictionary<string, string?>(StringComparer.Ordinal) { ["cant"] = r.cant, ["precio"] = r.precio }).ToList();

    [Fact]
    public void ParseColumns_lee_calc_agg_rollup()
    {
        var cols = FormGridCalculator.ParseColumns(Cols);
        Assert.Equal(3, cols.Count);
        var sub = cols[2];
        Assert.Equal("sub", sub.Id);
        Assert.Equal("{cant} * {precio}", sub.Calc);
        Assert.Equal(FormAggregate.Sum, sub.Agg);
        Assert.Equal("total", sub.Rollup);
    }

    [Fact]
    public void ParseColumns_columnas_viejas_sin_calc_siguen_valiendo()
    {
        var cols = FormGridCalculator.ParseColumns("""[{"id":"a","label":"A"},{"id":"b","label":"B"}]""");
        Assert.Equal(2, cols.Count);
        Assert.Null(cols[0].Calc);
        Assert.Equal(FormAggregate.None, cols[0].Agg);
    }

    [Theory]
    [InlineData(FormAggregate.Sum, "9")]
    [InlineData(FormAggregate.Count, "3")]
    [InlineData(FormAggregate.Avg, "3")]
    [InlineData(FormAggregate.Min, "1")]
    [InlineData(FormAggregate.Max, "5")]
    public void Aggregate_calcula_por_tipo(FormAggregate agg, string expected)
    {
        var result = FormGridCalculator.Aggregate(agg, new[] { "1", "3", "5" });
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result);
    }

    [Fact]
    public void Recompute_calcula_subtotal_por_fila_y_rollup_del_total()
    {
        var cols = FormGridCalculator.ParseColumns(Cols);
        var (rows, rollups) = FormGridCalculator.Recompute(Rows(("2", "1500"), ("3", "1000")), cols);

        Assert.Equal("3000", rows[0]["sub"]);   // 2 * 1500
        Assert.Equal("3000", rows[1]["sub"]);   // 3 * 1000
        Assert.Equal("6000", rollups["total"]); // suma de subtotales -> encabezado
    }

    [Fact]
    public void Recompute_sin_columnas_calc_no_toca_filas()
    {
        var cols = FormGridCalculator.ParseColumns("""[{"id":"a","label":"A"}]""");
        var input = new List<Dictionary<string, string?>> { new(StringComparer.Ordinal) { ["a"] = "x" } };
        var (rows, rollups) = FormGridCalculator.Recompute(input, cols);
        Assert.Equal("x", rows[0]["a"]);
        Assert.Empty(rollups);
    }
}
