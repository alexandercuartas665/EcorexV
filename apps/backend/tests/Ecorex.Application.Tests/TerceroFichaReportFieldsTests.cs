using System.Text.Json;
using Ecorex.Application.Reporting.Sources;

namespace Ecorex.Application.Tests;

/// <summary>Lectura tolerante de los campos de ficha del Directorio para reportes (ADR-0208).</summary>
public class TerceroFichaReportFieldsTests
{
    private static string? Leer(string json) => TerceroFichaReportFields.Scalar(JsonDocument.Parse(json).RootElement);

    [Theory]
    [InlineData("\"Metalmecanica\"", "Metalmecanica")]
    [InlineData("\"  Calle 5  \"", "Calle 5")]
    [InlineData("\"\"", null)]
    [InlineData("1500000.5", "1500000.5")]
    [InlineData("true", "true")]
    [InlineData("null", null)]
    [InlineData("{\"type\":\"select\",\"value\":\"Oro\"}", "Oro")]
    [InlineData("{\"Value\":12}", "12")]
    [InlineData("{\"type\":\"text\"}", null)]
    [InlineData("[\"A\",\"\",\"B\"]", "A, B")]
    public void Scalar_LeeEscalarEnvueltoOLista(string json, string? esperado) => Assert.Equal(esperado, Leer(json));

    [Theory]
    [InlineData("general", "tipo_de_empresa_subsector", "ficha_general_tipo_de_empresa_subsector")]
    [InlineData("Cliente", "Direccion Fiscal", "ficha_cliente_direccion_fiscal")]
    [InlineData("mod_publica", "razon-social", "ficha_mod_publica_razon_social")]
    public void KeyFor_EsEstableYSinChoqueConFijos(string seccion, string campo, string esperado)
        => Assert.Equal(esperado, TerceroFichaReportFields.KeyFor(seccion, campo));

    [Theory]
    [InlineData("tipo_de_empresa", "Tipo de empresa")]
    [InlineData("direccion", "Direccion")]
    [InlineData("sitio-web", "Sitio web")]
    public void Legible_DaEtiquetaParaClavesSinDefinicion(string clave, string esperado)
        => Assert.Equal(esperado, TerceroFichaReportFields.Legible(clave));
}
