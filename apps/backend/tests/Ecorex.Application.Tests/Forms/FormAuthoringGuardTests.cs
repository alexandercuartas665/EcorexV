using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// PRUEBA DORADA del blindaje HeaderGridCalcError: un campo (encabezado) NO puede sumar una columna de grilla con
// un calc {#...}; debe usar rollup. El agente reincidia en subtotal.calc={#items.total_item}; el guard lo corta.
public class FormAuthoringGuardTests
{
    private static SaveFormQuestionRequest Field(string code, FormControlType type, string? calc)
        => new(null, code, code, type, CalcExpression: calc);

    [Fact]
    public void Rechaza_calc_de_campo_que_referencia_columna_de_grilla()
    {
        var err = FormAuthoringToolset.HeaderGridCalcError(Field("subtotal", FormControlType.Number, "{#items.total_item}"));
        Assert.NotNull(err);
        Assert.Contains("rollup", err!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Acepta_un_calc_normal_de_campo()
    {
        Assert.Null(FormAuthoringToolset.HeaderGridCalcError(Field("iva", FormControlType.Number, "{subtotal}*0.19")));
    }

    [Fact]
    public void Acepta_campo_sin_calc()
    {
        Assert.Null(FormAuthoringToolset.HeaderGridCalcError(Field("subtotal", FormControlType.Number, null)));
    }

    [Fact]
    public void No_aplica_a_GridDetail_su_calc_va_en_options()
    {
        // El calc de una grilla va dentro de options_json (por columna), no en calc_expression -> el guard lo ignora.
        Assert.Null(FormAuthoringToolset.HeaderGridCalcError(Field("items", FormControlType.GridDetail, "{#x}")));
    }
}
