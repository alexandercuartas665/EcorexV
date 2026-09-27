using Ecorex.Application.Forms;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Escalon de estados: elige el estado MAS ALTO cuyas condiciones se cumplen (solo avanza). Foco: una
// condicion con value numerico no debe romper (mismo blindaje que la visibilidad).
public class FormStatusLadderTests
{
    private static Func<string, string?> Vals(params (string k, string? v)[] pairs)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) { d[k] = v; }
        return code => d.TryGetValue(code, out var v) ? v : null;
    }

    private const string Ladder = """
    { "field":"estado",
      "states":[
        {"label":"Borrador","when":[]},
        {"label":"Con items","when":[{"field":"num_items","op":"notEquals","value":0}]},
        {"label":"Aprobada","when":[{"field":"num_items","op":"notEquals","value":0},{"field":"aprobado","op":"equals","value":"si"}]}
      ] }
    """;

    [Fact]
    public void Value_numerico_no_rompe_y_avanza_por_escalon()
    {
        // sin items -> piso
        Assert.Equal("Borrador", FormStatusLadder.Resolve(Ladder, Vals(("estado", null), ("num_items", "0")))!.Label);
        // con items -> segundo escalon
        Assert.Equal("Con items", FormStatusLadder.Resolve(Ladder, Vals(("estado", null), ("num_items", "3")))!.Label);
        // con items + aprobado -> tope
        Assert.Equal("Aprobada", FormStatusLadder.Resolve(Ladder, Vals(("estado", null), ("num_items", "3"), ("aprobado", "si")))!.Label);
    }

    [Fact]
    public void Solo_avanza_no_baja()
    {
        // el estado actual ya es "Aprobada" aunque las condiciones ya no se cumplan -> no baja
        var r = FormStatusLadder.Resolve(Ladder, Vals(("estado", "Aprobada"), ("num_items", "0")));
        Assert.Equal("Aprobada", r!.Label);
        Assert.Equal("estado", r.TargetField);
    }
}
