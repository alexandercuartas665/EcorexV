using Ecorex.Application.Formulas;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del motor de formulas de los campos calculados (ADR-0029). Logica pura: no necesitan
/// Docker ni base de datos.
/// </summary>
public class FormulaEngineTests
{
    private static decimal? Eval(string formula, Dictionary<string, string?>? values = null)
    {
        var parsed = FormulaEngine.Parse(formula);
        Assert.True(parsed.IsOk, parsed.Error);
        return FormulaEngine.Evaluate(parsed.Node!, k => FormulaEngine.ToNumber(values?.GetValueOrDefault(k)));
    }

    // ---- Aritmetica y precedencia ----

    [Theory]
    [InlineData("2 + 3", 5)]
    [InlineData("10 - 4", 6)]
    [InlineData("6 * 7", 42)]
    [InlineData("20 / 4", 5)]
    [InlineData("2 + 3 * 4", 14)]        // la multiplicacion va antes
    [InlineData("(2 + 3) * 4", 20)]      // el parentesis manda
    [InlineData("-5 + 8", 3)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("2 * -3", -6)]
    [InlineData("100 - 10 - 5", 85)]     // asociatividad por la izquierda
    [InlineData("100 / 10 / 2", 5)]
    public void Aritmetica_respeta_precedencia_y_asociatividad(string formula, decimal expected)
        => Assert.Equal(expected, Eval(formula));

    [Fact]
    public void Decimales_no_pierden_precision_porque_todo_es_decimal()
    {
        // En double esto daria 0.30000000000000004. Aqui hay dinero de por medio.
        Assert.Equal(0.3m, Eval("0.1 + 0.2"));
    }

    // ---- Referencias a campos ----

    [Fact]
    public void Referencia_toma_el_valor_del_campo()
    {
        var values = new Dictionary<string, string?> { ["base"] = "1000", ["flete"] = "200" };
        Assert.Equal(1200m, Eval("{base} + {flete}", values));
    }

    [Fact]
    public void Campo_vacio_o_ausente_vale_cero_para_no_romper_la_ficha_mientras_se_captura()
    {
        var values = new Dictionary<string, string?> { ["base"] = "1000", ["flete"] = "" };
        Assert.Equal(1000m, Eval("{base} + {flete}", values));
        Assert.Equal(1000m, Eval("{base} + {no_existe}", values));
    }

    [Fact]
    public void Campo_con_texto_no_numerico_vale_cero()
    {
        var values = new Dictionary<string, string?> { ["base"] = "abc" };
        Assert.Equal(0m, Eval("{base}", values));
    }

    [Theory]
    [InlineData("1234.56", 1234.56)]      // invariante, como se guarda
    [InlineData("$ 1,234.56", 1234.56)]   // tecleado con moneda y miles
    [InlineData("1.234,56", 1234.56)]     // formato es-CO
    [InlineData("-500", -500)]
    public void ToNumber_entiende_lo_que_la_gente_teclea(string raw, decimal expected)
        => Assert.Equal(expected, FormulaEngine.ToNumber(raw));

    [Fact]
    public void Parse_expone_las_referencias_sin_repetir()
    {
        var parsed = FormulaEngine.Parse("{a} + {b} * {a}");
        Assert.True(parsed.IsOk);
        Assert.Equal(new[] { "a", "b" }, parsed.References.OrderBy(x => x));
    }

    // ---- Funciones ----

    [Theory]
    [InlineData("ROUND(2.555, 2)", 2.56)]
    [InlineData("ROUND(2.4, 0)", 2)]
    [InlineData("ROUND(2.5, 0)", 3)]          // .5 se aleja del cero, como espera un contador
    [InlineData("MIN(5, 2, 9)", 2)]
    [InlineData("MAX(5, 2, 9)", 9)]
    [InlineData("ABS(-7)", 7)]
    [InlineData("SUM(1, 2, 3, 4)", 10)]
    [InlineData("round(1.5, 0)", 2)]          // el nombre no distingue mayusculas
    public void Funciones(string formula, decimal expected)
        => Assert.Equal(expected, Eval(formula));

    [Fact]
    public void Funciones_anidadas_con_referencias()
    {
        var values = new Dictionary<string, string?> { ["valor_base"] = "1000", ["flete"] = "190" };
        Assert.Equal(1416.1m, Eval("ROUND(({valor_base} + {flete}) * 1.19, 2)", values));
    }

    // ---- Errores de sintaxis: el mensaje debe ayudar, no solo fallar ----

    [Theory]
    [InlineData("2 +")]
    [InlineData("(2 + 3")]
    [InlineData("{sin_cerrar")]
    [InlineData("2 $ 3")]
    [InlineData("NOEXISTE(1)")]
    [InlineData("ROUND(1)")]          // le falta un argumento
    [InlineData("ABS(1, 2)")]         // le sobra
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("   ")]
    public void Formula_invalida_no_parsea_y_explica(string formula)
    {
        var parsed = FormulaEngine.Parse(formula);
        Assert.False(parsed.IsOk);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
    }

    [Fact]
    public void El_error_dice_donde_esta_el_problema()
    {
        var parsed = FormulaEngine.Parse("2 + $");
        Assert.False(parsed.IsOk);
        Assert.Contains("posicion", parsed.Error!);
    }

    [Fact]
    public void Formula_demasiado_larga_se_rechaza_antes_de_parsear()
    {
        var parsed = FormulaEngine.Parse(new string('1', FormulaEngine.MaxLength + 1));
        Assert.False(parsed.IsOk);
    }

    // ---- Division por cero: campo vacio, no excepcion ----

    [Fact]
    public void Dividir_por_cero_da_null_en_vez_de_reventar()
    {
        var values = new Dictionary<string, string?> { ["a"] = "10", ["b"] = "0" };
        Assert.Null(Eval("{a} / {b}", values));
        Assert.Null(Eval("1 / 0"));
    }

    // ---- Formato de salida ----

    [Theory]
    [InlineData(1234.5, "1234.5")]
    [InlineData(1000, "1000")]
    [InlineData(0.5, "0.5")]
    public void Format_guarda_en_invariante_sin_ceros_de_mas(decimal value, string expected)
        => Assert.Equal(expected, FormulaEngine.Format(value));
}
