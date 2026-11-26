using Ecorex.Application.Forms;

namespace Ecorex.Application.Tests.Forms;

/// <summary>
/// Diseno por control (FormFieldStyle: saneo anti-inyeccion de CSS) y reescritura de referencias al renombrar
/// el nombre interno de un campo (FormFieldCodeRename: solo posiciones que son codigo, nunca ids de columna).
/// </summary>
public class FormFieldStyleAndRenameTests
{
    [Fact]
    public void Estilo_SaneaColoresYAcotaNumeros()
    {
        var s = FormFieldStyle.Parse("""{"labelSize":999,"labelColor":"red;}body{display:none","boxBg":"#ABCDEF","height":5,"align":"CENTER","valueBold":true}""");
        Assert.Equal(48, s.LabelSize);
        Assert.Null(s.LabelColor); // no es #hex: se descarta (no puede inyectar CSS)
        Assert.Equal("#abcdef", s.BoxBg);
        Assert.Equal(20, s.Height);
        Assert.Equal("center", s.Align);
        Assert.True(s.ValueBold);
        Assert.Contains("fs-bg", s.CssClasses());
        Assert.Contains("--fs-bg:#abcdef;", s.InlineStyle());
        Assert.DoesNotContain("body", s.InlineStyle());
    }

    [Fact]
    public void Estilo_VacioNoGuardaNada()
    {
        Assert.Null(FormFieldStyle.Normalize("""{"labelColor":"azul","align":"diagonal"}"""));
        Assert.Null(FormFieldStyle.Normalize("no es json"));
        Assert.Equal(string.Empty, FormFieldStyle.Empty.CssClasses());
    }

    [Fact]
    public void Renombrar_Calculo_ReescribeTokensDeCampo()
    {
        Assert.Equal("{cant_total} * {precio} + {#cant_total}",
            FormFieldCodeRename.FieldTokens("{cant} * {precio} + {#cant}", "cant", "cant_total"));
        // No toca un codigo que solo empieza igual.
        Assert.Equal("{cantidad}", FormFieldCodeRename.FieldTokens("{cantidad}", "cant", "x"));
    }

    [Fact]
    public void Renombrar_Grilla_SoloEncabezadoYRollup_NoIdsDeColumna()
    {
        var json = """{"columns":[{"id":"total","calc":"{total} * {#tasa}","rollup":"tasa"},{"id":"tasa"}]}""";
        var r = FormFieldCodeRename.Options(json, "tasa", "tasa_iva")!;
        Assert.Contains("{#tasa_iva}", r);
        Assert.Contains("\"rollup\":\"tasa_iva\"", r);
        Assert.Contains("\"id\":\"tasa\"", r); // id de columna: intacto
        Assert.Contains("{total}", r);
    }

    [Fact]
    public void Renombrar_Autollenado_SoloValores()
    {
        var r = FormFieldCodeRename.AutofillValues("""{"nit":"nit","ciudad":"ciudad_cli"}""", "nit", "nit_cliente");
        Assert.Equal("""{"nit":"nit_cliente","ciudad":"ciudad_cli"}""", r);
    }

    [Fact]
    public void Renombrar_Respuesta_RenombraLaClaveYConservaElOrdenYLosAcentos()
    {
        const string senor = "Se\u00f1or <b>";
        var r = FormFieldCodeRename.DataKey("{\"a\":{\"value\":\"1\"},\"nombre\":{\"value\":\"" + senor + "\"},\"z\":{\"value\":\"2\"}}", "nombre", "razon_social");
        Assert.Equal("{\"a\":{\"value\":\"1\"},\"razon_social\":{\"value\":\"" + senor + "\"},\"z\":{\"value\":\"2\"}}", r);
    }

    [Fact]
    public void Renombrar_ParametrosDeRegla()
    {
        var json = """{"titleField":"nombre","titleTemplate":"Lead {nombre} - {ciudad}","requireFields":["nombre","x"],"mappings":[{"source":"nombre","target":"otro"}],"mapping":{"nombre":"nombre_destino"},"mapeo":{"desc":"field:nombre","col":"col:nombre"}}""";
        var r = FormFieldCodeRename.RuleParams(json, "nombre", "cliente")!;
        Assert.Contains("\"titleField\":\"cliente\"", r);
        Assert.Contains("Lead {cliente} - {ciudad}", r);
        Assert.Contains("[\"cliente\",\"x\"]", r);
        Assert.Contains("\"source\":\"cliente\"", r);
        Assert.Contains("\"cliente\":\"nombre_destino\"", r); // clave = origen (si); valor = destino (no)
        Assert.Contains("\"desc\":\"field:cliente\"", r);
        Assert.Contains("\"col\":\"col:nombre\"", r); // col: es columna, no codigo
    }

    [Fact]
    public void Renombrar_RequireFieldsComoTextoSeparadoPorComas()
    {
        var r = FormFieldCodeRename.RuleParams("""{"requireFields":"a, nombre ,b"}""", "nombre", "cliente");
        Assert.Equal("""{"requireFields":"a,cliente,b"}""", r);
    }

    [Fact]
    public void Renombrar_SinCambios_DevuelveElMismoTexto()
    {
        const string json = """{ "field" : "otro", "x": "nombre" }""";
        Assert.Same(json, FormFieldCodeRename.FieldProps(json, "nombre", "x"));
        Assert.Equal(".field-total_iva{color:red} .field-x{}",
            FormFieldCodeRename.Css(".field-total_iva{color:red} .field-total{}", "total", "x"));
    }

    [Fact]
    public void Renombrar_EstadosYCondiciones_PropiedadField()
    {
        var r = FormFieldCodeRename.FieldProps("""{"field":"estado","states":[{"when":[{"field":"total","op":"gt"}]}]}""", "total", "valor");
        Assert.Equal("""{"field":"estado","states":[{"when":[{"field":"valor","op":"gt"}]}]}""", r);
    }
}
