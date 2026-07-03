using Ecorex.Application.Forms;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tests;

/// <summary>
/// Unit tests del validador de campos de formularios dinamicos (FASE 4 ola 2, ADR-0015):
/// required, longitudes, pattern, rangos numericos, opciones invalidas, fechas y toggle.
/// Es la MISMA logica que corre en cliente (renderer) y en servidor (submit).
/// </summary>
public class FormFieldValidatorTests
{
    private static readonly IReadOnlyList<FormOption> Options =
    [
        new("alta", "Alta"),
        new("media", "Media"),
        new("baja", "Baja", "BAJA")
    ];

    // ---- Required ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiredText_EmptyValue_Fails(string? value)
    {
        var error = FormFieldValidator.Validate(FormControlType.Text, required: true, value);
        Assert.Equal("Este campo es obligatorio.", error);
    }

    [Fact]
    public void OptionalText_EmptyValue_Passes()
    {
        Assert.Null(FormFieldValidator.Validate(FormControlType.Text, required: false, null));
    }

    [Fact]
    public void RequiredMultiCheck_EmptyArray_Fails()
    {
        var error = FormFieldValidator.Validate(FormControlType.MultiCheck, required: true, "[]", Options);
        Assert.Equal("Este campo es obligatorio.", error);
    }

    // ---- Controles que no capturan datos ----

    [Theory]
    [InlineData(FormControlType.Heading)]
    [InlineData(FormControlType.Literal)]
    public void NonInputControls_NeverValidate(FormControlType type)
    {
        Assert.True(FormFieldValidator.IsNonInput(type));
        Assert.Null(FormFieldValidator.Validate(type, required: true, null));
    }

    // ---- Longitudes y pattern ----

    [Fact]
    public void Text_ShorterThanMinLength_Fails()
    {
        var rules = new FormValidationRules(MinLength: 5);
        var error = FormFieldValidator.Validate(FormControlType.Text, false, "abc", rules: rules);
        Assert.Equal("Minimo 5 caracteres.", error);
    }

    [Fact]
    public void Text_LongerThanMaxLength_Fails()
    {
        var rules = new FormValidationRules(MaxLength: 3);
        var error = FormFieldValidator.Validate(FormControlType.TextArea, false, "abcdef", rules: rules);
        Assert.Equal("Maximo 3 caracteres.", error);
    }

    [Fact]
    public void Text_WithinLengthBounds_Passes()
    {
        var rules = new FormValidationRules(MinLength: 2, MaxLength: 5);
        Assert.Null(FormFieldValidator.Validate(FormControlType.Text, false, "abcd", rules: rules));
    }

    [Fact]
    public void Text_PatternMismatch_Fails()
    {
        var rules = new FormValidationRules(Pattern: "^[0-9]+$");
        var error = FormFieldValidator.Validate(FormControlType.Text, false, "abc", rules: rules);
        Assert.Equal("El valor no tiene el formato esperado.", error);
    }

    [Fact]
    public void Text_PatternMatch_Passes()
    {
        var rules = new FormValidationRules(Pattern: "^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}$");
        Assert.Null(FormFieldValidator.Validate(FormControlType.Text, false, "a@b.co", rules: rules));
    }

    [Fact]
    public void Text_InvalidPattern_IsIgnored()
    {
        // El pattern no compilable se rechaza al GUARDAR la pregunta; al validar se ignora.
        var rules = new FormValidationRules(Pattern: "([");
        Assert.Null(FormFieldValidator.Validate(FormControlType.Text, false, "x", rules: rules));
    }

    // ---- Numeros ----

    [Theory]
    [InlineData("abc")]
    [InlineData("12a")]
    public void Number_NotParseable_Fails(string value)
    {
        var error = FormFieldValidator.Validate(FormControlType.Number, false, value);
        Assert.Equal("Ingresa un numero valido.", error);
    }

    [Fact]
    public void Number_BelowMinValue_Fails()
    {
        var rules = new FormValidationRules(MinValue: 10);
        var error = FormFieldValidator.Validate(FormControlType.Number, false, "9.5", rules: rules);
        Assert.Equal("El valor minimo es 10.", error);
    }

    [Fact]
    public void Number_AboveMaxValue_Fails()
    {
        var rules = new FormValidationRules(MaxValue: 100);
        var error = FormFieldValidator.Validate(FormControlType.Number, false, "100.01", rules: rules);
        Assert.Equal("El valor maximo es 100.", error);
    }

    [Fact]
    public void Number_InRange_Passes()
    {
        var rules = new FormValidationRules(MinValue: 1, MaxValue: 100);
        Assert.Null(FormFieldValidator.Validate(FormControlType.Number, false, "55.5", rules: rules));
    }

    // ---- Fechas y toggle ----

    [Fact]
    public void Date_Invalid_Fails()
    {
        var error = FormFieldValidator.Validate(FormControlType.Date, false, "no-es-fecha");
        Assert.Equal("Ingresa una fecha valida.", error);
    }

    [Fact]
    public void Date_IsoFormat_Passes()
    {
        Assert.Null(FormFieldValidator.Validate(FormControlType.Date, false, "2026-07-03"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Toggle_BooleanString_Passes(string value)
    {
        Assert.Null(FormFieldValidator.Validate(FormControlType.Toggle, false, value));
    }

    [Fact]
    public void Toggle_NonBoolean_Fails()
    {
        var error = FormFieldValidator.Validate(FormControlType.Toggle, false, "si");
        Assert.Equal("Valor de interruptor invalido.", error);
    }

    // ---- Opciones (Select / Radio / MultiCheck) ----

    [Theory]
    [InlineData(FormControlType.Select)]
    [InlineData(FormControlType.Radio)]
    public void SingleOption_UnknownValue_Fails(FormControlType type)
    {
        var error = FormFieldValidator.Validate(type, false, "urgente", Options);
        Assert.Equal("Selecciona una opcion valida.", error);
    }

    [Theory]
    [InlineData("alta")]
    [InlineData("BAJA")] // tambien matchea por Value, no solo por Id
    public void SingleOption_KnownIdOrValue_Passes(string value)
    {
        Assert.Null(FormFieldValidator.Validate(FormControlType.Select, false, value, Options));
    }

    [Fact]
    public void MultiCheck_AllKnownOptions_Passes()
    {
        Assert.Null(FormFieldValidator.Validate(
            FormControlType.MultiCheck, true, """["alta","media"]""", Options));
    }

    [Fact]
    public void MultiCheck_ContainsUnknownOption_Fails()
    {
        var error = FormFieldValidator.Validate(
            FormControlType.MultiCheck, false, """["alta","inexistente"]""", Options);
        Assert.Equal("Hay opciones seleccionadas que no son validas.", error);
    }

    [Fact]
    public void Select_WithoutOptionsDefined_Fails()
    {
        var error = FormFieldValidator.Validate(FormControlType.Select, false, "alta", options: []);
        Assert.Equal("Selecciona una opcion valida.", error);
    }

    // ---- Parsers ----

    [Fact]
    public void ParseOptions_InvalidJson_ReturnsEmpty()
    {
        Assert.Empty(FormFieldValidator.ParseOptions("{no-json"));
    }

    [Fact]
    public void ParseRules_CamelCaseJson_Binds()
    {
        var rules = FormFieldValidator.ParseRules(
            """{"minLength":2,"maxLength":10,"pattern":"^a","minValue":1,"maxValue":9}""");
        Assert.NotNull(rules);
        Assert.Equal(2, rules!.MinLength);
        Assert.Equal(10, rules.MaxLength);
        Assert.Equal("^a", rules.Pattern);
        Assert.Equal(1m, rules.MinValue);
        Assert.Equal(9m, rules.MaxValue);
    }
}
