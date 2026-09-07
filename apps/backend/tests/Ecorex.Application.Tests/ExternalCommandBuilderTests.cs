using Ecorex.Application.Reporting.External;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Expansion de listas MULTI-VALOR del conector externo (SSRS `... IN (@p)`). Fija que:
/// - Un escalar deja el token intacto y emite UN parametro fisico.
/// - Un multi-valor con N valores reemplaza el token @p por @p__0..@p__{N-1} y emite N parametros TIPADOS
///   (cero interpolacion: el valor viaja como valor, no como texto del comando).
/// - Un multi-valor sin valores se vuelve NULL (IN (NULL) => ninguna fila, sin romper la sintaxis).
/// - El reemplazo respeta limite de palabra: @p no pisa @p2 ni @precio.
/// Pieza pura, corre sin BD.
/// </summary>
public class ExternalCommandBuilderTests
{
    [Fact]
    public void Scalar_LeavesTokenUntouched_AndEmitsOneParameter()
    {
        var bound = new[] { new ExternalBoundParameter("id", ExternalDataParameterType.Int, 42L) };
        var (sql, flat) = ExternalCommandBuilder.ExpandInLists("SELECT * FROM t WHERE id = @id", bound);

        Assert.Equal("SELECT * FROM t WHERE id = @id", sql);
        var p = Assert.Single(flat);
        Assert.Equal("@id", p.Name);
        Assert.Equal(42L, p.Value);
    }

    [Fact]
    public void MultiValue_ExpandsTokenToNumberedPlaceholders_AndEmitsTypedParameterPerValue()
    {
        var bound = new[]
        {
            new ExternalBoundParameter("grupo", ExternalDataParameterType.Int, null, new object?[] { 1L, 2L, 3L })
        };
        var (sql, flat) = ExternalCommandBuilder.ExpandInLists("SELECT * FROM v WHERE grupo IN (@grupo)", bound);

        Assert.Equal("SELECT * FROM v WHERE grupo IN (@grupo__0, @grupo__1, @grupo__2)", sql);
        Assert.Equal(3, flat.Count);
        Assert.Equal("@grupo__0", flat[0].Name);
        Assert.Equal(1L, flat[0].Value);
        Assert.Equal("@grupo__2", flat[2].Name);
        Assert.Equal(3L, flat[2].Value);
    }

    [Fact]
    public void MultiValue_Empty_BecomesNull_AndEmitsNoParameters()
    {
        var bound = new[]
        {
            new ExternalBoundParameter("grupo", ExternalDataParameterType.Int, null, Array.Empty<object?>())
        };
        var (sql, flat) = ExternalCommandBuilder.ExpandInLists("SELECT * FROM v WHERE grupo IN (@grupo)", bound);

        Assert.Equal("SELECT * FROM v WHERE grupo IN (NULL)", sql);
        Assert.Empty(flat);
    }

    [Fact]
    public void TokenReplacement_RespectsWordBoundary_DoesNotClobberSimilarNames()
    {
        // @grupo no debe pisar @grupo2 ni @grupo_sec: solo el token exacto.
        var bound = new[]
        {
            new ExternalBoundParameter("grupo", ExternalDataParameterType.Int, null, new object?[] { 7L }),
            new ExternalBoundParameter("grupo2", ExternalDataParameterType.Int, 9L)
        };
        var (sql, _) = ExternalCommandBuilder.ExpandInLists(
            "WHERE grupo IN (@grupo) AND g2 = @grupo2", bound);

        Assert.Equal("WHERE grupo IN (@grupo__0) AND g2 = @grupo2", sql);
    }

    [Fact]
    public void MultiValue_InjectionValue_TravelsAsParameterValue_NotConcatenated()
    {
        var payload = "01'); DROP TABLE ventas; --";
        var bound = new[]
        {
            new ExternalBoundParameter("codigo", ExternalDataParameterType.String, null, new object?[] { "01", payload })
        };
        var (sql, flat) = ExternalCommandBuilder.ExpandInLists("WHERE codigo IN (@codigo)", bound);

        Assert.Equal("WHERE codigo IN (@codigo__0, @codigo__1)", sql);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(payload, flat[1].Value);
    }
}
