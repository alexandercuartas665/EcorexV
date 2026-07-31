using Ecorex.Application.Forms.Cascade;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Contrato del esquema del configurador en cascada (config-driven): parseo + validacion. El caso valido
// es una taxonomia tipo SOLDARCO reducida; los invalidos cubren las reglas duras del contrato.
public class CascadeConfigTests
{
    // Oportunidad(single) -> Portafolio(multi) -> Producto(multi, abre tabla) -> Proceso(multi, alimenta columna).
    private const string ValidJson = """
    {
      "version": 1,
      "levels": [
        { "key":"oportunidad", "label":"Oportunidad", "select":"single", "opensTable":false,
          "options":[ {"id":"1","label":"Producto"} ] },
        { "key":"portafolio", "label":"Portafolio", "select":"multi",
          "options":[ {"id":"g1","label":"Servicio tecnico","color":"#e0803a"} ] },
        { "key":"producto", "label":"Producto", "select":"multi", "dependsOn":"portafolio",
          "opensTable":true, "columnSetFrom":"self",
          "options":[ {"id":"g1::SAP","label":"SAP","parent":"g1","columnSet":"sap"} ] },
        { "key":"proceso", "label":"Proceso", "select":"multi", "dependsOn":"producto",
          "opensTable":false, "feedsColumn":"proceso",
          "options":[ {"id":"g1::SAP::laser","label":"laser","parent":"g1::SAP"} ] }
      ],
      "columns": {
        "nombre pieza": { "label":"Nombre pieza", "kind":"text" },
        "cantidad":     { "label":"Cantidad", "kind":"number", "width":78 },
        "valor":        { "label":"Valor", "kind":"money", "width":118 },
        "subtotal":     { "label":"Subtotal", "kind":"money", "calc":"{cantidad}*{valor}", "agg":"Sum" },
        "proceso":      { "label":"Proceso", "kind":"text", "suggest":["laser","mig"] }
      },
      "columnSets": {
        "sap": ["nombre pieza","cantidad","valor","subtotal","proceso"]
      },
      "table": {
        "keyBy": ["portafolio","columnSet"],
        "requiredOnStartedRow": ["nombre pieza","cantidad"]
      }
    }
    """;

    [Fact]
    public void Parse_config_valida_devuelve_el_modelo()
    {
        var cfg = CascadeConfig.Parse(ValidJson, out var error);

        Assert.Null(error);
        Assert.NotNull(cfg);
        Assert.Equal(4, cfg!.Levels.Count);
        Assert.Contains(cfg.Levels, l => l.OpensTable);
        Assert.Equal("proceso", cfg.Levels[3].FeedsColumn);
        Assert.True(cfg.ColumnSets.ContainsKey("sap"));
    }

    [Fact]
    public void Nivel_con_options_y_source_a_la_vez_es_invalido()
    {
        var json = ValidJson.Replace(
            "\"options\":[ {\"id\":\"1\",\"label\":\"Producto\"} ] }",
            "\"options\":[ {\"id\":\"1\",\"label\":\"Producto\"} ], \"source\":{\"kind\":\"DataContainer\",\"ref\":\"x\"} }");

        var cfg = CascadeConfig.Parse(json, out var error);

        Assert.Null(cfg);
        Assert.Contains("options", error);
    }

    [Fact]
    public void Source_con_kind_options_es_invalido()
    {
        // Un nivel sin inline pero con source.kind = Options (eso seria inline) debe fallar.
        var json = ValidJson.Replace(
            "\"options\":[ {\"id\":\"g1\",\"label\":\"Servicio tecnico\",\"color\":\"#e0803a\"} ] }",
            "\"source\":{\"kind\":\"Options\",\"ref\":\"x\"} }");

        var cfg = CascadeConfig.Parse(json, out var error);

        Assert.Null(cfg);
        Assert.Contains("Options", error);
    }

    [Fact]
    public void ColumnSet_que_referencia_columna_inexistente_es_invalido()
    {
        var json = ValidJson.Replace(
            "\"sap\": [\"nombre pieza\",\"cantidad\",\"valor\",\"subtotal\",\"proceso\"]",
            "\"sap\": [\"nombre pieza\",\"columna_fantasma\"]");

        var cfg = CascadeConfig.Parse(json, out var error);

        Assert.Null(cfg);
        Assert.Contains("columna_fantasma", error);
    }

    [Fact]
    public void Formula_invalida_en_una_columna_es_rechazada_por_el_evaluador()
    {
        // Reusa FormExpressionEvaluator.Validate: un parentesis sin cerrar debe fallar.
        var json = ValidJson.Replace("\"calc\":\"{cantidad}*{valor}\"", "\"calc\":\"{cantidad}*(\"");

        var cfg = CascadeConfig.Parse(json, out var error);

        Assert.Null(cfg);
        Assert.Contains("formula", error);
    }

    [Fact]
    public void Sin_ningun_nivel_opensTable_es_invalido()
    {
        var json = ValidJson.Replace("\"opensTable\":true", "\"opensTable\":false");

        var cfg = CascadeConfig.Parse(json, out var error);

        Assert.Null(cfg);
        Assert.Contains("opensTable", error);
    }
}
