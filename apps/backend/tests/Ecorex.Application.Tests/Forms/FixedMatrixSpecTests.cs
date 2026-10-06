using System;
using System.Collections.Generic;
using System.Linq;
using Ecorex.Application.Forms;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Xunit;

namespace Ecorex.Application.Tests.Forms;

// PRUEBAS DORADAS de la Matriz FIJA (FixedMatrix): el parser compartido (spec + valor), la validacion de
// estructura/valor y la auto-revision. Es la primitiva que reemplaza la simulacion del 350 con Rows sueltas.
public class FixedMatrixSpecTests
{
    private const string F350 = """
        {"rows":[{"id":"rentas_trabajo","label":"Rentas de trabajo"},{"id":"honorarios","label":"Honorarios"}],
         "cols":[{"id":"jur_base","label":"Base sujeta a retencion","group":"A personas juridicas","format":"currency"},
                 {"id":"jur_ret","label":"Retenciones a titulo de renta","group":"A personas juridicas","format":"currency"},
                 {"id":"nat_base","label":"Base sujeta a retencion","group":"A personas naturales","format":"currency"},
                 {"id":"nat_ret","label":"Retenciones a titulo de renta","group":"A personas naturales","format":"currency"}],
         "captions":{"honorarios.jur_base":"29","honorarios.jur_ret":"42","honorarios.nat_base":"79","honorarios.nat_ret":"95","rentas_trabajo.nat_base":"77","rentas_trabajo.nat_ret":"93"},
         "disabled":["rentas_trabajo.jur_base","rentas_trabajo.jur_ret"]}
        """;

    [Fact]
    public void Parse_lee_filas_columnas_grupos_captions_y_celdas_na()
    {
        var spec = FixedMatrixSpec.Parse(F350);
        Assert.NotNull(spec);
        Assert.Equal(2, spec!.Rows.Count);
        Assert.Equal(4, spec.Cols.Count);
        Assert.Equal("A personas juridicas", spec.Cols[0].Group);
        Assert.Equal("currency", spec.Cols[0].Format);
        Assert.Equal("29", spec.Caption("honorarios", "jur_base"));
        Assert.True(spec.IsDisabled("rentas_trabajo", "jur_base"));
        Assert.False(spec.IsDisabled("honorarios", "jur_base"));
        Assert.Equal(6, spec.ActiveCellCount); // 8 celdas - 2 n/a
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]                                                   // arreglo (shape de GridDetail), no objeto
    [InlineData("""{"rows":[{"id":"a","label":"A"}]}""")]                 // sin columnas
    [InlineData("""{"rows":[],"cols":[{"id":"c","label":"C"}]}""")]       // sin filas
    [InlineData("""{"rows":[{"id":"a","label":"A"},{"id":"a","label":"B"}],"cols":[{"id":"c","label":"C"}]}""")] // ids repetidos
    [InlineData("""{"rows":[{"id":"a"}],"cols":[{"id":"c","label":"C"}]}""")] // fila sin label
    public void Parse_rechaza_specs_invalidas(string? json)
    {
        Assert.Null(FixedMatrixSpec.Parse(json));
    }

    [Fact]
    public void Valor_ida_y_vuelta_estable_y_sin_celdas_vacias()
    {
        var cells = new Dictionary<string, string?> { ["honorarios.jur_base"] = "1000", ["honorarios.jur_ret"] = "", ["rentas_trabajo.nat_base"] = "500" };
        var json = FixedMatrixSpec.SerializeValue(cells);
        var back = FixedMatrixSpec.ParseValue(json);
        Assert.Equal(2, back.Count);
        Assert.Equal("1000", back["honorarios.jur_base"]);
        Assert.Equal("500", back["rentas_trabajo.nat_base"]);
        Assert.Equal(json, FixedMatrixSpec.SerializeValue(back)); // estable
        Assert.Empty(FixedMatrixSpec.ParseValue("no es json"));
    }

    [Fact]
    public void Validacion_de_valor_obligatorio_celda_inexistente_y_celda_na()
    {
        var sinOpciones = Array.Empty<FormOption>();
        // Obligatorio y vacio -> error de obligatorio.
        Assert.NotNull(FormFieldValidator.Validate(FormControlType.FixedMatrix, true, null, sinOpciones, null, F350));
        // Valor valido -> sin error.
        Assert.Null(FormFieldValidator.Validate(FormControlType.FixedMatrix, true, """{"honorarios.jur_base":"1000"}""", sinOpciones, null, F350));
        // Celda que no existe en la spec.
        Assert.Contains("no existe", FormFieldValidator.Validate(FormControlType.FixedMatrix, false, """{"otra.jur_base":"1"}""", sinOpciones, null, F350)!);
        // Celda marcada n/a.
        Assert.Contains("no aplica", FormFieldValidator.Validate(FormControlType.FixedMatrix, false, """{"rentas_trabajo.jur_base":"1"}""", sinOpciones, null, F350)!);
    }

    [Fact]
    public void Verify_form_marca_error_una_matriz_sin_spec_valida()
    {
        var bad = new FormQuestionDto(Guid.NewGuid(), null, "matriz", "Matriz", null, null, FormControlType.FixedMatrix,
            "[]", false, 0, "col-12", null, null);
        var ok = new FormQuestionDto(Guid.NewGuid(), null, "matriz_ok", "Matriz OK", null, null, FormControlType.FixedMatrix,
            F350, false, 0, "col-12", null, null);
        var def = new FormDefinitionDetailDto(Guid.NewGuid(), "COD", "Titulo", null, FormStatus.Draft, 1, false, 1,
            Array.Empty<FormContainerDto>(), new[] { bad, ok });
        var issues = FormAuthoringToolset.VerifyForm(def);
        Assert.Contains(issues, i => i.Severity == "error" && i.Where.Contains("matriz'") && i.Problem.Contains("filas/columnas"));
        Assert.DoesNotContain(issues, i => i.Where.Contains("matriz_ok"));
    }

    // IsCapture excluye la matriz (su valor es una coleccion); IsTier1 la incluye (tiene componente en el renderer).
    [Fact]
    public void Clasificadores_tratan_la_matriz_como_coleccion_tier1()
    {
        Assert.False(FormFieldValidator.IsCapture(FormControlType.FixedMatrix));
        Assert.False(FormFieldValidator.IsNonInput(FormControlType.FixedMatrix));
        Assert.True(FormFieldValidator.IsTier1(FormControlType.FixedMatrix));
    }
}
