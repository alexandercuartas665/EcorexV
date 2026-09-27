using Ecorex.Application.Forms;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Visibilidad condicional por valor de otra pregunta {field, op, value}. Foco: el 'value' puede venir
// como numero o booleano (no solo string) y NO debe romper la evaluacion (antes tumbaba el render).
public class FormVisibilityEvaluatorTests
{
    private static Func<string, string?> Vals(params (string k, string? v)[] pairs)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) { d[k] = v; }
        return code => d.TryGetValue(code, out var v) ? v : null;
    }

    [Fact]
    public void Value_numerico_no_rompe_y_evalua_notEquals()
    {
        // "mostrar el motivo solo si el descuento es distinto de 0" -> value:0 (numero)
        var json = """{"field":"descuento","op":"notEquals","value":0}""";

        // descuento = 0  -> notEquals 0 = false -> OCULTO
        Assert.False(FormVisibilityEvaluator.IsVisible(json, Vals(("descuento", "0"))));
        // descuento = 3200 -> distinto de 0 -> VISIBLE
        Assert.True(FormVisibilityEvaluator.IsVisible(json, Vals(("descuento", "3200"))));
    }

    [Fact]
    public void Value_booleano_no_rompe()
    {
        var json = """{"field":"acepta","op":"equals","value":true}""";
        Assert.True(FormVisibilityEvaluator.IsVisible(json, Vals(("acepta", "true"))));
        Assert.False(FormVisibilityEvaluator.IsVisible(json, Vals(("acepta", "false"))));
    }

    [Fact]
    public void Ops_basicos_string()
    {
        Assert.True(FormVisibilityEvaluator.IsVisible("""{"field":"estado","op":"equals","value":"Rechazado"}""", Vals(("estado", "Rechazado"))));
        Assert.False(FormVisibilityEvaluator.IsVisible("""{"field":"estado","op":"equals","value":"Rechazado"}""", Vals(("estado", "Aprobado"))));
        Assert.True(FormVisibilityEvaluator.IsVisible("""{"field":"nota","op":"notEmpty"}""", Vals(("nota", "algo"))));
        Assert.False(FormVisibilityEvaluator.IsVisible("""{"field":"nota","op":"notEmpty"}""", Vals(("nota", ""))));
    }

    [Fact]
    public void Json_ausente_o_invalido_es_visible()
    {
        Assert.True(FormVisibilityEvaluator.IsVisible(null, Vals()));
        Assert.True(FormVisibilityEvaluator.IsVisible("   ", Vals()));
        Assert.True(FormVisibilityEvaluator.IsVisible("no-es-json", Vals()));
    }
}
