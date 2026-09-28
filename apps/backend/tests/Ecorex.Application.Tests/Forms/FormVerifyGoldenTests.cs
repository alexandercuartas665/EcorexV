using System;
using System.Collections.Generic;
using System.Linq;
using Ecorex.Application.Forms;
using Ecorex.Application.Forms.Calc;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// PRUEBAS DORADAS de la auto-revision (verify_form / VerifyForm): fija que detecta cada incoherencia que ya
// rompio formularios reales y que NO ladra en un formulario correcto (cero falsos positivos). El agente
// depende de esto para auto-corregirse antes de cerrar.
public class FormVerifyGoldenTests
{
    private static FormQuestionDto Field(
        string code, FormControlType type, string? calc = null, string? options = null,
        FormSourceKind src = FormSourceKind.Options, string? sourceRef = null)
        => new(Guid.NewGuid(), null, code, code, null, null, type, options, false, 0, "12", null, null,
            CalcExpression: calc, SourceKind: src, SourceRef: sourceRef);

    private static FormDefinitionDetailDto Def(
        IEnumerable<FormQuestionDto> qs, bool transactional = false,
        FormIdentityMode mode = FormIdentityMode.None, string? identitySource = null)
        => new(Guid.NewGuid(), "COD", "Titulo", null, FormStatus.Draft, 1, false, 1,
            Array.Empty<FormContainerDto>(), qs.ToList(),
            IsTransactional: transactional, IdentityMode: mode, IdentitySourceFieldCode: identitySource);

    private static bool HasError(IEnumerable<FormAuthoringToolset.FormVerifyIssue> issues, string needle)
        => issues.Any(i => i.Severity == "error" && (i.Where + " " + i.Problem).Contains(needle, StringComparison.OrdinalIgnoreCase));

    // Grilla items(cantidad,precio,total_item calc+agg=Sum+rollup=subtotal) + subtotal(Number, SIN calc):
    // el patron correcto. Debe salir LIMPIO (sin errores).
    [Fact]
    public void Formulario_correcto_con_rollup_no_reporta_errores()
    {
        var grid = Field("items", FormControlType.GridDetail, options:
            "[{\"id\":\"cantidad\",\"label\":\"Cant\",\"type\":\"text\"}," +
            "{\"id\":\"precio\",\"label\":\"Precio\",\"type\":\"text\"}," +
            "{\"id\":\"total_item\",\"label\":\"Total\",\"type\":\"calc\",\"calc\":\"{cantidad}*{precio}\",\"agg\":\"Sum\",\"rollup\":\"subtotal\"}]");
        var subtotal = Field("subtotal", FormControlType.Number); // destino del rollup, SIN calc
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { grid, subtotal }));
        Assert.DoesNotContain(issues, i => i.Severity == "error");
    }

    // El rollup apunta a "subtotal" pero ese campo no existe en el encabezado.
    [Fact]
    public void Rollup_a_campo_inexistente_es_error()
    {
        var grid = Field("items", FormControlType.GridDetail, options:
            "[{\"id\":\"total_item\",\"label\":\"Total\",\"type\":\"calc\",\"calc\":\"{cantidad}*1\",\"agg\":\"Sum\",\"rollup\":\"subtotal\"},{\"id\":\"cantidad\",\"label\":\"C\",\"type\":\"text\"}]");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { grid }));
        Assert.True(HasError(issues, "subtotal"));
    }

    // El campo destino del rollup ADEMAS tiene calc: el calc pisa el total de la columna (la regresion clasica).
    [Fact]
    public void Destino_de_rollup_con_calc_es_error()
    {
        var grid = Field("items", FormControlType.GridDetail, options:
            "[{\"id\":\"total_item\",\"label\":\"Total\",\"type\":\"calc\",\"calc\":\"{cantidad}*1\",\"agg\":\"Sum\",\"rollup\":\"subtotal\"},{\"id\":\"cantidad\",\"label\":\"C\",\"type\":\"text\"}]");
        var subtotal = Field("subtotal", FormControlType.Number, calc: "{items.total_item}");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { grid, subtotal }));
        Assert.True(HasError(issues, "pisa"));
    }

    // Un CAMPO que intenta sumar una columna con {#...} (debe ser rollup).
    [Fact]
    public void Campo_que_suma_columna_con_hash_es_error()
    {
        var grid = Field("items", FormControlType.GridDetail, options:
            "[{\"id\":\"total_item\",\"label\":\"T\",\"type\":\"text\"}]");
        var subtotal = Field("subtotal", FormControlType.Number, calc: "{#items.total_item}");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { grid, subtotal }));
        Assert.True(HasError(issues, "grilla"));
    }

    // calc de un campo que referencia un field_code que no existe (typo).
    [Fact]
    public void Referencia_colgante_en_calc_de_campo_es_error()
    {
        var iva = Field("iva", FormControlType.Number, calc: "{subtottal}*0.19"); // typo: subtottal
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { iva }));
        Assert.True(HasError(issues, "subtottal"));
    }

    // calc de un campo que referencia otro campo REAL: no debe reportar nada.
    [Fact]
    public void Referencia_valida_en_calc_de_campo_no_reporta()
    {
        var subtotal = Field("subtotal", FormControlType.Number);
        var iva = Field("iva", FormControlType.Number, calc: "{subtotal}*0.19");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { subtotal, iva }));
        Assert.DoesNotContain(issues, i => i.Severity == "error");
    }

    // Lookup de contenedor sin source_ref.
    [Fact]
    public void Lookup_sin_fuente_es_error()
    {
        var producto = Field("producto", FormControlType.Select, src: FormSourceKind.DataContainer, sourceRef: null);
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { producto }));
        Assert.True(HasError(issues, "source_ref"));
    }

    // Lista de opciones fijas sin opciones.
    [Fact]
    public void Lista_de_opciones_vacia_es_error()
    {
        var estado = Field("estado", FormControlType.Select, options: "[]");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { estado }));
        Assert.True(HasError(issues, "opciones"));
    }

    // Grilla sin columnas.
    [Fact]
    public void Grilla_sin_columnas_es_error()
    {
        var grid = Field("items", FormControlType.GridDetail, options: "[]");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { grid }));
        Assert.True(HasError(issues, "columnas"));
    }

    // NaturalKey apuntando a un campo que no existe.
    [Fact]
    public void NaturalKey_a_campo_inexistente_es_error()
    {
        var numero = Field("otro", FormControlType.Text);
        var def = Def(new[] { numero }, transactional: true, mode: FormIdentityMode.NaturalKey, identitySource: "numero_doc");
        var issues = FormAuthoringToolset.VerifyForm(def);
        Assert.True(HasError(issues, "numero_doc"));
    }

    // NaturalKey a un campo real: sin error.
    [Fact]
    public void NaturalKey_a_campo_real_no_reporta()
    {
        var numero = Field("numero_doc", FormControlType.Text);
        var def = Def(new[] { numero }, transactional: true, mode: FormIdentityMode.NaturalKey, identitySource: "numero_doc");
        var issues = FormAuthoringToolset.VerifyForm(def);
        Assert.DoesNotContain(issues, i => i.Severity == "error");
    }

    // calc de columna que referencia una columna inexistente de la propia grilla.
    [Fact]
    public void Calc_de_columna_a_columna_inexistente_es_error()
    {
        var grid = Field("items", FormControlType.GridDetail, options:
            "[{\"id\":\"total\",\"label\":\"T\",\"type\":\"calc\",\"calc\":\"{cantidad}*{precio}\"},{\"id\":\"cantidad\",\"label\":\"C\",\"type\":\"text\"}]");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { grid }));
        Assert.True(HasError(issues, "precio")); // 'precio' no es columna
    }

    // REGRESION del brief en vivo: el agente escribio la columna en camelCase (calcExpression/aggregate/
    // controlType) y apunto el rollup a un campo inexistente. Antes verify_form quedaba ciego (ParseColumns
    // no leia esas claves -> Agg=None). Ahora, con la tolerancia de alias, debe DETECTAR el rollup colgante.
    [Fact]
    public void Grilla_en_camelCase_verify_detecta_rollup_inexistente()
    {
        var grid = Field("items", FormControlType.GridDetail, options:
            "[{\"id\":\"total_item\",\"label\":\"Total\",\"controlType\":\"Number\",\"calcExpression\":\"{cantidad} * {precio}\",\"aggregate\":\"Sum\",\"rollup\":\"gran_total\"},{\"id\":\"cantidad\",\"label\":\"C\",\"controlType\":\"Number\"}]");
        var issues = FormAuthoringToolset.VerifyForm(Def(new[] { grid }));
        Assert.True(HasError(issues, "gran_total"));
    }
}

// PRUEBA DORADA de la tolerancia de alias camelCase en el parser de columnas del motor: una grilla escrita con
// calcExpression/aggregate/controlType debe COMPUTAR igual que con calc/agg/type (antes se quedaba muda).
public class FormGridColumnAliasTests
{
    [Fact]
    public void ParseColumns_lee_alias_camelCase()
    {
        var cols = FormGridCalculator.ParseColumns(
            "[{\"id\":\"total_item\",\"label\":\"Total\",\"controlType\":\"Number\",\"calcExpression\":\"{cantidad}*{precio}\",\"aggregate\":\"Sum\",\"rollup\":\"subtotal\"}]");
        var c = Assert.Single(cols);
        Assert.Equal("{cantidad}*{precio}", c.Calc);
        Assert.Equal(FormAggregate.Sum, c.Agg);
        Assert.Equal("subtotal", c.Rollup);
        Assert.Equal("number", c.Kind);
    }

    [Fact]
    public void ParseColumns_la_clave_canonica_gana_sobre_el_alias()
    {
        var cols = FormGridCalculator.ParseColumns(
            "[{\"id\":\"x\",\"calc\":\"{a}\",\"calcExpression\":\"{b}\",\"agg\":\"Sum\",\"aggregate\":\"Count\"}]");
        var c = Assert.Single(cols);
        Assert.Equal("{a}", c.Calc);
        Assert.Equal(FormAggregate.Sum, c.Agg);
    }

    // Una grilla en camelCase debe rollupear igual: total_item = cantidad*precio, sumado a subtotal.
    [Fact]
    public void Compute_rollup_funciona_con_columnas_en_camelCase()
    {
        var cols = FormGridCalculator.ParseColumns(
            "[{\"id\":\"cantidad\",\"controlType\":\"Number\"},{\"id\":\"precio\",\"controlType\":\"Number\"}," +
            "{\"id\":\"total_item\",\"controlType\":\"Number\",\"calcExpression\":\"{cantidad}*{precio}\",\"aggregate\":\"Sum\",\"rollup\":\"subtotal\"}]");
        var rows = new List<Dictionary<string, string?>>
        {
            new(StringComparer.Ordinal) { ["cantidad"] = "2", ["precio"] = "1000" },
            new(StringComparer.Ordinal) { ["cantidad"] = "3", ["precio"] = "500" },
        };
        var (computed, rollups) = FormGridCalculator.Recompute(rows, cols);
        Assert.Equal("2000", computed[0]["total_item"]);
        Assert.Equal("3500", rollups["subtotal"]); // 2000 + 1500
    }
}
