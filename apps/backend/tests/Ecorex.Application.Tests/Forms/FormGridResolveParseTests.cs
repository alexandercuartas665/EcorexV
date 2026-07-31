using Ecorex.Application.Forms.Lookups;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// Contrato de la llave 'resolve' (auto-resolucion multi-clave VLOOKUP) en el options_json de una
// columna de GridDetail: match compuesto + return + guarda 'when'.
public class FormGridResolveParseTests
{
    private const string Json = """
    [
      { "id": "tipo_lamina", "label": "Lamina" },
      { "id": "espesor", "label": "Espesor" },
      { "id": "rolado", "label": "Rolado" },
      { "id": "precio_corte", "label": "Corte", "type": "text",
        "resolve": { "source": "DataContainer", "sourceRef": "41eae926-02ad-432c-bd06-320d17b98d09",
                     "match": { "Lamina": "{tipo_lamina}", "Espesor": "{espesor}" }, "return": "Precio" } },
      { "id": "rolado_unitario", "label": "Rolado", "type": "text",
        "resolve": { "source": "DataContainer", "sourceRef": "67bf3c8a-5682-465c-be16-7e5b1d446013",
                     "match": { "Lamina": "{tipo_lamina}" }, "return": "Precio",
                     "when": { "{rolado}": "SI" } } }
    ]
    """;

    [Fact]
    public void Parse_lee_resolve_multiclave()
    {
        var extras = FormGridColumnLookupParser.Parse(Json);

        Assert.True(extras.TryGetValue("precio_corte", out var corte));
        var rc = corte!.Resolve;
        Assert.NotNull(rc);
        Assert.Equal(FormSourceKind.DataContainer, rc!.SourceKind);
        Assert.Equal("41eae926-02ad-432c-bd06-320d17b98d09", rc.SourceRef);
        Assert.Equal("Precio", rc.ReturnField);
        Assert.Equal("{tipo_lamina}", rc.Match["Lamina"]);
        Assert.Equal("{espesor}", rc.Match["Espesor"]);
        Assert.Empty(rc.When);
    }

    [Fact]
    public void Parse_lee_la_guarda_when()
    {
        var extras = FormGridColumnLookupParser.Parse(Json);

        var rolado = extras["rolado_unitario"].Resolve;
        Assert.NotNull(rolado);
        Assert.Single(rolado!.Match);
        Assert.Equal("SI", rolado.When["{rolado}"]);
    }

    [Fact]
    public void Resolve_sin_match_o_sin_return_se_ignora()
    {
        var bad = """
        [ { "id": "x", "resolve": { "source": "DataContainer", "sourceRef": "a" } } ]
        """;

        var extras = FormGridColumnLookupParser.Parse(bad);

        Assert.False(extras.ContainsKey("x"));
    }
}
