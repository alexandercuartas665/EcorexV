using Ecorex.Application.Forms.Calc;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// PRUEBA DORADA de la matematica de una cotizacion: dado el options_json que un agente CORRECTO produce para
// la tabla (columna total = cantidad*precio con agg=Sum + rollup=subtotal), el motor debe calcular el total por
// fila y volcar la SUMA al campo subtotal. Es la regresion exacta que rompio el trim del arnes; con esto se
// detecta sola en CI sin manejar el navegador.
public class FormGridGoldenTests
{
    // Columnas tal cual las arma el asistente: producto, cantidad, precio_unitario, total_item (calc+Sum+rollup).
    private const string ItemsColumns = """
    [
      {"id":"producto","label":"Producto","type":"text"},
      {"id":"cantidad","label":"Cantidad","type":"number","format":"integer"},
      {"id":"precio_unitario","label":"Precio","type":"number","format":"currency"},
      {"id":"total_item","label":"Total","type":"calc","calc":"{cantidad}*{precio_unitario}","agg":"Sum","rollup":"subtotal"}
    ]
    """;

    private static Dictionary<string, string?> Row(string prod, string cant, string precio)
        => new() { ["producto"] = prod, ["cantidad"] = cant, ["precio_unitario"] = precio };

    [Fact]
    public void Total_por_fila_y_rollup_a_subtotal()
    {
        var cols = FormGridCalculator.ParseColumns(ItemsColumns);
        var rows = new List<Dictionary<string, string?>>
        {
            Row("Cemento", "10", "32000"),   // 320000
            Row("Varilla", "2", "28500"),    // 57000
            Row("Arena",   "3", "65000"),    // 195000
        };

        var (computed, rollups) = FormGridCalculator.Recompute(rows, cols);

        // Total por fila.
        Assert.Equal("320000", computed[0]["total_item"]);
        Assert.Equal("57000", computed[1]["total_item"]);
        Assert.Equal("195000", computed[2]["total_item"]);

        // Rollup: subtotal = 320000 + 57000 + 195000 = 572000.
        Assert.True(rollups.ContainsKey("subtotal"), "el rollup debe llenar 'subtotal'");
        Assert.Equal(572000m, decimal.Parse(rollups["subtotal"]!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Tabla_vacia_da_subtotal_cero_o_vacio()
    {
        var cols = FormGridCalculator.ParseColumns(ItemsColumns);
        var (_, rollups) = FormGridCalculator.Recompute(new List<Dictionary<string, string?>>(), cols);
        var sub = rollups.TryGetValue("subtotal", out var v) ? v : "0";
        Assert.True(string.IsNullOrEmpty(sub) || decimal.Parse(sub!, System.Globalization.CultureInfo.InvariantCulture) == 0m);
    }
}
