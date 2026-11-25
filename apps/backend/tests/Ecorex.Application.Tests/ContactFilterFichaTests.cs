using Ecorex.Application.Directorio;
using Ecorex.Application.Gestor;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Filtros del Gestor de contactos (000740) por campos de FICHA: criterio "f:{FieldKey}" evaluado contra los
/// valores de FichasJson. El mismo evaluador alimenta la lista, el conteo del filtro y el motor de acciones.
/// </summary>
public class ContactFilterFichaTests
{
    private const string Fichas = """
        { "general": { "tipo_de_empresa_subsector": "Ingenio Azucarero / Planta", "pais": "Colombia" },
          "cliente": { "cupo_de_credito": 5000000, "direccion": "  Calle 1  " },
          "comercial": { "segmentos": ["A", "B"], "tabla": { "x": 1 }, "vacio": "" } }
        """;

    private static ContactFilterEvaluator.Row Row(IReadOnlyDictionary<string, string>? fichas, string ciudad = "Cali")
        => new("ACME", ciudad, "Ana", "Industria", null, TerceroPerfil.Cliente, TerceroEstado.Activo, fichas);

    private static FiltroCriterio C(string campo, string op, string valor, string conector = "Y")
        => new(campo, op, valor, conector);

    [Fact]
    public void Flatten_AplanaSeccionesYConvierteValores()
    {
        var f = TerceroFichaValues.Flatten(Fichas)!;
        Assert.Equal("Ingenio Azucarero / Planta", f["tipo_de_empresa_subsector"]);
        Assert.Equal("5000000", f["cupo_de_credito"]);
        Assert.Equal("Calle 1", f["direccion"]);
        Assert.Equal("A, B", f["segmentos"]);
        Assert.False(f.ContainsKey("tabla"));
        Assert.False(f.ContainsKey("vacio"));
        Assert.Equal("Colombia", f["PAIS"]); // sin distinguir mayusculas
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[1,2]")]
    public void Flatten_VacioOCorrupto_EsNull(string? json) => Assert.Null(TerceroFichaValues.Flatten(json));

    [Fact]
    public void CriterioDeFicha_ComparaContraElValorDeLaFicha()
    {
        var row = Row(TerceroFichaValues.Flatten(Fichas));
        Assert.True(ContactFilterEvaluator.Matches(row, C("f:tipo_de_empresa_subsector", "=", "ingenio azucarero / planta")));
        Assert.False(ContactFilterEvaluator.Matches(row, C("f:tipo_de_empresa_subsector", "=", "Cantera")));
        Assert.True(ContactFilterEvaluator.Matches(row, C("f:tipo_de_empresa_subsector", "LIKE", "azucar")));
        Assert.True(ContactFilterEvaluator.Matches(row, C("f:tipo_de_empresa_subsector", "!=", "Cantera")));
        Assert.True(ContactFilterEvaluator.Matches(row, C("f:cupo_de_credito", ">", "1000000")));
        Assert.False(ContactFilterEvaluator.Matches(row, C("f:cupo_de_credito", "<", "1000000")));
    }

    [Fact]
    public void CriterioDeFicha_SinDato_NoCoincide_Y_YaNoSeIgnora()
    {
        // Antes los campos de ficha se ignoraban (todos pasaban); ahora una fila sin el dato no coincide con "=".
        var sinFichas = Row(null);
        Assert.False(ContactFilterEvaluator.Matches(sinFichas, C("f:pais", "=", "Colombia")));
        Assert.False(ContactFilterEvaluator.Matches(sinFichas, C("f:pais", "LIKE", "Col")));
        Assert.True(ContactFilterEvaluator.Matches(sinFichas, C("f:pais", "!=", "Colombia")));
        Assert.True(ContactFilterEvaluator.Matches(sinFichas, C("f:pais", "=", "")));
    }

    [Fact]
    public void CriterioDeFicha_SeCombinaConCamposFijos()
    {
        var fichas = TerceroFichaValues.Flatten(Fichas);
        var criterios = new[] { C("ciudad", "=", "Cali"), C("f:pais", "=", "Colombia") };
        Assert.True(ContactFilterEvaluator.MatchesAll(Row(fichas), criterios));
        Assert.False(ContactFilterEvaluator.MatchesAll(Row(fichas, ciudad: "Bogota"), criterios));
        Assert.True(ContactFilterEvaluator.MatchesAll(Row(fichas, ciudad: "Bogota"),
            new[] { C("ciudad", "=", "Cali"), C("f:pais", "=", "Colombia", "O") }));
    }

    [Fact]
    public void FichasConComparadorOrdinal_SeBuscanSinMayusculas()
    {
        // Filtrables del listado usa comparador ordinal: la clave del criterio puede venir en otra caja.
        var ordinal = new Dictionary<string, string>(StringComparer.Ordinal) { ["Sector_Economico"] = "Salud" };
        Assert.True(ContactFilterEvaluator.Matches(Row(ordinal), C("f:sector_economico", "=", "Salud")));
    }

    [Theory]
    [InlineData("f:pais", "pais")]
    [InlineData("F: pais ", "pais")]
    [InlineData("ciudad", null)]
    [InlineData("f:", null)]
    [InlineData(null, null)]
    public void FieldKeyOf_ReconoceElPrefijo(string? campo, string? esperado)
        => Assert.Equal(esperado, TerceroFichaValues.FieldKeyOf(campo));

    [Fact]
    public void CamposFijos_SiguenIgual_Y_DesconocidosSeIgnoran()
    {
        var row = Row(null);
        Assert.True(ContactFilterEvaluator.Matches(row, C("vendedor", "=", "ana")));
        Assert.True(ContactFilterEvaluator.Matches(row, C("perfil", "=", "Cliente")));
        Assert.True(ContactFilterEvaluator.Matches(row, C("campo_raro", "=", "x")));
    }

    [Theory]
    [InlineData(TerceroFieldType.Select, true)]
    [InlineData(TerceroFieldType.Text, true)]
    [InlineData(TerceroFieldType.Currency, true)]
    [InlineData(TerceroFieldType.Lookup, false)]
    [InlineData(TerceroFieldType.Asesor, false)]
    [InlineData(TerceroFieldType.Table, false)]
    [InlineData(TerceroFieldType.Separator, false)]
    public void IsFilterable_ExcluyeLosQueGuardanIds(TerceroFieldType tipo, bool esperado)
        => Assert.Equal(esperado, TerceroFichaValues.IsFilterable(tipo));
}
