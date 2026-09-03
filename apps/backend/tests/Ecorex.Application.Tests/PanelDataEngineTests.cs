using Ecorex.Application.Reporting.Panels;

namespace Ecorex.Application.Tests;

/// <summary>
/// Motor de pivoteo del renderizador generico por spec (ADR-0066). Estas pruebas FIJAN la reproduccion
/// NUMERICA 1:1 de lo que hacen a mano los paneles OCS/Tareas/SIIGO: agregaciones, group-aggregate con
/// escala/topN, pareto con acumulado, matriz cruzada, tabla agrupada, join + derivados y formato.
/// </summary>
public class PanelDataEngineTests
{
    private static PanelRow Row(params (string Key, object? Value)[] cells)
    {
        var r = new PanelRow();
        foreach (var (k, v) in cells)
        {
            r[k] = v;
        }

        return r;
    }

    private static List<PanelRow> Facturas() => new()
    {
        Row(("Nit", "900"), ("Vendedor", "Ana"), ("Total", 10_000_000m), ("Estado", "Aceptado"), ("Fecha", new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero))),
        Row(("Nit", "900"), ("Vendedor", "Ana"), ("Total", 6_000_000m), ("Estado", "Aceptado"), ("Fecha", new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero))),
        Row(("Nit", "800"), ("Vendedor", "Beto"), ("Total", 4_000_000m), ("Estado", "Rechazado"), ("Fecha", new DateTimeOffset(2026, 2, 20, 0, 0, 0, TimeSpan.Zero))),
        Row(("Nit", "700"), ("Vendedor", "Ana"), ("Total", 2_000_000m), ("Estado", "Aceptado"), ("Fecha", new DateTimeOffset(2025, 12, 5, 0, 0, 0, TimeSpan.Zero))),
    };

    [Fact]
    public void Aggregate_Sum_Count_CountDistinct_Avg()
    {
        var rows = Facturas();
        Assert.Equal(22_000_000m, PanelDataEngine.Aggregate(rows, "sum", "Total"));
        Assert.Equal(4m, PanelDataEngine.Aggregate(rows, "count", null));
        Assert.Equal(3m, PanelDataEngine.Aggregate(rows, "countDistinct", "Nit"));
        Assert.Equal(5_500_000m, PanelDataEngine.Aggregate(rows, "avg", "Total"));
    }

    [Fact]
    public void PercentOfTotal_ByCount_BySum_AndZeroDenominator()
    {
        var rows = Facturas();
        var aceptados = rows.Where(r => PanelDataEngine.Norm(r["Estado"]) == "Aceptado").ToList();

        // Por CANTIDAD (sin Field -> sub-agg count): 3 aceptados de 4 = 75%.
        Assert.Equal(75m, PanelDataEngine.PercentOfTotal(aceptados, rows, null));

        // Por MONTO (con Field -> sub-agg sum): (10+6+2)/22 * 100 = 81.81...%.
        Assert.Equal(18_000_000m / 22_000_000m * 100m, PanelDataEngine.PercentOfTotal(aceptados, rows, "Total"));

        // Denominador vacio -> 0 (sin division por cero), con y sin Field.
        Assert.Equal(0m, PanelDataEngine.PercentOfTotal(aceptados, new List<PanelRow>(), "Total"));
        Assert.Equal(0m, PanelDataEngine.PercentOfTotal(aceptados, new List<PanelRow>(), null));
    }

    [Fact]
    public void GroupAggregate_ByVendor_SumScaledMillions_OrderedDesc()
    {
        var slices = PanelDataEngine.GroupAggregate(Facturas(), "Vendedor", "sum", "Total", scale: 1_000_000d);
        Assert.Equal(2, slices.Count);
        Assert.Equal("Ana", slices[0].Key);
        Assert.Equal(18d, slices[0].Value); // (10+6+2) millones
        Assert.Equal("Beto", slices[1].Key);
        Assert.Equal(4d, slices[1].Value);
    }

    [Fact]
    public void GroupAggregate_TopN_TakesHighestOnly()
    {
        var slices = PanelDataEngine.GroupAggregate(Facturas(), "Vendedor", "sum", "Total", topN: 1);
        Assert.Single(slices);
        Assert.Equal("Ana", slices[0].Key);
    }

    [Fact]
    public void GroupAggregate_Line_OrdersByDimensionAscending()
    {
        var rows = Facturas();
        foreach (var r in rows)
        {
            r["Mes"] = PanelDataEngine.Derive(r["Fecha"], "yyyymm");
        }

        var slices = PanelDataEngine.GroupAggregate(rows, "Mes", "count", null, order: PanelSliceOrder.DimAsc);
        Assert.Equal(new[] { "2025-12", "2026-01", "2026-02" }, slices.Select(s => s.Key).ToArray());
    }

    [Fact]
    public void Pareto_CumulativeReaches100_AndIsMonotonic()
    {
        var (cats, bars, cum) = PanelDataEngine.Pareto(Facturas(), "Nit", "sum", "Total", scale: 1_000_000d, topN: 10);

        Assert.Equal(3, cats.Count);
        Assert.Equal("900", cats[0]); // 16M es el mayor
        Assert.Equal(16d, bars[0]);
        // Acumulado creciente y termina en 100 (todas las categorias presentes).
        for (var i = 1; i < cum.Count; i++)
        {
            Assert.True(cum[i] >= cum[i - 1]);
        }

        Assert.Equal(100d, cum[^1], 3);
    }

    [Fact]
    public void Pareto_CumulativeUsesGrandTotalOverAllCategories_NotOnlyTopN()
    {
        // Con topN=1, el acumulado del unico punto es su parte del GRAN total (16/22), no 100%.
        var (_, _, cum) = PanelDataEngine.Pareto(Facturas(), "Nit", "sum", "Total", scale: 1_000_000d, topN: 1);
        Assert.Single(cum);
        Assert.Equal(16d / 22d * 100d, cum[0], 3);
    }

    [Fact]
    public void Matrix_CrossTab_CellsAndTotals()
    {
        var rows = new List<PanelRow>
        {
            Row(("SO", "Win10"), ("Bits", "64")),
            Row(("SO", "Win10"), ("Bits", "64")),
            Row(("SO", "Win10"), ("Bits", "32")),
            Row(("SO", "Win7"), ("Bits", "32")),
        };

        var m = PanelDataEngine.Matrix(rows, "SO", "Bits", "count", null);
        Assert.Equal(2d, m.Cell("Win10", "64"));
        Assert.Equal(1d, m.Cell("Win10", "32"));
        Assert.Equal(3d, m.RowTotal("Win10"));
        Assert.Equal(2d, m.ColTotal("32"));
        Assert.Equal(4d, m.Grand);
        Assert.Equal(2d, m.Max);
    }

    [Fact]
    public void TableRows_MixesDirectFieldsAndAggregates_SortsByFirstAggregate()
    {
        var cols = new List<PanelColumn>
        {
            new() { Label = "Vendedor", Field = "Vendedor" },
            new() { Label = "Facturas", Agg = "count" },
            new() { Label = "Ventas", Agg = "sum", AggField = "Total" }
        };

        var rows = PanelDataEngine.TableRows(Facturas(), "Vendedor", cols, topN: null);
        Assert.Equal(2, rows.Count);
        // Ordenado por la primera columna agregada (facturas) desc: Ana (3) antes que Beto (1).
        Assert.Equal("Ana", rows[0][0]);
        Assert.Equal(3m, rows[0][1]);
        Assert.Equal(18_000_000m, rows[0][2]);
    }

    [Fact]
    public void Derive_DateBuckets()
    {
        var d = new DateTimeOffset(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026", PanelDataEngine.Derive(d, "year"));
        Assert.Equal("2026-03", PanelDataEngine.Derive(d, "yyyymm"));
        Assert.Equal("03", PanelDataEngine.Derive(d, "month"));
        Assert.Equal("2026-03-07", PanelDataEngine.Derive(d, "date"));
        Assert.Null(PanelDataEngine.Derive("no es fecha", "year"));
    }

    [Fact]
    public void Format_MoneyMillionsPercentInt()
    {
        Assert.Equal("$1,234", PanelDataEngine.Format(1234m, "money"));
        Assert.Equal("$5 M", PanelDataEngine.Format(5_000_000m, "moneyM"));
        Assert.Equal("42%", PanelDataEngine.Format(42m, "percent"));
        Assert.Equal("1,000", PanelDataEngine.Format(1000m, "int"));
    }

    [Fact]
    public void AsDecimal_HandlesTypedAndTextValues()
    {
        Assert.Equal(5m, PanelDataEngine.AsDecimal(5));
        Assert.Equal(5m, PanelDataEngine.AsDecimal(5L));
        Assert.Equal(5.5m, PanelDataEngine.AsDecimal(5.5d));
        Assert.Equal(1234m, PanelDataEngine.AsDecimal("1234"));
        Assert.Null(PanelDataEngine.AsDecimal("x"));
    }
}
