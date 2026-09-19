using Ecorex.Application.Forms;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Cierre por evento de un registro transaccional (FormBuilder Ola 6/A3). Sin JSON / invalido / sin campo =>
/// NO cierra (fail-open al borrador). Reusa la semantica de la visibilidad condicional.
/// </summary>
public class FormCloseRuleTests
{
    private static Func<string, string?> Values(params (string k, string? v)[] pairs)
    {
        var d = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) { d[k] = v; }
        return code => d.TryGetValue(code, out var val) ? val : null;
    }

    [Fact]
    public void NotEmpty_cierra_cuando_la_firma_tiene_valor()
    {
        var json = """{"field":"firma","op":"notEmpty"}""";
        Assert.False(FormCloseRule.IsMet(json, Values(("firma", null))));
        Assert.False(FormCloseRule.IsMet(json, Values(("firma", "  "))));
        Assert.True(FormCloseRule.IsMet(json, Values(("firma", "data:image/png;base64,AAAA"))));
    }

    [Fact]
    public void Equals_cierra_con_el_valor_esperado()
    {
        var json = """{"field":"estado","op":"equals","value":"cerrado"}""";
        Assert.True(FormCloseRule.IsMet(json, Values(("estado", "cerrado"))));
        Assert.False(FormCloseRule.IsMet(json, Values(("estado", "abierto"))));
    }

    [Fact]
    public void Sin_json_o_invalido_o_sin_campo_no_cierra()
    {
        var vals = Values(("x", "y"));
        Assert.False(FormCloseRule.IsMet(null, vals));
        Assert.False(FormCloseRule.IsMet("", vals));
        Assert.False(FormCloseRule.IsMet("   ", vals));
        Assert.False(FormCloseRule.IsMet("no es json", vals));
        Assert.False(FormCloseRule.IsMet("""{"op":"notEmpty"}""", vals));   // sin field
        Assert.False(FormCloseRule.IsMet("[1,2,3]", vals));
    }
}
