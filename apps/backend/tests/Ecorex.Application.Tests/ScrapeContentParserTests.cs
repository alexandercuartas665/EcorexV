using System.Text.Json;
using Ecorex.Application.Scraping;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests del parser puro de contenido (ADR-0025): JSON (array raiz, propiedad array,
/// objeto suelto, invalido) y HTML por selector CSS via AngleSharp (selector valido,
/// invalido tipado, sin coincidencias), mas el recorte del ResultJson persistible.
/// </summary>
public class ScrapeContentParserTests
{
    // ---- JSON ----

    [Fact]
    public void Json_ArrayRaiz_CuentaYTabula()
    {
        var result = ScrapeContentParser.ParseJson(
            """[{"sku":"A1","precio":100},{"sku":"B2","precio":200,"stock":5}]""", 20);

        Assert.True(result.Ok);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(["sku", "precio", "stock"], result.Columns);
        Assert.Equal(["A1", "100", null], result.Rows[0]);
        Assert.Equal(["B2", "200", "5"], result.Rows[1]);
    }

    [Fact]
    public void Json_ObjetoConPropiedadArray_UsaEsaLista()
    {
        var result = ScrapeContentParser.ParseJson(
            """{"total":3,"items":[{"n":1},{"n":2},{"n":3}]}""", 20);

        Assert.True(result.Ok);
        Assert.Equal(3, result.ItemCount);
        Assert.Equal(["n"], result.Columns);
    }

    [Fact]
    public void Json_ObjetoSinArrays_EsUnSoloItem()
    {
        var result = ScrapeContentParser.ParseJson("""{"nombre":"unico","valor":9}""", 20);

        Assert.True(result.Ok);
        Assert.Equal(1, result.ItemCount);
        Assert.Single(result.Rows);
    }

    [Fact]
    public void Json_ArrayDeEscalares_UsaColumnaValor()
    {
        var result = ScrapeContentParser.ParseJson("[1,2,3,4]", 20);

        Assert.True(result.Ok);
        Assert.Equal(4, result.ItemCount);
        Assert.Equal(["valor"], result.Columns);
        Assert.Equal(["1"], result.Rows[0]);
    }

    [Fact]
    public void Json_Invalido_EsErrorTipado()
    {
        var result = ScrapeContentParser.ParseJson("{esto no es json", 20);

        Assert.False(result.Ok);
        Assert.Contains("no es JSON valido", result.Error);
    }

    [Fact]
    public void Json_PreviewAcotada_ElConteoEsElTotal()
    {
        var body = "[" + string.Join(",", Enumerable.Range(1, 100).Select(i => $"{{\"n\":{i}}}")) + "]";
        var result = ScrapeContentParser.ParseJson(body, 20);

        Assert.True(result.Ok);
        Assert.Equal(100, result.ItemCount);
        Assert.Equal(20, result.Rows.Count);
    }

    // ---- HTML + selector CSS ----

    private const string Html = """
        <html><body>
          <article class="product-card"><h3>Pintura blanca</h3><span class="price">$89.900</span></article>
          <article class="product-card"><h3>Pintura   gris</h3><span class="price">$104.900</span></article>
          <article class="promo"><h3>No soy producto</h3></article>
        </body></html>
        """;

    [Fact]
    public void Html_SelectorValido_ExtraeTextoNormalizado()
    {
        var result = ScrapeContentParser.ParseHtml(Html, "article.product-card h3", 20);

        Assert.True(result.Ok);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(["texto"], result.Columns);
        Assert.Equal("Pintura blanca", result.Rows[0][0]);
        Assert.Equal("Pintura gris", result.Rows[1][0]); // espacios colapsados
    }

    [Fact]
    public void Html_SelectorCompuesto_Funciona()
    {
        var result = ScrapeContentParser.ParseHtml(Html, "article.product-card > .price", 20);

        Assert.True(result.Ok);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal("$89.900", result.Rows[0][0]);
    }

    [Fact]
    public void Html_SelectorInvalido_EsErrorTipado()
    {
        var result = ScrapeContentParser.ParseHtml(Html, "article..[[", 20);

        Assert.False(result.Ok);
        Assert.Contains("Selector CSS invalido", result.Error);
    }

    [Fact]
    public void Html_SinSelector_EsErrorTipado()
    {
        var result = ScrapeContentParser.ParseHtml(Html, "  ", 20);

        Assert.False(result.Ok);
        Assert.Contains("requieren un selector", result.Error);
    }

    [Fact]
    public void Html_SelectorSinCoincidencias_EsExitoConCero()
    {
        var result = ScrapeContentParser.ParseHtml(Html, "div.inexistente", 20);

        Assert.True(result.Ok);
        Assert.Equal(0, result.ItemCount);
        Assert.Empty(result.Rows);
    }

    // ---- ResultJson persistible ----

    [Fact]
    public void BuildResultJson_EsJsonValidoConLosConteos()
    {
        var parse = ScrapeContentParser.ParseJson("""[{"a":1},{"a":2}]""", 20);
        var json = ScrapeContentParser.BuildResultJson(parse, 64 * 1024);

        using var doc = JsonDocument.Parse(json); // no lanza = JSON valido (jsonb lo exige)
        Assert.Equal(2, doc.RootElement.GetProperty("itemCount").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("previewCount").GetInt32());
    }

    [Fact]
    public void BuildResultJson_RecortaFilas_NuncaBytesCrudos()
    {
        // Filas grandes para forzar el recorte con un tope chico.
        var body = "[" + string.Join(",", Enumerable.Range(1, 50)
            .Select(i => $"{{\"texto\":\"{new string('x', 200)}{i}\"}}")) + "]";
        var parse = ScrapeContentParser.ParseJson(body, 50);

        var json = ScrapeContentParser.BuildResultJson(parse, 2048);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= 2048);
        using var doc = JsonDocument.Parse(json); // sigue siendo JSON valido tras el recorte
        Assert.Equal(50, doc.RootElement.GetProperty("itemCount").GetInt32()); // el TOTAL no se pierde
        Assert.True(doc.RootElement.GetProperty("previewCount").GetInt32() < 50);
    }
}
