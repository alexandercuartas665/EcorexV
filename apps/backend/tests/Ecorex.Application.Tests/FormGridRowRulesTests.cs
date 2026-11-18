using Ecorex.Application.Forms;
using Ecorex.Application.Forms.Calc;
using Xunit;

namespace Ecorex.Application.Tests;

/// <summary>
/// Reglas por fila de una grilla (ADR-0181), con la grilla de la oferta de un portal de ingenio: cantidad cotizada
/// no mayor que la solicitada, precio obligatorio salvo "No cotiza", motivo obligatorio cuando no se cotiza.
/// </summary>
public class FormGridRowRulesTests
{
    private const string Columnas = """
        [{"id":"codigo","label":"Codigo"},
         {"id":"cnt_sol","label":"Cnt Sol"},
         {"id":"cnt_cot","label":"Cnt Cot","maxCol":"cnt_sol"},
         {"id":"vlr_uni","label":"Vlr. Uni","requiredUnless":"no_cotiza"},
         {"id":"no_cotiza","label":"No cotiza","type":"select","options":[{"id":"Si","label":"Si"}]},
         {"id":"motivo","label":"Motivo","type":"select","requiredWhen":"no_cotiza",
          "options":[{"id":"AGOTADO","label":"AGOTADO"},{"id":"NO LO VENDE","label":"NO LO VENDE"},{"id":"NO LO DISTRIBUYE","label":"NO LO DISTRIBUYE"}]}]
        """;

    private static Dictionary<string, string?> Fila(string cntSol, string? cntCot, string? vlr, string? noCot = null, string? motivo = null)
        => new() { ["codigo"] = "1428020", ["cnt_sol"] = cntSol, ["cnt_cot"] = cntCot, ["vlr_uni"] = vlr, ["no_cotiza"] = noCot, ["motivo"] = motivo };

    [Fact]
    public void Parse_solo_toma_columnas_con_reglas()
    {
        var rules = FormGridRowRules.Parse(Columnas);
        Assert.Equal(3, rules.Count);
        Assert.Contains(rules, r => r.ColumnId == "cnt_cot" && r.MaxCol == "cnt_sol");
        Assert.Contains(rules, r => r.ColumnId == "vlr_uni" && r.RequiredUnless == "no_cotiza");
        Assert.Contains(rules, r => r.ColumnId == "motivo" && r.RequiredWhen == "no_cotiza");
    }

    [Fact]
    public void Oferta_completa_pasa()
    {
        var rows = new[] { Fila("27", "27", "185000"), Fila("5", null, null, "Si", "AGOTADO") };
        Assert.Null(FormGridRowRules.Validate(rows, Columnas));
    }

    [Fact]
    public void Cantidad_cotizada_mayor_que_la_solicitada_falla()
    {
        var err = FormGridRowRules.Validate(new[] { Fila("27", "30", "185000") }, Columnas);
        Assert.NotNull(err);
        Assert.Contains("Cnt Cot", err);
        Assert.Contains("Cnt Sol", err);
        Assert.Contains("fila 1", err);
    }

    [Fact]
    public void Precio_obligatorio_salvo_no_cotiza()
    {
        var err = FormGridRowRules.Validate(new[] { Fila("27", "27", null) }, Columnas);
        Assert.NotNull(err);
        Assert.Contains("Vlr. Uni", err);
        Assert.Contains("No cotiza", err);
    }

    [Fact]
    public void No_cotiza_exige_motivo()
    {
        var err = FormGridRowRules.Validate(new[] { Fila("27", "27", "1"), Fila("5", null, null, "Si") }, Columnas);
        Assert.NotNull(err);
        Assert.Contains("Motivo", err);
        Assert.Contains("fila 2", err);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("No", false)]
    [InlineData("0", false)]
    [InlineData("[]", false)]
    [InlineData("Si", true)]
    [InlineData("true", true)]
    [InlineData("[\"x\"]", true)]
    public void IsMarked(string? valor, bool esperado) => Assert.Equal(esperado, FormGridRowRules.IsMarked(valor));

    [Fact]
    public void ValidateGridValue_aplica_reglas_por_fila_ademas_de_las_de_columna()
    {
        const string json = """[{"codigo":"1428020","cnt_sol":"27","cnt_cot":"40","vlr_uni":"10"}]""";
        Assert.Contains("no puede superar", FormFieldValidator.ValidateGridValue(json, Columnas));
        Assert.Null(FormFieldValidator.ValidateGridValue(null, Columnas));
        Assert.Null(FormFieldValidator.ValidateGridValue("""[{"codigo":"1","cnt_sol":"2","cnt_cot":"2","vlr_uni":"10"}]""", Columnas));
    }

    [Fact]
    public void Sin_reglas_no_valida_nada()
    {
        const string cols = """[{"id":"a","label":"A"},{"id":"b","label":"B"}]""";
        Assert.Null(FormGridRowRules.Validate(new[] { new Dictionary<string, string?> { ["a"] = "9", ["b"] = "1" } }, cols));
    }
}
