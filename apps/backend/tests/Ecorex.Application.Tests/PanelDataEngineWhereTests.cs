using Ecorex.Application.Reporting.Panels;

namespace Ecorex.Application.Tests;

/// <summary>
/// Helpers PUROS del motor de panel para el Where/When fijo y la normalizacion de clave de cruce
/// (ADR-0068): TransformKey (beforeDash) y Matches (eq/ne/contains/gt/gte/lt/lte). Sin BD ni Blazor.
/// </summary>
public class PanelDataEngineWhereTests
{
    private static PanelRow Row(params (string k, object? v)[] cells)
    {
        var r = new PanelRow();
        foreach (var (k, v) in cells) { r[k] = v; }
        return r;
    }

    [Theory]
    [InlineData("T00016-1", "beforeDash", "T00016")]
    [InlineData("T00016", "beforeDash", "T00016")]
    [InlineData("T00016-2-3", "beforeDash", "T00016")]
    [InlineData("T00016-1", "", "T00016-1")]
    [InlineData("T00016-1", null, "T00016-1")]
    [InlineData("T00016-1", "desconocido", "T00016-1")]
    public void TransformKey_BeforeDash(string raw, string? transform, string expected)
    {
        Assert.Equal(expected, PanelDataEngine.TransformKey(raw, transform));
    }

    [Fact]
    public void Matches_Eq_Ne_Contains()
    {
        var row = Row(("Tablero", "GESTION COMERCIAL"));
        Assert.True(PanelDataEngine.Matches(row, "Tablero", "eq", "GESTION COMERCIAL"));
        Assert.True(PanelDataEngine.Matches(row, "Tablero", "eq", "gestion comercial")); // case-insensitive
        Assert.False(PanelDataEngine.Matches(row, "Tablero", "eq", "OTRO"));
        Assert.True(PanelDataEngine.Matches(row, "Tablero", "ne", "OTRO"));
        Assert.True(PanelDataEngine.Matches(row, "Tablero", "contains", "COMERCIAL"));
        Assert.False(PanelDataEngine.Matches(row, "Tablero", "contains", "SOPORTE"));
    }

    [Fact]
    public void Matches_NumericComparators()
    {
        var row = Row(("Monto", "1500.50"));
        Assert.True(PanelDataEngine.Matches(row, "Monto", "gt", "1000"));
        Assert.True(PanelDataEngine.Matches(row, "Monto", "gte", "1500.50"));
        Assert.False(PanelDataEngine.Matches(row, "Monto", "lt", "1000"));
        Assert.True(PanelDataEngine.Matches(row, "Monto", "lte", "1500.50"));
    }

    [Fact]
    public void Matches_MissingField_IsFalseForComparators()
    {
        var row = Row(("Otro", "x"));
        Assert.False(PanelDataEngine.Matches(row, "Monto", "gt", "10"));
        // eq contra vacio: solo si el target es vacio
        Assert.True(PanelDataEngine.Matches(row, "Monto", "eq", ""));
    }
}
