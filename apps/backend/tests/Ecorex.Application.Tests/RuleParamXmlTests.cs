using System.Text.Json.Nodes;
using Ecorex.Application.Rules;
using Ecorex.Application.Rules.Verbs;

namespace Ecorex.Application.Tests;

/// <summary>
/// Tests unitarios de RuleParamXml (ADR-0023): round-trip ParamsJson -&gt; PARAM_XML -&gt;
/// ParamsJson por cada tipo de parametro, y errores claros de parseo (XML malformado,
/// proceso distinto, parametro desconocido/repetido, tipo invalido, obligatorio faltante).
/// Clase pura: sin BD.
/// </summary>
public class RuleParamXmlTests
{
    /// <summary>Descriptor sintetico con TODOS los tipos de parametro.</summary>
    private static readonly RuleVerbDescriptor Descriptor = new(
        "VERBO_DEMO", "Verbo demo", "Descriptor sintetico para probar PARAM_XML.",
        [
            new RuleVerbParamDescriptor("mensaje", "Mensaje", RuleParamType.Text, Required: true),
            new RuleVerbParamDescriptor("cantidad", "Cantidad", RuleParamType.Number, Required: false),
            new RuleVerbParamDescriptor("activo", "Activo", RuleParamType.Boolean, Required: false),
            new RuleVerbParamDescriptor("campo", "Campo", RuleParamType.FieldCode, Required: false),
            new RuleVerbParamDescriptor("mapeos", "Mapeos", RuleParamType.Json, Required: false)
        ]);

    // ---- Round-trip ----

    [Fact]
    public void GenerateThenParse_RoundTripsAllParamTypes()
    {
        const string paramsJson =
            """{"mensaje":"hola <mundo> & \"todos\"","cantidad":42.5,"activo":true,"campo":"nombre_solicitante","mapeos":[{"source":"a","target":"b"}]}""";

        var xml = RuleParamXml.Generate(Descriptor, paramsJson);
        var parsed = RuleParamXml.Parse(xml, Descriptor);

        Assert.True(parsed.IsOk, parsed.Error);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(paramsJson), JsonNode.Parse(parsed.Value!)),
            $"round-trip distinto: {parsed.Value}");
    }

    [Fact]
    public void GenerateThenParse_RoundTripsWithRealVerbDescriptor()
    {
        // Descriptor REAL del catalogo tipado (PASAR_CAMPOS): el contrato del modulo.
        var descriptor = new PasarCamposVerb().Descriptor;
        const string paramsJson = """{"mappings":[{"source":"origen","target":"destino"}]}""";

        var xml = RuleParamXml.Generate(descriptor, paramsJson);
        Assert.Contains("<PROCESO>" + descriptor.VerbName + "</PROCESO>", xml);

        var parsed = RuleParamXml.Parse(xml, descriptor);
        Assert.True(parsed.IsOk, parsed.Error);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(paramsJson), JsonNode.Parse(parsed.Value!)));
    }

    [Fact]
    public void Generate_EmitsAllDescriptorParams_UnsetOnesWithoutValor()
    {
        var xml = RuleParamXml.Generate(Descriptor, """{"mensaje":"hola"}""");

        // Todos los params del descriptor aparecen (contrato visible en el editor)...
        Assert.Contains("name=\"mensaje\"", xml);
        Assert.Contains("name=\"cantidad\"", xml);
        Assert.Contains("name=\"mapeos\"", xml);
        Assert.Contains("obligatorio=\"true\"", xml);
        // ...pero solo el configurado lleva valor.
        Assert.Contains("valor=\"hola\"", xml);
        Assert.Single(xml.Split("valor=\"", StringSplitOptions.None).Skip(1));
    }

    [Fact]
    public void Generate_WithCorruptOrEmptyParamsJson_EmitsContractWithoutValues()
    {
        foreach (var paramsJson in new[] { null, "", "no-es-json", "[1,2]" })
        {
            var xml = RuleParamXml.Generate(Descriptor, paramsJson);
            Assert.Contains("<PROCESO>VERBO_DEMO</PROCESO>", xml);
            Assert.DoesNotContain("valor=", xml);
        }
    }

    [Fact]
    public void Parse_WithoutAnyValues_ReturnsNullWhenNoRequiredParams()
    {
        var optional = new RuleVerbDescriptor("VERBO_OPC", "Opcional", "Sin obligatorios.",
            [new RuleVerbParamDescriptor("nota", "Nota", RuleParamType.Text, Required: false)]);
        var xml = RuleParamXml.Generate(optional, null);

        var parsed = RuleParamXml.Parse(xml, optional);

        Assert.True(parsed.IsOk, parsed.Error);
        Assert.Null(parsed.Value);
    }

    // ---- Errores de parseo ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyXml_IsInvalid(string xml)
    {
        var parsed = RuleParamXml.Parse(xml, Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, parsed.Status);
        Assert.Contains("vacio", parsed.Error);
    }

    [Fact]
    public void Parse_MalformedXml_IsInvalidWithClearError()
    {
        var parsed = RuleParamXml.Parse("<REGLA><PROCESO>VERBO_DEMO</PROCESO>", Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, parsed.Status);
        Assert.Contains("XML malformado", parsed.Error);
    }

    [Fact]
    public void Parse_WrongRootOrMissingProceso_IsInvalid()
    {
        var wrongRoot = RuleParamXml.Parse("<OTRA />", Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, wrongRoot.Status);
        Assert.Contains("<REGLA>", wrongRoot.Error);

        var noProceso = RuleParamXml.Parse("<REGLA><PARAMETROS /></REGLA>", Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, noProceso.Status);
        Assert.Contains("PROCESO", noProceso.Error);
    }

    [Fact]
    public void Parse_ProcesoMismatch_IsInvalid()
    {
        var parsed = RuleParamXml.Parse(
            "<REGLA><PROCESO>OTRO_VERBO</PROCESO><PARAMETROS /></REGLA>", Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, parsed.Status);
        Assert.Contains("no coincide", parsed.Error);
        Assert.Contains("VERBO_DEMO", parsed.Error);
    }

    [Fact]
    public void Parse_UnknownParam_IsInvalidAndListsValidNames()
    {
        var parsed = RuleParamXml.Parse(
            """<REGLA><PROCESO>VERBO_DEMO</PROCESO><PARAMETROS><PARAM name="sql" valor="DROP TABLE" /></PARAMETROS></REGLA>""",
            Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, parsed.Status);
        Assert.Contains("'sql' no existe", parsed.Error);
        Assert.Contains("mensaje", parsed.Error);
    }

    [Fact]
    public void Parse_DuplicatedParam_IsInvalid()
    {
        var parsed = RuleParamXml.Parse(
            """<REGLA><PROCESO>VERBO_DEMO</PROCESO><PARAMETROS><PARAM name="mensaje" valor="a" /><PARAM name="mensaje" valor="b" /></PARAMETROS></REGLA>""",
            Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, parsed.Status);
        Assert.Contains("repetido", parsed.Error);
    }

    [Theory]
    [InlineData("cantidad", "no-numero", "numerico")]
    [InlineData("activo", "quizas", "true o false")]
    [InlineData("mapeos", "{roto", "no es JSON valido")]
    public void Parse_BadTypedValues_AreInvalidWithParamName(string name, string valor, string expectedError)
    {
        var xml = $"""
            <REGLA><PROCESO>VERBO_DEMO</PROCESO><PARAMETROS>
              <PARAM name="mensaje" valor="ok" />
              <PARAM name="{name}" valor="{valor}" />
            </PARAMETROS></REGLA>
            """;
        var parsed = RuleParamXml.Parse(xml, Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, parsed.Status);
        Assert.Contains($"'{name}'", parsed.Error);
        Assert.Contains(expectedError, parsed.Error);
    }

    [Fact]
    public void Parse_MissingRequiredParam_IsInvalid()
    {
        // 'mensaje' es obligatorio: presente sin valor cuenta como faltante.
        var parsed = RuleParamXml.Parse(
            """<REGLA><PROCESO>VERBO_DEMO</PROCESO><PARAMETROS><PARAM name="mensaje" tipo="string" obligatorio="true" /></PARAMETROS></REGLA>""",
            Descriptor);
        Assert.Equal(RuleServiceStatus.Invalid, parsed.Status);
        Assert.Contains("obligatorios", parsed.Error);
        Assert.Contains("mensaje", parsed.Error);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndIsCaseInsensitiveOnNames()
    {
        var parsed = RuleParamXml.Parse(
            """
            <!-- comentario libre -->
            <regla><proceso>verbo_demo</proceso><parametros>
              <param name="MENSAJE" valor="hola" />
            </parametros></regla>
            """, Descriptor);
        Assert.True(parsed.IsOk, parsed.Error);
        // La clave sale con el nombre CANONICO del descriptor (round-trip estable).
        Assert.Equal("hola", JsonNode.Parse(parsed.Value!)!["mensaje"]!.GetValue<string>());
    }

    // ---- Formatear ----

    [Fact]
    public void Format_ReindentsValidXml_AndRejectsBrokenXml()
    {
        var compact = "<REGLA><PROCESO>VERBO_DEMO</PROCESO><PARAMETROS><PARAM name=\"mensaje\" valor=\"x\" /></PARAMETROS></REGLA>";
        var formatted = RuleParamXml.Format(compact);
        Assert.True(formatted.IsOk, formatted.Error);
        Assert.Contains("\n", formatted.Value);
        Assert.Contains("  <PROCESO>", formatted.Value);

        var broken = RuleParamXml.Format("<REGLA>");
        Assert.Equal(RuleServiceStatus.Invalid, broken.Status);
        Assert.Contains("XML malformado", broken.Error);
    }
}
