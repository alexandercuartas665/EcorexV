using System;
using System.Collections.Generic;
using System.Linq;
using Ecorex.Application.Forms;
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
}
