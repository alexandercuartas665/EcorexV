using Ecorex.Application.Forms.Calc;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests del evaluador de campos calculados (ola F2, doc 01 D5): sandbox tipado con
/// aritmetica, parentesis, refs {campo}, refs al encabezado {#campo}, comparadores, funciones de
/// la allow-list (SI / REDONDEAR* / MIN / MAX) y menos unario. Contrato que NO se rompe:
/// campo vacio o no numerico = 0; expresion invalida = null (nunca excepcion).
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
    public void Texto_no_numerico_cuenta_como_cero()
    {
        Assert.Equal(7m, FormExpressionEvaluator.Evaluate("{a} + 7", V(("a", "N/A"))));
        Assert.Equal(0m, FormExpressionEvaluator.Evaluate("{a} * 5", V(("a", "sin dato"))));
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

    // ---- C2: comparadores ----

    [Theory]
    [InlineData("3 > 2", 1)]
    [InlineData("2 > 3", 0)]
    [InlineData("2 < 3", 1)]
    [InlineData("3 >= 3", 1)]
    [InlineData("2 >= 3", 0)]
    [InlineData("3 <= 3", 1)]
    [InlineData("4 <= 3", 0)]
    [InlineData("2 = 2", 1)]
    [InlineData("2 = 3", 0)]
    [InlineData("2 <> 3", 1)]
    [InlineData("2 <> 2", 0)]
    [InlineData("1 + 1 = 2", 1)]        // el comparador tiene MENOS precedencia que la aritmetica
    [InlineData("2 * 3 > 5 + 0", 1)]
    public void Comparadores_devuelven_uno_o_cero(string expr, decimal expected)
        => Assert.Equal(expected, FormExpressionEvaluator.Evaluate(expr, V()));

    [Fact]
    public void Comparador_con_texto_no_numerico_lo_trata_como_cero()
    {
        // "abc" = 0, asi que "abc" < 1 es verdadero y "abc" = 0 tambien.
        Assert.Equal(1m, FormExpressionEvaluator.Evaluate("{a} < 1", V(("a", "abc"))));
        Assert.Equal(1m, FormExpressionEvaluator.Evaluate("{a} = 0", V(("a", "abc"))));
        Assert.Equal(1m, FormExpressionEvaluator.Evaluate("{a} = {b}", V(("a", "abc"), ("b", "xyz"))));
    }

    // ---- C2: SI() ----

    [Theory]
    [InlineData("SI(1; 10; 20)", 10)]
    [InlineData("SI(0; 10; 20)", 20)]
    [InlineData("SI(3 > 2; 10; 20)", 10)]
    [InlineData("si(3 > 2; 10; 20)", 10)]                 // insensible a mayusculas
    [InlineData("SI(1; SI(0; 1; 2); 3)", 2)]              // anidada
    [InlineData("SI(1; 10; 20) + SI(0; 100; 200)", 210)]  // compone con aritmetica
    public void Si_elige_la_rama(string expr, decimal expected)
        => Assert.Equal(expected, FormExpressionEvaluator.Evaluate(expr, V()));

    [Fact]
    public void Si_es_perezosa_la_rama_no_tomada_no_revienta()
    {
        // La rama descartada divide por cero; como no se toma, la expresion sigue siendo valida.
        var values = V(("a", "10"), ("b", "0"));
        Assert.Equal(0m, FormExpressionEvaluator.Evaluate("SI({b} = 0; 0; {a} / {b})", values));
    }

    [Theory]
    [InlineData("SI(1; 2)")]           // faltan argumentos
    [InlineData("SI(1; 2; 3; 4)")]     // sobran argumentos
    [InlineData("SI 1; 2; 3)")]        // falta el parentesis de apertura
    [InlineData("SI(1; 2; 3")]         // falta el cierre
    public void Si_con_aridad_incorrecta_devuelve_null(string expr)
        => Assert.Null(FormExpressionEvaluator.Evaluate(expr, V()));

    // ---- C2: familia REDONDEAR (multiplo como PARAMETRO) ----

    [Theory]
    [InlineData("REDONDEAR.SUPERIOR(1234; 1000)", 2000)]
    [InlineData("REDONDEAR.SUPERIOR(2000; 1000)", 2000)]   // ya es multiplo: no sube
    [InlineData("REDONDEAR.SUPERIOR(1234; 100)", 1300)]
    [InlineData("REDONDEAR.SUPERIOR(1234; 50)", 1250)]
    [InlineData("REDONDEAR.SUPERIOR(1.2)", 2)]             // sin multiplo = 1
    [InlineData("REDONDEAR.INFERIOR(1234; 1000)", 1000)]
    [InlineData("REDONDEAR.INFERIOR(1999; 500)", 1500)]
    [InlineData("REDONDEAR(1234; 1000)", 1000)]
    [InlineData("REDONDEAR(1500; 1000)", 2000)]            // el punto medio sube (AwayFromZero)
    [InlineData("REDONDEAR(1.5)", 2)]
    [InlineData("REDONDEAR(2.4)", 2)]
    [InlineData("redondear.superior(1234; 1000)", 2000)]   // insensible a mayusculas
    public void Familia_redondear(string expr, decimal expected)
        => Assert.Equal(expected, FormExpressionEvaluator.Evaluate(expr, V()));

    [Theory]
    [InlineData("REDONDEAR.SUPERIOR(1234; 0)")]
    [InlineData("REDONDEAR.INFERIOR(1234; 0)")]
    [InlineData("REDONDEAR(1234; 0)")]
    public void Redondeo_con_multiplo_cero_da_cero_y_no_lanza(string expr)
    {
        // Convencion del Excel de origen: sin rejilla donde encajar, el resultado es 0. Lo que
        // NO puede pasar es una DivideByZeroException ni un null que borre la columna.
        Assert.Equal(0m, FormExpressionEvaluator.Evaluate(expr, V()));
    }

    [Fact]
    public void Redondeo_con_multiplo_negativo_usa_la_magnitud()
        => Assert.Equal(2000m, FormExpressionEvaluator.Evaluate("REDONDEAR.SUPERIOR(1234; -1000)", V()));

    [Theory]
    [InlineData("REDONDEAR.SUPERIOR()")]
    [InlineData("REDONDEAR.SUPERIOR(1; 2; 3)")]
    public void Redondeo_con_aridad_incorrecta_devuelve_null(string expr)
        => Assert.Null(FormExpressionEvaluator.Evaluate(expr, V()));

    // ---- C2: MIN / MAX ----

    [Theory]
    [InlineData("MIN(3; 1; 2)", 1)]
    [InlineData("MAX(3; 1; 2)", 3)]
    [InlineData("MIN(5)", 5)]
    [InlineData("MAX(-3; -7)", -3)]
    [InlineData("MIN(3; 1) + MAX(1; 2)", 3)]
    public void Min_y_max(string expr, decimal expected)
        => Assert.Equal(expected, FormExpressionEvaluator.Evaluate(expr, V()));

    // ---- C2: el sandbox sigue cerrado ----

    [Theory]
    [InlineData("SISTEMA(1)")]
    [InlineData("EVAL(\"1+1\")")]
    [InlineData("System.Math.Abs(1)")]
    [InlineData("ABS(-1)")]              // funcion real de Excel, pero FUERA de la allow-list
    [InlineData("{a}.ToString()")]
    public void Funcion_fuera_de_la_allow_list_devuelve_null(string expr)
        => Assert.Null(FormExpressionEvaluator.Evaluate(expr, V(("a", "1"))));

    [Fact]
    public void Anidamiento_excesivo_devuelve_null_y_no_tumba_el_proceso()
    {
        var expr = new string('(', 500) + "1" + new string(')', 500);
        Assert.Null(FormExpressionEvaluator.Evaluate(expr, V()));
    }

    // ---- C3: referencias al encabezado {#campo} ----

    [Fact]
    public void Referencia_al_encabezado_se_resuelve_del_segundo_diccionario()
    {
        var row = V(("subt_desc", "1000"));
        var header = V(("iva_pct", "19"));
        Assert.Equal(190m, FormExpressionEvaluator.Evaluate("{subt_desc} * {#iva_pct} / 100", row, header));
    }

    [Fact]
    public void Referencia_al_encabezado_inexistente_o_sin_encabezado_vale_cero()
    {
        var row = V(("subt_desc", "1000"));
        Assert.Equal(0m, FormExpressionEvaluator.Evaluate("{subt_desc} * {#noexiste} / 100", row, V()));
        // Sin diccionario de encabezado (llamada de 2 argumentos, la historica) tampoco lanza.
        Assert.Equal(0m, FormExpressionEvaluator.Evaluate("{subt_desc} * {#iva_pct} / 100", row));
    }

    [Fact]
    public void Compatibilidad_una_formula_vieja_no_cambia_de_significado()
    {
        // Mismo codigo en fila y encabezado con valores distintos: {campo} sigue siendo la FILA.
        var row = V(("iva_pct", "5"), ("base", "1000"));
        var header = V(("iva_pct", "19"));
        Assert.Equal(50m, FormExpressionEvaluator.Evaluate("{base} * {iva_pct} / 100", row, header));
        Assert.Equal(190m, FormExpressionEvaluator.Evaluate("{base} * {#iva_pct} / 100", row, header));
    }

    [Fact]
    public void ReferencedFields_separa_fila_de_encabezado()
    {
        const string expr = "SI({exento_iva} = 1; 0; {subt_desc} * {#iva_pct} / 100)";
        Assert.Equal(new[] { "exento_iva", "subt_desc" }, FormExpressionEvaluator.ReferencedFields(expr));
        Assert.Equal(new[] { "iva_pct" }, FormExpressionEvaluator.ReferencedHeaderFields(expr));
    }

    [Theory]
    [InlineData("{} + 1")]
    [InlineData("{#} + 1")]
    [InlineData("{a + 1")]
    public void Referencia_malformada_devuelve_null(string expr)
        => Assert.Null(FormExpressionEvaluator.Evaluate(expr, V(("a", "1"))));

    // ---- Criterio de aceptacion: las tres formulas del SIMULADOR DE COTIZACIONES ----

    [Fact]
    public void Formulas_del_simulador_de_cotizaciones()
    {
        var row = V(
            ("precio_base", "1200000"), ("mano_obra", "150000"),
            ("cantidad", "5"), ("stock", "3"),
            ("exento_iva", "0"), ("subt_desc", "1000000"));
        var header = V(("iva_pct", "19"));

        Assert.Equal(1350000m, FormExpressionEvaluator.Evaluate(
            "REDONDEAR.SUPERIOR({precio_base}+{mano_obra}; 1000)", row, header));
        Assert.Equal(1m, FormExpressionEvaluator.Evaluate(
            "SI({cantidad} > {stock}; 1; 0)", row, header));
        Assert.Equal(190000m, FormExpressionEvaluator.Evaluate(
            "SI({exento_iva}=1; 0; {subt_desc}*{#iva_pct}/100)", row, header));

        // Exento: la rama del IVA no aplica.
        var exento = new Dictionary<string, string?>(row, StringComparer.Ordinal) { ["exento_iva"] = "1" };
        Assert.Equal(0m, FormExpressionEvaluator.Evaluate(
            "SI({exento_iva}=1; 0; {subt_desc}*{#iva_pct}/100)", exento, header));
    }
}
