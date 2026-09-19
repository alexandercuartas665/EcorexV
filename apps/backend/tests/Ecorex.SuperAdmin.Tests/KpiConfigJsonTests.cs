using Ecorex.SuperAdmin.Components.Shared.Forms;
using Xunit;

namespace Ecorex.SuperAdmin.Tests;

/// <summary>
/// KPIs configurables de la bandeja del modulo (FormBuilder Ola 6/A1): Read/Build del kpis_json + ParseNumber.
/// </summary>
public class KpiConfigJsonTests
{
    [Fact]
    public void Build_roundtrip_conteo_y_campo()
    {
        var json = KpiConfigJson.Build(new[]
        {
            new KpiConfigJson.Kpi("Registros", KpiConfigJson.Count, null),
            new KpiConfigJson.Kpi("Total vendido", KpiConfigJson.Sum, "tot_total"),
        });
        Assert.NotNull(json);
        var read = KpiConfigJson.Read(json);
        Assert.Equal(2, read.Count);
        Assert.Equal(KpiConfigJson.Count, read[0].Metric);
        Assert.Null(read[0].Field);
        Assert.Equal("Total vendido", read[1].Label);
        Assert.Equal(KpiConfigJson.Sum, read[1].Metric);
        Assert.Equal("tot_total", read[1].Field);
    }

    [Fact]
    public void Build_null_si_no_hay_kpis()
        => Assert.Null(KpiConfigJson.Build(System.Array.Empty<KpiConfigJson.Kpi>()));

    [Fact]
    public void Read_descarta_metrica_de_campo_sin_campo()
    {
        // sum sin field no sirve -> se omite.
        var read = KpiConfigJson.Read("""[{"metric":"sum"},{"metric":"count"}]""");
        Assert.Single(read);
        Assert.Equal(KpiConfigJson.Count, read[0].Metric);
    }

    [Fact]
    public void Read_descarta_metrica_desconocida_y_json_malo()
    {
        Assert.Empty(KpiConfigJson.Read("""[{"metric":"whatever","field":"x"}]"""));
        Assert.Empty(KpiConfigJson.Read("no es json"));
        Assert.Empty(KpiConfigJson.Read(null));
    }

    [Fact]
    public void Read_usa_etiqueta_por_defecto_si_falta()
    {
        var read = KpiConfigJson.Read("""[{"metric":"month"}]""");
        Assert.Single(read);
        Assert.Equal("Este mes", read[0].Label);
    }

    [Fact]
    public void MetricNeedsField_solo_para_agregados()
    {
        Assert.True(KpiConfigJson.MetricNeedsField(KpiConfigJson.Sum));
        Assert.True(KpiConfigJson.MetricNeedsField(KpiConfigJson.Avg));
        Assert.False(KpiConfigJson.MetricNeedsField(KpiConfigJson.Count));
        Assert.False(KpiConfigJson.MetricNeedsField(KpiConfigJson.Month));
    }

    [Theory]
    [InlineData("1620000", 1620000)]
    [InlineData("$ 1,620,000", 1620000)]
    [InlineData("1,620,000.50", 1620000.50)]
    [InlineData("1620000,50", 1620000.50)]
    [InlineData("-42", -42)]
    [InlineData("145677.33146400", 145677.331464)]
    [InlineData("968526.7200", 968526.72)]
    [InlineData("0.0000", 0)]
    public void ParseNumber_tolera_moneda_y_miles(string raw, double expected)
        => Assert.Equal(expected, KpiConfigJson.ParseNumber(raw)!.Value, 2);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void ParseNumber_null_si_no_hay_numero(string? raw)
        => Assert.Null(KpiConfigJson.ParseNumber(raw));
}
