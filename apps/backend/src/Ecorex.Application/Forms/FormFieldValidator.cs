using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms;

/// <summary>
/// Validacion SERVIDOR de un valor contra la definicion de su pregunta (ADR-0015).
/// Es la fuente de verdad: el renderer la reutiliza para la validacion cliente inmediata,
/// pero el submit SIEMPRE re-valida aqui (FormResponseService). Sin estado ni EF: puro,
/// para poder unit-testearlo por tipo de control.
/// </summary>
public static class FormFieldValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Controles que NO capturan datos (estructura/visuales): nunca validan valor.</summary>
    public static bool IsNonInput(FormControlType type)
        => type is FormControlType.Heading or FormControlType.Literal
            or FormControlType.Button or FormControlType.Chart or FormControlType.Html;

    /// <summary>Controles Tier 1 con componente en el DynamicFormRenderer.</summary>
    public static bool IsTier1(FormControlType type)
        => type <= FormControlType.Literal;

    /// <summary>Opciones de la pregunta ([{id,label,value}]); lista vacia si el JSON es nulo o invalido.</summary>
    public static IReadOnlyList<FormOption> ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<FormOption>>(optionsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Reglas de validacion (minLength, maxLength, pattern, minValue, maxValue).</summary>
    public static FormValidationRules? ParseRules(string? validationJson)
    {
        if (string.IsNullOrWhiteSpace(validationJson))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<FormValidationRules>(validationJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Valores seleccionados de un MultiCheck (el value del campo es un arreglo JSON de ids).</summary>
    public static IReadOnlyList<string> ParseMultiValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Valida un valor contra su pregunta. Devuelve el mensaje de error o null si es valido.
    /// El pattern invalido (no compilable) se ignora aqui: FormDefinitionService lo rechaza
    /// al GUARDAR la pregunta, que es donde se puede corregir.
    /// </summary>
    public static string? Validate(
        FormControlType type, bool required, string? value,
        IReadOnlyList<FormOption>? options = null, FormValidationRules? rules = null)
    {
        if (IsNonInput(type))
        {
            return null;
        }

        var isEmpty = type == FormControlType.MultiCheck
            ? ParseMultiValues(value).Count == 0
            : string.IsNullOrWhiteSpace(value);
        if (isEmpty)
        {
            return required ? "Este campo es obligatorio." : null;
        }

        return type switch
        {
            FormControlType.Text or FormControlType.TextArea => ValidateText(value!, rules),
            FormControlType.Number => ValidateNumber(value!, rules),
            FormControlType.Date => ValidateDate(value!),
            FormControlType.Toggle => ValidateToggle(value!),
            FormControlType.Select or FormControlType.Radio => ValidateOption(value!, options),
            FormControlType.MultiCheck => ValidateMulti(value!, options),
            // Tiers sin componente aun (Image, Photo, ...): no se captura valor por UI; si
            // llega algo (import externo), se acepta tal cual.
            _ => null
        };
    }

    private static string? ValidateText(string value, FormValidationRules? rules)
    {
        if (rules is null)
        {
            return null;
        }
        if (rules.MinLength is int min && value.Length < min)
        {
            return $"Minimo {min} caracteres.";
        }
        if (rules.MaxLength is int max && value.Length > max)
        {
            return $"Maximo {max} caracteres.";
        }
        if (!string.IsNullOrEmpty(rules.Pattern))
        {
            try
            {
                if (!Regex.IsMatch(value, rules.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250)))
                {
                    return "El valor no tiene el formato esperado.";
                }
            }
            catch (ArgumentException)
            {
                // Pattern invalido: se ignora (se valida al guardar la pregunta).
            }
            catch (RegexMatchTimeoutException)
            {
                return "El valor no tiene el formato esperado.";
            }
        }
        return null;
    }

    private static string? ValidateNumber(string value, FormValidationRules? rules)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return "Ingresa un numero valido.";
        }
        if (rules?.MinValue is decimal min && number < min)
        {
            return $"El valor minimo es {min.ToString(CultureInfo.InvariantCulture)}.";
        }
        if (rules?.MaxValue is decimal max && number > max)
        {
            return $"El valor maximo es {max.ToString(CultureInfo.InvariantCulture)}.";
        }
        return null;
    }

    private static string? ValidateDate(string value)
        => DateOnly.TryParse(value, CultureInfo.InvariantCulture, out _)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? null
            : "Ingresa una fecha valida.";

    private static string? ValidateToggle(string value)
        => bool.TryParse(value, out _) ? null : "Valor de interruptor invalido.";

    private static string? ValidateOption(string value, IReadOnlyList<FormOption>? options)
        => options is { Count: > 0 } && options.Any(o => OptionMatches(o, value))
            ? null
            : "Selecciona una opcion valida.";

    private static string? ValidateMulti(string value, IReadOnlyList<FormOption>? options)
    {
        var selected = ParseMultiValues(value);
        if (options is not { Count: > 0 })
        {
            return "Selecciona una opcion valida.";
        }
        return selected.All(s => options.Any(o => OptionMatches(o, s)))
            ? null
            : "Hay opciones seleccionadas que no son validas.";
    }

    private static bool OptionMatches(FormOption option, string value)
        => string.Equals(option.Id, value, StringComparison.Ordinal)
            || string.Equals(option.Value, value, StringComparison.Ordinal);
}
