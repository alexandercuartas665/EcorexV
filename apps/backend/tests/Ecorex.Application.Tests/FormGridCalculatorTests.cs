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

    // ---- C3: una formula de columna lee el ENCABEZADO con {#campo} ----

    private const string ColsConIva =
        """
        [{"id":"base","label":"Base"},
         {"id":"iva","label":"IVA","calc":"{base} * {#iva_pct} / 100","agg":"Sum","rollup":"total_iva"}]
        """;

    [Fact]
    public void Recompute_resuelve_referencias_al_encabezado()
    {
        var cols = FormGridCalculator.ParseColumns(ColsConIva);
        var input = new List<Dictionary<string, string?>>
        {
            new(StringComparer.Ordinal) { ["base"] = "1000" },
            new(StringComparer.Ordinal) { ["base"] = "2000" },
        };
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { ["iva_pct"] = "19" };

        var (rows, rollups) = FormGridCalculator.Recompute(input, cols, header);
        Assert.Equal("190", rows[0]["iva"]);
        Assert.Equal("380", rows[1]["iva"]);
        Assert.Equal("570", rollups["total_iva"]);
    }

    [Fact]
    public void Recompute_sin_encabezado_no_lanza_y_la_referencia_vale_cero()
    {
        var cols = FormGridCalculator.ParseColumns(ColsConIva);
        var input = new List<Dictionary<string, string?>> { new(StringComparer.Ordinal) { ["base"] = "1000" } };
        // Llamada de 2 argumentos: la firma historica sigue compilando y funcionando.
        var (rows, _) = FormGridCalculator.Recompute(input, cols);
        Assert.Equal("0", rows[0]["iva"]);
    }

    // ---- C4: agregado condicional (aggWhen) ----

    private const string ColsConAggWhen =
        """
        [{"id":"cant","label":"Cantidad"},
         {"id":"stock","label":"Stock"},
         {"id":"sin_stock","label":"Sin stock","calc":"SI({cant} > {stock}; 1; 0)"},
         {"id":"sub","label":"Subtotal","calc":"{cant} * 100","agg":"Sum","rollup":"total","aggWhen":"{sin_stock}=0"}]
        """;

    private static List<Dictionary<string, string?>> Stock(params (string cant, string stock)[] rs)
        => rs.Select(r => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["cant"] = r.cant,
            ["stock"] = r.stock,
        }).ToList();

    [Fact]
    public void ParseColumns_lee_aggWhen()
    {
        var cols = FormGridCalculator.ParseColumns(ColsConAggWhen);
        Assert.Equal("{sin_stock}=0", cols[3].AggWhen);
        Assert.Null(cols[0].AggWhen);
    }

    [Fact]
    public void Compute_excluye_del_total_las_filas_sin_stock_y_las_reporta()
    {
        var cols = FormGridCalculator.ParseColumns(ColsConAggWhen);
        // Fila 1: 5 pedidos con 3 en stock -> se marca y NO suma (como el Excel).
        var result = FormGridCalculator.Compute(Stock(("2", "10"), ("5", "3"), ("4", "9")), cols);

        Assert.Equal("0", result.Rows[0]["sin_stock"]);
        Assert.Equal("1", result.Rows[1]["sin_stock"]);
        Assert.Equal("600", result.Rollups["total"]);       // 200 + 400, sin los 500 de la fila 1
        Assert.Equal(new[] { 1 }, result.ExcludedRowIndexes);
        Assert.Equal(new[] { 1 }, result.ExcludedRowIndexesByColumn["sub"]);
        Assert.False(result.ExcludedRowIndexesByColumn.ContainsKey("cant"));
    }

    [Fact]
    public void Compute_sin_aggWhen_suma_todas_las_filas_y_no_excluye_nada()
    {
        var cols = FormGridCalculator.ParseColumns(Cols);
        var result = FormGridCalculator.Compute(Rows(("2", "1500"), ("3", "1000")), cols);
        Assert.Equal("6000", result.Rollups["total"]);
        Assert.Empty(result.ExcludedRowIndexes);
        Assert.Empty(result.ExcludedRowIndexesByColumn);
    }

    [Fact]
    public void RowIsIncluded_condicion_vacia_o_invalida_incluye_la_fila()
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal) { ["sin_stock"] = "1" };
        Assert.True(FormGridCalculator.RowIsIncluded(null, row));
        Assert.True(FormGridCalculator.RowIsIncluded("   ", row));
        // Formula rota: se falla ABIERTO (incluye), para no reducir un total en silencio.
        Assert.True(FormGridCalculator.RowIsIncluded("{sin_stock} @@", row));
        Assert.False(FormGridCalculator.RowIsIncluded("{sin_stock}=0", row));
    }

    [Fact]
    public void Aggregate_por_columna_puede_condicionarse_con_el_encabezado()
    {
        var cols = FormGridCalculator.ParseColumns(
            """[{"id":"v","label":"V","agg":"Sum","rollup":"t","aggWhen":"{v} >= {#minimo}"}]""");
        var rows = new List<Dictionary<string, string?>>
        {
            new(StringComparer.Ordinal) { ["v"] = "50" },
            new(StringComparer.Ordinal) { ["v"] = "150" },
            new(StringComparer.Ordinal) { ["v"] = "200" },
        };
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { ["minimo"] = "100" };
        Assert.Equal(350m, FormGridCalculator.Aggregate(cols[0], rows, header));
    }
}
