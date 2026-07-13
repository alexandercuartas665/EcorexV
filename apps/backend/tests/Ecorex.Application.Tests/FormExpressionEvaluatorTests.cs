using Ecorex.Application.Forms.Calc;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests del evaluador de campos calculados (ola F2, doc 01 D5): sandbox tipado con
/// aritmetica, parentesis, refs {campo}, menos unario; campo vacio = 0; expresion invalida = null.
/// </summary>
public class FormExpressionEvaluatorTests
{
    private static Dictionary<string, string?> V(params (string k, string? v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v, StringComparer.Ordinal);

    [Theory]
    [InlineData("2 + 3", 5)]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("-3 + 5", 2)]
    [InlineData("2 * -(3 + 1)", -8)]
    public void Aritmetica_basica(string expr, decimal expected)
        => Assert.Equal(expected, FormExpressionEvaluator.Evaluate(expr, V()));

    [Fact]
    public void Resuelve_referencias_de_campos()
    {
        var values = V(("cantidad", "3"), ("precio", "1500"), ("descuento", "0.1"));
        var result = FormExpressionEvaluator.Evaluate("{cantidad} * {precio} * (1 - {descuento})", values);
        Assert.Equal(4050m, result);
    }

    [Fact]
    public void Campo_vacio_o_ausente_cuenta_como_cero()
    {
        var values = V(("cantidad", "5"));
        Assert.Equal(5m, FormExpressionEvaluator.Evaluate("{cantidad} + {noexiste}", values));
        Assert.Equal(0m, FormExpressionEvaluator.Evaluate("{vacio}", V(("vacio", ""))));
    }

    [Fact]
    public void Ignora_separadores_de_miles()
        => Assert.Equal(2469m, FormExpressionEvaluator.Evaluate("{a} + {b}", V(("a", "1,234"), ("b", "1235"))));

    [Theory]
    [InlineData("2 +")]        // termina en operador
    [InlineData("(2 + 3")]      // parentesis sin cerrar
    [InlineData("2 3")]         // dos numeros pegados
    [InlineData("{a} @ {b}")]   // token no permitido
    public void Expresion_invalida_devuelve_null(string expr)
        => Assert.Null(FormExpressionEvaluator.Evaluate(expr, V(("a", "1"), ("b", "2"))));

    [Fact]
    public void Division_por_cero_devuelve_null()
        => Assert.Null(FormExpressionEvaluator.Evaluate("{a} / {b}", V(("a", "10"), ("b", "0"))));

    [Fact]
    public void ReferencedFields_extrae_los_codigos()
    {
        var refs = FormExpressionEvaluator.ReferencedFields("{cantidad} * {precio} - {cantidad}");
        Assert.Equal(new[] { "cantidad", "precio" }, refs);
    }

    [Fact]
    public void Validate_detecta_forma_invalida()
    {
        Assert.Null(FormExpressionEvaluator.Validate("{a} * 2"));
        Assert.NotNull(FormExpressionEvaluator.Validate("{a} * "));
    }
}
