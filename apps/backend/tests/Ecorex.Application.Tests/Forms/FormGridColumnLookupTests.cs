using Ecorex.Application.Forms.Lookups;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Contrato del lookup por COLUMNA de GridDetail (C1) y del default por columna (C5): parseo del
// options_json, clave que se guarda, y el autollenado por fila (campoFuente -> columnaDestino).
public class FormGridColumnLookupTests
{
    // JSON de una columna 'codigo' = lookup a Items, como lo produce el disenador.
    private const string SimuladorJson = """
    [
      { "id":"codigo", "label":"Codigo", "type":"text",
        "lookup": { "source":"Item", "valueField":"sku", "displayField":"name",
          "autofill": { "name":"producto", "description":"detalle", "costo_sin_iva":"costo",
                        "proveedor":"proveedor", "stock":"stock", "exento_iva":"exento_iva" } } },
      { "id":"producto", "label":"Producto", "type":"text" },
      { "id":"cantidad", "label":"Cantidad", "type":"text", "default": 1 },
      { "id":"total", "label":"Total", "type":"text", "calc":"{cantidad}*2" }
    ]
    """;

    [Fact]
    public void Parse_lee_lookup_valueField_display_y_autofill()
    {
        var extras = FormGridColumnLookupParser.Parse(SimuladorJson);

        Assert.True(extras.TryGetValue("codigo", out var codigo));
        var lk = codigo!.Lookup;
        Assert.NotNull(lk);
        Assert.Equal(FormSourceKind.Item, lk!.SourceKind);
        Assert.Equal("sku", lk.ValueField);
        Assert.Equal("name", lk.DisplayField);
        Assert.Equal("costo", lk.Autofill["costo_sin_iva"]);
        Assert.Equal("exento_iva", lk.Autofill["exento_iva"]);
        Assert.Equal(6, lk.Autofill.Count);
    }

    [Fact]
    public void Fields_incluye_display_clave_y_origenes_del_autofill()
    {
        var lk = FormGridColumnLookupParser.Parse(SimuladorJson)["codigo"].Lookup!;
        var fields = lk.Fields();
        // display (name), clave (sku) y las fuentes del autollenado, sin duplicar 'name'.
        Assert.Contains("sku", fields);
        Assert.Contains("name", fields);
        Assert.Contains("costo_sin_iva", fields);
        Assert.Contains("proveedor", fields);
        Assert.Equal(fields.Count, new HashSet<string>(fields, System.StringComparer.OrdinalIgnoreCase).Count);
    }

    [Fact]
    public void KeyOf_guarda_el_valueField_o_cae_al_id()
    {
        var lk = FormGridColumnLookupParser.Parse(SimuladorJson)["codigo"].Lookup!;

        var conSku = new FormLookupItem("item-guid", "IMPRESORA", new Dictionary<string, string?> { ["sku"] = "IMP1" });
        Assert.Equal("IMP1", lk.KeyOf(conSku)); // guarda la clave legible

        var sinSku = new FormLookupItem("item-guid", "SIN SKU", new Dictionary<string, string?> { ["sku"] = "" });
        Assert.Equal("item-guid", lk.KeyOf(sinSku)); // sin clave, cae al id
    }

    [Fact]
    public void Autofill_por_fila_copia_los_campos_a_las_columnas_destino()
    {
        var lk = FormGridColumnLookupParser.Parse(SimuladorJson)["codigo"].Lookup!;
        var item = new FormLookupItem("item-guid", "IMPRESORA HP", new Dictionary<string, string?>
        {
            ["name"] = "IMPRESORA HP",
            ["description"] = "LaserJet M111W",
            ["costo_sin_iva"] = "436974.79",
            ["proveedor"] = "LEDACOM",
            ["stock"] = "5",
            ["exento_iva"] = "NO",
        });

        // Reproduce lo que hace el renderer al elegir: por cada (fuente -> destino) copia el valor.
        var row = new Dictionary<string, string?>();
        foreach (var (source, target) in lk.Autofill)
        {
            row[target] = item.Fields.TryGetValue(source, out var v) ? v : null;
        }

        Assert.Equal("IMPRESORA HP", row["producto"]);
        Assert.Equal("LaserJet M111W", row["detalle"]);
        Assert.Equal("436974.79", row["costo"]);
        Assert.Equal("LEDACOM", row["proveedor"]);
        Assert.Equal("5", row["stock"]);
        Assert.Equal("NO", row["exento_iva"]);
    }

    [Fact]
    public void Default_por_columna_se_parsea_como_texto()
    {
        var extras = FormGridColumnLookupParser.Parse(SimuladorJson);
        Assert.Equal("1", extras["cantidad"].Default); // C5: numero o texto, ambos como cadena
    }

    [Fact]
    public void Columna_sin_extras_no_aparece_en_el_mapa()
    {
        var extras = FormGridColumnLookupParser.Parse(SimuladorJson);
        Assert.False(extras.ContainsKey("producto")); // sin lookup/default/stockCheck
        Assert.False(extras.ContainsKey("total"));     // calc no es un extra de este parser
    }

    [Theory]
    [InlineData("\"presentation\":\"list\",", FormFieldPresentation.Dropdown)]
    [InlineData("\"presentation\":\"dropdown\",", FormFieldPresentation.Dropdown)]
    [InlineData("\"presentation\":\"modal\",", FormFieldPresentation.Modal)]
    [InlineData("\"presentation\":\"autocomplete\",", FormFieldPresentation.Autocomplete)]
    [InlineData("", FormFieldPresentation.Autocomplete)] // ausente -> default, no cambia lo configurado
    public void Presentation_se_parsea_o_cae_a_autocomplete(string presentationJson, FormFieldPresentation esperado)
    {
        var json = $$"""
        [ { "id":"c", "label":"C", "lookup": { "source":"Item", {{presentationJson}} "valueField":"sku" } } ]
        """;
        var lk = FormGridColumnLookupParser.Parse(json)["c"].Lookup!;
        Assert.Equal(esperado, lk.Presentation);
    }

    [Fact]
    public void Fuente_Contenedor_lee_source_y_sourceRef()
    {
        var json = """
        [ { "id":"cli", "label":"Cliente", "lookup": {
            "source":"DataContainer", "sourceRef":"contenedor-guid", "valueField":"codigo",
            "displayField":"nombre", "autofill": { "correo":"email" } } } ]
        """;
        var lk = FormGridColumnLookupParser.Parse(json)["cli"].Lookup!;
        Assert.Equal(FormSourceKind.DataContainer, lk.SourceKind);
        Assert.Equal("contenedor-guid", lk.SourceRef);
        Assert.Equal("email", lk.Autofill["correo"]);
    }

    [Fact]
    public void Autofill_de_marca_mapea_brand_a_la_columna()
    {
        // La marca del item viaja como campo 'brand' (FK Item.Brand.Name resuelta por ItemLookupSource).
        var json = """
        [ { "id":"codigo", "label":"Codigo", "lookup": {
            "source":"Item", "valueField":"sku", "autofill": { "brand":"marca" } } } ]
        """;
        var lk = FormGridColumnLookupParser.Parse(json)["codigo"].Lookup!;
        Assert.Equal("marca", lk.Autofill["brand"]);
        Assert.Contains("brand", lk.Fields());

        var item = new FormLookupItem("id", "IMPRESORA", new Dictionary<string, string?> { ["brand"] = "HP" });
        var row = new Dictionary<string, string?>();
        foreach (var (source, target) in lk.Autofill) { row[target] = item.Fields.GetValueOrDefault(source); }
        Assert.Equal("HP", row["marca"]);
    }
}
