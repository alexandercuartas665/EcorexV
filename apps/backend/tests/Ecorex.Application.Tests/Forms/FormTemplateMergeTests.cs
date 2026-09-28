using Ecorex.Application.Forms;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Merge de una plantilla de impresion con los datos de un registro: campos sueltos, bloque repetible
// de tabla (line items) y marcadores de sistema, con formato de presentacion.
public class FormTemplateMergeTests
{
    private static readonly Dictionary<string, string?> NoFieldFormat = new();
    private static readonly Dictionary<string, string?> NoGridOptions = new();
    private static readonly Dictionary<string, string?> NoCanvasOptions = new();
    private static readonly DateTimeOffset Fecha = new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

    // Data del registro: { fieldCode: { value, type } }. El grid guarda su arreglo como texto en "value".
    private const string Data = """
    {
      "cliente": { "value": "AGROMETALICAS", "type": "text" },
      "total":   { "value": "1500000", "type": "number" },
      "items":   { "value": "[{\"codigo\":\"IMP1\",\"producto\":\"IMPRESORA\",\"cantidad\":\"2\",\"precio\":\"750000\"},{\"codigo\":\"PANT2\",\"producto\":\"MONITOR\",\"cantidad\":\"1\",\"precio\":\"218000\"}]", "type": "grid" }
    }
    """;

    [Fact]
    public void Reemplaza_campos_sistema_y_formato()
    {
        var fieldFormat = new Dictionary<string, string?> { ["total"] = "currency" };
        var html = FormTemplateMerge.Render(
            "<h1>{{empresa}}</h1><p>Cliente: {{campo.cliente}} | Total: {{campo.total}} | No {{numero}} | {{fecha}}</p>",
            Data, fieldFormat, NoGridOptions, NoCanvasOptions, "SKY SYSTEM", Fecha, "COT-000007", "T00042");

        Assert.Contains("SKY SYSTEM", html);
        Assert.Contains("Cliente: AGROMETALICAS", html);
        Assert.Contains("Total: $ 1,500,000", html); // formato currency
        Assert.Contains("No COT-000007", html);
        Assert.Contains("27/07/2026", html);
    }

    [Fact]
    public void Repite_el_bloque_de_tabla_por_cada_fila()
    {
        var gridOptions = new Dictionary<string, string?>
        {
            ["items"] = """[{"id":"codigo","label":"Codigo"},{"id":"producto","label":"Producto"},{"id":"cantidad","label":"Cant"},{"id":"precio","label":"Precio","format":"currency"}]""",
        };
        var tpl = "<table>{{#tabla.items}}<tr><td>{{fila}}</td><td>{{col.codigo}}</td><td>{{col.producto}}</td><td>{{col.cantidad}}</td><td>{{col.precio}}</td></tr>{{/tabla.items}}</table>";

        var html = FormTemplateMerge.Render(tpl, Data, NoFieldFormat, gridOptions, NoCanvasOptions, "SKY", Fecha, "1", "T1");

        // Dos filas, numeradas, con la columna precio formateada como moneda.
        Assert.Contains("<td>1</td><td>IMP1</td><td>IMPRESORA</td><td>2</td><td>$ 750,000</td>", html);
        Assert.Contains("<td>2</td><td>PANT2</td><td>MONITOR</td><td>1</td><td>$ 218,000</td>", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<tr>").Count);
    }

    [Fact]
    public void Marcador_de_campo_inexistente_queda_vacio_y_no_rompe()
    {
        var html = FormTemplateMerge.Render(
            "A[{{campo.no_existe}}]B[{{#tabla.no_existe}}x{{/tabla.no_existe}}]C",
            Data, NoFieldFormat, NoGridOptions, NoCanvasOptions, "T", Fecha, "1", "T1");
        Assert.Equal("A[]B[]C", html); // ambos marcadores se colapsan a vacio, el resto intacto
    }

    [Fact]
    public void Escapa_html_de_los_valores()
    {
        var data = """{ "x": { "value": "<b>hola</b> & cia", "type": "text" } }""";
        var html = FormTemplateMerge.Render("{{campo.x}}", data, NoFieldFormat, NoGridOptions, NoCanvasOptions, "T", Fecha, "1", "T1");
        Assert.Equal("&lt;b&gt;hola&lt;/b&gt; &amp; cia", html);
    }

    [Fact]
    public void Columna_select_y_multicheck_imprimen_etiqueta_no_id()
    {
        // La celda select guarda el id de la opcion; la multicheck guarda un arreglo JSON de ids.
        // En impresion deben salir las ETIQUETAS ("Mail" y "Cotizacion, PQR"), no el id ni el JSON crudo.
        var data = """
        {
          "contactos": { "value": "[{\"medio\":\"mail\",\"gestiones\":\"[\\\"cotizacion\\\",\\\"pqr\\\"]\"}]", "type": "grid" }
        }
        """;
        var gridOptions = new Dictionary<string, string?>
        {
            ["contactos"] = """[{"id":"medio","label":"Medio","type":"select","options":[{"id":"mail","label":"Mail"},{"id":"telefono","label":"Telefono"}]},{"id":"gestiones","label":"Gestiones","type":"multicheck","options":[{"id":"cotizacion","label":"Cotizacion"},{"id":"leads","label":"Leads"},{"id":"pqr","label":"PQR"}]}]""",
        };
        var tpl = "{{#tabla.contactos}}[{{col.medio}}|{{col.gestiones}}]{{/tabla.contactos}}";

        var html = FormTemplateMerge.Render(tpl, data, NoFieldFormat, gridOptions, NoCanvasOptions, "SKY", Fecha, "1", "T1");

        Assert.Equal("[Mail|Cotizacion, PQR]", html);
    }

    [Fact]
    public void Campo_select_y_multicheck_imprimen_etiqueta_no_id()
    {
        // Campos sueltos (no grilla): un Select guarda el id; un MultiCheck guarda ["id1","id2"].
        // Con fieldChoices, {{campo.x}} imprime la etiqueta (o "A, B" en multi).
        var data = """
        {
          "perfil":  { "value": "prospecto", "type": "text" },
          "canales": { "value": "[\"mail\",\"tel\"]", "type": "text" }
        }
        """;
        var perfilOpts = """[{"id":"prospecto","label":"Prospecto"},{"id":"cliente","label":"Cliente"}]""";
        var canalesOpts = """[{"id":"mail","label":"Mail"},{"id":"tel","label":"Telefono"}]""";
        var fieldChoices = new Dictionary<string, (string? OptionsJson, bool IsMulti)>
        {
            ["perfil"] = (perfilOpts, false),
            ["canales"] = (canalesOpts, true),
        };

        var html = FormTemplateMerge.Render(
            "P:{{campo.perfil}} C:{{campo.canales}}", data, NoFieldFormat, NoGridOptions, NoCanvasOptions,
            "SKY", Fecha, "1", "T1", fieldChoices);

        Assert.Equal("P:Prospecto C:Mail, Telefono", html);
    }

    [Fact]
    public void Expone_el_numero_de_tarea_como_tarea_y_barcode()
    {
        // numero (registro) y tarea son distintos: la tarea es la Reference sin el ordinal (lo calcula el
        // caller del servicio); aqui se pasan explicitos para fijar los marcadores.
        var html = FormTemplateMerge.Render(
            "Tarea {{tarea}} | Registro {{numero}} | BC:<span>{{barcode:tarea}}</span>",
            Data, NoFieldFormat, NoGridOptions, NoCanvasOptions, "SKY", Fecha, "T00042-1", "T00042");

        Assert.Contains("Tarea T00042 ", html);       // {{tarea}} -> numero de tarea
        Assert.Contains("Registro T00042-1 ", html);  // {{numero}} -> numero de registro (con ordinal), intacto
        Assert.Contains("<svg", html);                // {{barcode:tarea}} emitio un codigo de barras SVG
    }
}
