using Ecorex.Application.Formulas;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del calculador de un CONJUNTO de campos calculados (ADR-0029): orden de dependencias y
/// ciclos. Es justo lo que el proyecto hermano no hace, asi que se cubre a conciencia.
/// </summary>
public class FormulaCalculatorTests
{
    private static readonly Dictionary<string, string?> Empty = new();

    [Fact]
    public void Un_calculado_simple_se_resuelve()
    {
        var fields = new[] { new CalculatedField("total", "{a} + {b}") };
        var values = new Dictionary<string, string?> { ["a"] = "100", ["b"] = "50" };

        var result = FormulaCalculator.EvaluateAll(fields, values);

        Assert.Equal("150", result["total"]);
    }

    [Fact]
    public void Un_calculado_puede_depender_de_otro_calculado()
    {
        // subtotal -> iva -> total: la cadena debe resolverse completa, sin importar el orden.
        var fields = new[]
        {
            new CalculatedField("total", "{subtotal} + {iva}"),
            new CalculatedField("iva", "{subtotal} * 0.19"),
            new CalculatedField("subtotal", "{cantidad} * {precio}"),
        };
        var values = new Dictionary<string, string?> { ["cantidad"] = "10", ["precio"] = "1000" };

        var result = FormulaCalculator.EvaluateAll(fields, values);

        Assert.Equal("10000", result["subtotal"]);
        Assert.Equal("1900", result["iva"]);
        Assert.Equal("11900", result["total"]);
    }

    [Fact]
    public void El_orden_en_que_se_declaran_no_importa()
    {
        var alReves = new[]
        {
            new CalculatedField("c", "{b} + 1"),
            new CalculatedField("b", "{a} + 1"),
            new CalculatedField("a", "1"),
        };

        var result = FormulaCalculator.EvaluateAll(alReves, Empty);

        Assert.Equal("3", result["c"]);
    }

    // ---- Ciclos ----

    [Fact]
    public void Ciclo_directo_se_detecta()
    {
        var fields = new[]
        {
            new CalculatedField("a", "{b} + 1"),
            new CalculatedField("b", "{a} + 1"),
        };

        var cycle = FormulaCalculator.FindCycle(fields);

        Assert.NotNull(cycle);
        Assert.Contains("a", cycle);
        Assert.Contains("b", cycle);
    }

    [Fact]
    public void Ciclo_indirecto_se_detecta()
    {
        var fields = new[]
        {
            new CalculatedField("a", "{b}"),
            new CalculatedField("b", "{c}"),
            new CalculatedField("c", "{a}"),
        };

        Assert.NotNull(FormulaCalculator.FindCycle(fields));
    }

    [Fact]
    public void Un_campo_que_se_referencia_a_si_mismo_es_ciclo()
    {
        var fields = new[] { new CalculatedField("a", "{a} + 1") };

        Assert.NotNull(FormulaCalculator.FindCycle(fields));
    }

    [Fact]
    public void El_mensaje_del_ciclo_nombra_el_recorrido_para_poder_arreglarlo()
    {
        var fields = new[]
        {
            new CalculatedField("total", "{iva}"),
            new CalculatedField("iva", "{total}"),
        };

        var cycle = FormulaCalculator.FindCycle(fields);

        Assert.Contains("->", cycle);
    }

    [Fact]
    public void Sin_ciclo_devuelve_null()
    {
        var fields = new[]
        {
            new CalculatedField("total", "{subtotal} * 1.19"),
            new CalculatedField("subtotal", "{a} + {b}"),
        };

        Assert.Null(FormulaCalculator.FindCycle(fields));
    }

    [Fact]
    public void Un_diamante_no_es_ciclo()
    {
        // total depende de dos ramas que vuelven al mismo origen. Es valido.
        var fields = new[]
        {
            new CalculatedField("total", "{izq} + {der}"),
            new CalculatedField("izq", "{base} * 2"),
            new CalculatedField("der", "{base} * 3"),
            new CalculatedField("base", "10"),
        };

        Assert.Null(FormulaCalculator.FindCycle(fields));
        var result = FormulaCalculator.EvaluateAll(fields, Empty);
        Assert.Equal("50", result["total"]);
    }

    [Fact]
    public void Con_ciclo_ningun_calculado_se_publica()
    {
        // Un valor que dependa del orden de recorrido enganaria mas que un campo vacio.
        var fields = new[]
        {
            new CalculatedField("a", "{b}"),
            new CalculatedField("b", "{a}"),
            new CalculatedField("sano", "1 + 1"),
        };

        var result = FormulaCalculator.EvaluateAll(fields, Empty);

        Assert.Null(result["a"]);
        Assert.Null(result["b"]);
        Assert.Null(result["sano"]);
    }

    // ---- Degradacion ----

    [Fact]
    public void Una_formula_rota_deja_su_campo_vacio_sin_tumbar_a_los_demas()
    {
        var fields = new[]
        {
            new CalculatedField("roto", "2 +"),
            new CalculatedField("sano", "{a} * 2"),
        };
        var values = new Dictionary<string, string?> { ["a"] = "21" };

        var result = FormulaCalculator.EvaluateAll(fields, values);

        Assert.Null(result["roto"]);
        Assert.Equal("42", result["sano"]);
    }

    [Fact]
    public void Division_por_cero_deja_el_campo_vacio()
    {
        var fields = new[] { new CalculatedField("ratio", "{a} / {b}") };
        var values = new Dictionary<string, string?> { ["a"] = "10", ["b"] = "0" };

        var result = FormulaCalculator.EvaluateAll(fields, values);

        Assert.Null(result["ratio"]);
    }

    [Fact]
    public void Un_calculado_roto_del_que_otro_depende_lo_deja_en_cero_no_lo_rompe()
    {
        var fields = new[]
        {
            new CalculatedField("roto", "2 +"),
            new CalculatedField("depende", "{roto} + 5"),
        };

        var result = FormulaCalculator.EvaluateAll(fields, Empty);

        Assert.Null(result["roto"]);
        Assert.Equal("5", result["depende"]);
    }

    [Fact]
    public void Sin_campos_calculados_no_hay_nada_que_hacer()
        => Assert.Empty(FormulaCalculator.EvaluateAll(Array.Empty<CalculatedField>(), Empty));
}
