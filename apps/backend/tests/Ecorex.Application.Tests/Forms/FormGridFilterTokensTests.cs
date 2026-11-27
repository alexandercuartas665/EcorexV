using Ecorex.Application.Forms.Lookups;

namespace Ecorex.Application.Tests.Forms;

/// <summary>Cascada en columnas de grilla: tokens {columna} del filtro del lookup con los valores de la fila.</summary>
public class FormGridFilterTokensTests
{
    private static Dictionary<string, string?> Row(params (string K, string? V)[] cells)
        => cells.ToDictionary(c => c.K, c => c.V, StringComparer.Ordinal);

    [Fact]
    public void Sustituye_El_Token_Con_El_Valor_De_La_Fila()
    {
        var r = FormGridFilterTokens.Resolve("""{"cliente":"{proyecto_cliente}"}""", Row(("proyecto_cliente", " ACME ")));
        Assert.Equal("""{"cliente":"ACME"}""", r);
    }

    [Fact]
    public void Columna_Vacia_O_Inexistente_Queda_Vacia()
    {
        Assert.Equal("""{"cliente":""}""", FormGridFilterTokens.Resolve("""{"cliente":"{proyecto_cliente}"}""", Row(("proyecto_cliente", null))));
        Assert.Equal("""{"cliente":""}""", FormGridFilterTokens.Resolve("""{"cliente":"{no_existe}"}""", Row()));
    }

    [Fact]
    public void Texto_Mixto_Objeto_Anidado_Y_Valores_No_String()
    {
        var r = FormGridFilterTokens.Resolve(
            """{"fixed":{"tablero":"{tab}","nota":"cli-{c}-x"},"archivadas":true,"n":3}""",
            Row(("tab", "B1"), ("c", "7")));
        Assert.Equal("""{"fixed":{"tablero":"B1","nota":"cli-7-x"},"archivadas":true,"n":3}""", r);
    }

    [Fact]
    public void Sin_Tokens_Nulo_O_Invalido_Devuelve_El_Original()
    {
        Assert.Equal("""{"cliente":"ACME"}""", FormGridFilterTokens.Resolve("""{"cliente":"ACME"}""", Row()));
        Assert.Null(FormGridFilterTokens.Resolve(null, Row()));
        Assert.Equal("{no es json", FormGridFilterTokens.Resolve("{no es json", Row()));
    }
}
