using Ecorex.Application.Forms.Cascade;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Logica del motor: opciones visibles por dependencia, armado de tablas por (rama + columnSet) con
// productos que comparten o separan tabla, y valores alimentados a una columna.
public class CascadeRuntimeTests
{
    private const string Json = """
    {
      "version": 1,
      "levels": [
        { "key":"port", "label":"Portafolio", "select":"multi",
          "options":[ {"id":"gA","label":"A","columnSet":"full"}, {"id":"gB","label":"B"} ] },
        { "key":"prod", "label":"Producto", "select":"multi", "dependsOn":"port", "opensTable":true,
          "options":[ {"id":"pa1","label":"A1","parent":"gA"}, {"id":"pa2","label":"A2","parent":"gA"},
                      {"id":"pb1","label":"B1","parent":"gB","columnSet":"x"} ] },
        { "key":"proc", "label":"Proceso", "select":"multi", "dependsOn":"prod", "feedsColumn":"proc",
          "options":[ {"id":"pr1","label":"laser","parent":"pa1"} ] }
      ],
      "columns": {
        "cantidad": {"label":"Cantidad","kind":"number"},
        "valor": {"label":"Valor","kind":"money"},
        "proc": {"label":"Proceso","kind":"text"},
        "sub": {"label":"Subtotal","kind":"money","calc":"{cantidad}*{valor}","agg":"Sum","rollup":"total"}
      },
      "columnSets": { "full":["cantidad","proc","valor","sub"], "x":["cantidad","proc"] },
      "table": { "keyBy":["port","columnSet"] }
    }
    """;

    private static CascadeConfig Cfg() => CascadeConfig.Parse(Json, out _)!;

    private static Dictionary<string, IReadOnlyList<string>> Sel(params (string k, string[] v)[] items)
        => items.ToDictionary(i => i.k, i => (IReadOnlyList<string>)i.v);

    [Fact]
    public void VisibleOptions_filtra_por_padre_seleccionado()
    {
        var cfg = Cfg();
        var prod = cfg.Levels.First(l => l.Key == "prod");

        var visible = CascadeRuntime.VisibleOptions(prod, Sel(("port", new[] { "gA" })));

        Assert.Equal(new[] { "pa1", "pa2" }, visible.Select(o => o.Id));
    }

    [Fact]
    public void Productos_del_mismo_portafolio_y_juego_comparten_UNA_tabla()
    {
        var cfg = Cfg();
        var tables = CascadeRuntime.DesiredTables(cfg, Sel(("port", new[] { "gA" }), ("prod", new[] { "pa1", "pa2" })));

        Assert.Single(tables);
        Assert.Equal("gA#full", tables[0].Key);
        Assert.Equal(new[] { "pa1", "pa2" }, tables[0].ProductOptionIds);
        Assert.Equal("full", tables[0].ColumnSet);
    }

    [Fact]
    public void Portafolios_distintos_o_juegos_distintos_SEPARAN_tablas()
    {
        var cfg = Cfg();
        var tables = CascadeRuntime.DesiredTables(cfg,
            Sel(("port", new[] { "gA", "gB" }), ("prod", new[] { "pa1", "pb1" })));

        Assert.Equal(2, tables.Count);
        Assert.Contains(tables, t => t.Key == "gA#full");
        Assert.Contains(tables, t => t.Key == "gB#x");
    }

    [Fact]
    public void Proceso_alimenta_la_columna_del_producto_padre()
    {
        var cfg = Cfg();
        var tables = CascadeRuntime.DesiredTables(cfg,
            Sel(("port", new[] { "gA" }), ("prod", new[] { "pa1" }), ("proc", new[] { "pr1" })));

        var t = Assert.Single(tables);
        Assert.True(t.Feeds.ContainsKey("proc"));
        Assert.Equal(new[] { "laser" }, t.Feeds["proc"]);
    }
}
