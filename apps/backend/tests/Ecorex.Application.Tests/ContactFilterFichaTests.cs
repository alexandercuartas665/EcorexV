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

    // ---- Operadores nuevos (es uno de, no contiene, vacio, ...) y campos fijos del Directorio ----

    private static ContactFilterEvaluator.Row Completa() => new(
        "ACME SAS", "Cali", "Ana", "Agropecuario", null, TerceroPerfil.Cliente | TerceroPerfil.Proveedor,
        TerceroEstado.Activo, TerceroFichaValues.Flatten(Fichas),
        Identificacion: "900123456", Naturaleza: "Empresa", Correo: "compras@acme.co", Telefono: null, Empresa: null);

    [Theory]
    [InlineData("sector", "IN", "Industrial, agropecuario", true)]
    [InlineData("sector", "IN", "Industrial, Construccion", false)]
    [InlineData("sector", "NOT IN", "Industrial, Construccion", true)]
    [InlineData("sector", "NOT IN", "Agropecuario", false)]
    [InlineData("f:tipo_de_empresa_subsector", "IN", "Cantera,Ingenio Azucarero / Planta", true)]
    [InlineData("nombre", "NOT LIKE", "sas", false)]
    [InlineData("nombre", "NOT LIKE", "ltda", true)]
    [InlineData("telefono", "EMPTY", "", true)]
    [InlineData("telefono", "NOT EMPTY", "", false)]
    [InlineData("correo", "NOT EMPTY", "", true)]
    [InlineData("correo", "LIKE", "@acme", true)]
    [InlineData("identificacion", "=", "900123456", true)]
    [InlineData("naturaleza", "=", "persona", false)]
    [InlineData("naturaleza", "IN", "Empresa,Persona", true)]
    [InlineData("estado", "IN", "Prospecto, Activo", true)]
    [InlineData("perfil", "IN", "Empleado, Proveedor", true)]
    [InlineData("perfil", "NOT IN", "Empleado, Proveedor", false)]
    [InlineData("perfil", "EMPTY", "", false)]
    [InlineData("f:pais", "EMPTY", "", false)]
    [InlineData("f:no_existe", "EMPTY", "", true)]
    [InlineData("vendedor", "contains", "an", true)]
    public void Operadores_Y_CamposFijos(string campo, string op, string valor, bool esperado)
        => Assert.Equal(esperado, ContactFilterEvaluator.Matches(Completa(), C(campo, op, valor)));

    [Fact]
    public void MayorMenor_AceptaNumerosYFechas()
    {
        var row = Row(new Dictionary<string, string> { ["fecha_ingreso"] = "2026-03-15", ["salario"] = "2500000" });
        Assert.True(ContactFilterEvaluator.Matches(row, C("f:fecha_ingreso", ">", "2026-01-01")));
        Assert.False(ContactFilterEvaluator.Matches(row, C("f:fecha_ingreso", "<", "2026-01-01")));
        Assert.True(ContactFilterEvaluator.Matches(row, C("f:salario", "<", "3000000")));
    }

    [Theory]
    [InlineData("NIT 900123456", "900123456")]
    [InlineData("CC 1144", "1144")]
    [InlineData("Tel 3001112233", "3001112233")]
    [InlineData("correo@x.co", "correo@x.co")]
    [InlineData("Sin identificacion", null)]
    [InlineData(null, null)]
    public void IdentificacionDe_QuitaElPrefijoDelListado(string? texto, string? esperado)
        => Assert.Equal(esperado, ContactFilterEvaluator.IdentificacionDe(texto));

    [Theory]
    [InlineData("contains", "LIKE")]
    [InlineData("not  like", "NOT LIKE")]
    [InlineData("in", "IN")]
    [InlineData("notin", "NOT IN")]
    [InlineData("eq", "=")]
    [InlineData(null, "=")]
    public void NormalizeOp_AceptaAlias(string? op, string esperado)
        => Assert.Equal(esperado, ContactFilterEvaluator.NormalizeOp(op));

    [Fact]
    public void TodosLosOperadoresDeLaUi_SonConocidos()
    {
        var row = Completa();
        foreach (var op in ContactFilterEvaluator.Operadores)
        {
            // Ningun operador ofrecido cae al "default -> false" por desconocido con un valor que coincide.
            var r = ContactFilterEvaluator.Matches(row, C("nombre", op, "ACME SAS"));
            Assert.True(r || op is "!=" or "NOT LIKE" or "NOT IN" or "EMPTY" or ">" or "<", op);
        }
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
