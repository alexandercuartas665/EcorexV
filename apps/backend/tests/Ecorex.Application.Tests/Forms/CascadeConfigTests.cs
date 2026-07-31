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
        "cantidad":     { "label":"Cantidad", "kind":"number", "width":"78px" },
        "valor":        { "label":"Valor", "kind":"money", "width":"118px" },
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

    // Config REAL de SOLDARCO (la que armo la sesion de disenio): 4 niveles, 19 productos, juegos
    // sap/tall/rep/full/corto, resolucion MIXTA de columnSet (unos definen, otros heredan del portafolio),
    // rollup como nombre destino, widths CSS. Debe parsear VALIDA contra el contrato cerrado.
    private const string SoldarcoJson = """
    {
      "version": 1,
      "levels": [
        { "key": "oportunidad", "label": "Oportunidad", "select": "single", "dependsOn": null,
          "opensTable": false, "feedsColumn": null,
          "options": [
            { "id": "t1", "label": "Producto", "desc": "venta cruzada" },
            { "id": "t2", "label": "Sustitucion", "desc": "cambio" },
            { "id": "t3", "label": "Productividad", "desc": "menos costos" },
            { "id": "t4", "label": "Servicio Tecnico", "desc": "mantenimiento" },
            { "id": "t5", "label": "Proyecto", "desc": "inversion" }
          ] },
        { "key": "portafolio", "label": "Portafolio", "select": "multi", "dependsOn": null,
          "opensTable": false,
          "options": [
            { "id": "g1", "label": "Servicio tecnico", "color": "#e0803a" },
            { "id": "g2", "label": "Equipos", "color": "#3d4ed8", "columnSet": "full" },
            { "id": "g3", "label": "Partes y Repuestos", "color": "#0f8b5f", "columnSet": "full" },
            { "id": "g4", "label": "Soldadura", "color": "#8b5cf6", "columnSet": "corto" },
            { "id": "g5", "label": "Sistemas De Automatizacion", "color": "#c0392f", "columnSet": "corto" }
          ] },
        { "key": "producto", "label": "Producto", "select": "multi", "dependsOn": "portafolio",
          "opensTable": true, "columnSetFrom": "parent",
          "options": [
            { "id": "g1_sap", "label": "SAP", "parent": "g1", "columnSet": "sap" },
            { "id": "g1_taller", "label": "Taller", "parent": "g1", "columnSet": "tall" },
            { "id": "g1_repuesto", "label": "Repuesto", "parent": "g1", "columnSet": "rep" },
            { "id": "g2_autogenos", "label": "Autogenos", "parent": "g2" },
            { "id": "g2_plasma", "label": "Plasma", "parent": "g2" },
            { "id": "g3_consumibles", "label": "Consumibles", "parent": "g3" },
            { "id": "g4_electrodo", "label": "Electrodo", "parent": "g4" },
            { "id": "g5_robot", "label": "Robot", "parent": "g5" }
          ] },
        { "key": "proceso", "label": "Proceso", "select": "multi", "dependsOn": "producto",
          "opensTable": false, "feedsColumn": "proceso",
          "options": [
            { "id": "p_laser", "label": "laser", "parent": "g3_consumibles" },
            { "id": "p_mig", "label": "mig", "parent": "g3_consumibles" }
          ] }
      ],
      "columns": {
        "nombre_pieza": { "label": "Nombre pieza", "kind": "text", "placeholder": "Pieza" },
        "equipo": { "label": "Equipo", "kind": "text", "placeholder": "Equipo" },
        "descripcion": { "label": "Descripcion", "kind": "text" },
        "cantidad": { "label": "Cantidad", "kind": "number", "width": "78px", "format": "integer" },
        "unidad": { "label": "Unidad", "kind": "text", "width": "118px" },
        "desgaste": { "label": "Desgaste", "kind": "text", "width": "118px" },
        "proceso": { "label": "Proceso", "kind": "text", "width": "138px", "suggest": ["laser", "mig"] },
        "marca": { "label": "Marca", "kind": "text", "width": "122px" },
        "comentario": { "label": "Comentario", "kind": "text" },
        "valor": { "label": "Valor", "kind": "money", "width": "118px", "format": "currency" },
        "subtotal": { "label": "Subtotal", "kind": "money", "calc": "{cantidad}*{valor}", "agg": "Sum", "rollup": "total_estimado", "format": "currency" },
        "actividad": { "label": "Actividad", "kind": "text", "width": "140px" }
      },
      "columnSets": {
        "sap": ["nombre_pieza", "cantidad", "unidad", "desgaste", "comentario", "valor", "subtotal", "actividad"],
        "tall": ["equipo", "cantidad", "unidad", "proceso", "comentario"],
        "rep": ["descripcion", "cantidad", "unidad", "marca", "comentario"],
        "full": ["descripcion", "cantidad", "unidad", "proceso", "comentario", "valor", "subtotal", "actividad"],
        "corto": ["descripcion", "cantidad", "unidad", "proceso", "comentario", "valor", "subtotal"]
      },
      "table": {
        "keyBy": ["portafolio", "columnSet"],
        "requiredOnStartedRow": ["nombre_pieza", "equipo", "descripcion", "cantidad"]
      }
    }
    """;

    [Fact]
    public void Config_real_de_SOLDARCO_es_valida()
    {
        var cfg = CascadeConfig.Parse(SoldarcoJson, out var error);

        Assert.Null(error);
        Assert.NotNull(cfg);
    }

    [Theory]
    [InlineData("g1_sap", "sap")]     // define el suyo
    [InlineData("g2_autogenos", "full")]  // hereda del portafolio g2
    [InlineData("g5_robot", "corto")]     // hereda del portafolio g5
    public void ResolveColumnSet_mezcla_propio_y_heredado(string optionId, string expected)
    {
        var cfg = CascadeConfig.Parse(SoldarcoJson, out _);

        var resolved = CascadeConfig.ResolveColumnSet(cfg!, optionId);

        Assert.Equal(expected, resolved);
    }
}
