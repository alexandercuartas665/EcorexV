using Ecorex.Application.Forms.Builder;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Golden del ARNES: fija el CRITERIO DE DISENO que el agente debe aplicar por defecto (densidad multi-columna
/// y eleccion del control por significado). Hallado con el Formulario 350 DIAN: el agente construia formularios
/// correctos pero PLANOS (todo a width 12, todo Number/Text) porque, sin pista visual, no densificaba ni elegia
/// Select para enumeraciones. Si alguien borra esa guia, estos asserts lo cazan.
/// </summary>
public class FormBuilderHarnessTests
{
    private static string Prompt() =>
        FormBuilderHarness.SystemPrompt("ACME", editingExisting: false, formId: "11111111-1111-1111-1111-111111111111");

    [Fact]
    public void SystemPrompt_exige_densidad_por_defecto_no_todo_a_ancho_completo()
    {
        var p = Prompt();
        Assert.Contains("DENSIDAD POR DEFECTO", p);
        Assert.Contains("NO tires todo a width 12", p);
        // La densidad aplica aunque le pasen los campos como lista de texto (sin decir el ancho).
        Assert.Contains("lista de texto", p);
    }

    [Fact]
    public void SystemPrompt_exige_elegir_el_control_por_significado()
    {
        var p = Prompt();
        Assert.Contains("ELIGE EL CONTROL POR SIGNIFICADO", p);
        // Enumeraciones -> Select/Toggle, no Number/Text libre; y placeholders utiles.
        Assert.Contains("Select", p);
        Assert.Contains("placeholder_text", p);
    }
}
