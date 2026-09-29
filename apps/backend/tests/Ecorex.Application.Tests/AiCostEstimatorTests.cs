using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests;

// PRUEBAS DORADAS del estimador de costo POR MODELO: que Gemini flash no se cobre como pro, que gpt-6-luna y
// gpt-4o-mini usen su tarifa barata, que Claude use el catalogo, y que un modelo desconocido caiga a la tarifa
// del proveedor. Con 1M tokens de entrada y 1M de salida, el costo = (In + Out) por millon.
public class AiCostEstimatorTests
{
    private const int M = 1_000_000;

    [Fact]
    public void Gemini_flash_es_mas_barato_que_pro()
    {
        var flash = AiCostEstimator.Estimate(AiProvider.Gemini, "gemini-2.5-flash", M, M); // 0.30 + 2.50
        var pro = AiCostEstimator.Estimate(AiProvider.Gemini, "gemini-2.5-pro", M, M);     // 1.25 + 10
        Assert.Equal(2.80m, flash);
        Assert.Equal(11.25m, pro);
        Assert.True(flash < pro);
    }

    [Fact]
    public void Gemini_2_0_flash_es_el_mas_barato()
        => Assert.Equal(0.50m, AiCostEstimator.Estimate(AiProvider.Gemini, "gemini-2.0-flash", M, M)); // 0.10+0.40

    [Fact]
    public void Gpt6_luna_y_4o_mini_usan_su_tarifa()
    {
        Assert.Equal(0.60m, AiCostEstimator.Estimate(AiProvider.ChatGpt, "gpt-6-luna", M, M));    // 0.10+0.50
        Assert.Equal(0.75m, AiCostEstimator.Estimate(AiProvider.ChatGpt, "gpt-4o-mini", M, M));   // 0.15+0.60
    }

    [Fact]
    public void Claude_haiku_usa_el_catalogo()
        => Assert.Equal(6m, AiCostEstimator.Estimate(AiProvider.Claude, "claude-haiku-4-5", M, M)); // 1+5

    [Fact]
    public void Modelo_desconocido_cae_a_la_tarifa_del_proveedor()
    {
        // Sin modelo -> tarifa de gama alta del proveedor (Gemini 1.25/10).
        Assert.Equal(11.25m, AiCostEstimator.Estimate(AiProvider.Gemini, null, M, M));
        Assert.Equal(11.25m, AiCostEstimator.Estimate(AiProvider.Gemini, "modelo-raro-xyz", M, M));
        // La sobrecarga vieja (sin modelo) sigue dando la tarifa del proveedor.
        Assert.Equal(11.25m, AiCostEstimator.Estimate(AiProvider.Gemini, M, M));
    }
}
