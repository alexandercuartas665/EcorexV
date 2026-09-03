using System.Text.Json;

namespace Ecorex.Application.Reporting.External;

/// <summary>Serializacion de la lista de parametros/campos de un ExternalDataSet (columnas jsonb/text).
/// Aislada aqui para un solo formato estable compartido por servicio, reader y UI.</summary>
public static class ExternalDataJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static string SerializeParameters(IReadOnlyList<ExternalDataSetParameter> parameters) =>
        JsonSerializer.Serialize(parameters, Options);

    public static IReadOnlyList<ExternalDataSetParameter> DeserializeParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ExternalDataSetParameter>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ExternalDataSetParameter>>(json, Options)
                ?? new List<ExternalDataSetParameter>();
        }
        catch (JsonException)
        {
            return Array.Empty<ExternalDataSetParameter>();
        }
    }

    /// <summary>Tokens validos del enum <see cref="ExternalDataParameterType"/> (para validar/avisar).</summary>
    public static readonly IReadOnlyList<string> ValidTypeTokens =
        new[] { "String", "Int", "Decimal", "Date", "Boolean", "Guid" };

    /// <summary>Tokens validos del enum <see cref="ExternalDataParameterBinding"/>.</summary>
    public static readonly IReadOnlyList<string> ValidBindingTokens = new[] { "Input", "Context", "RowLimit" };

    /// <summary>Valida el JSON de PARAMETROS: lista de objetos con Type valido (y Binding valido si viene).
    /// Devuelve el mensaje de error legible, o null si es valido. Evita el guardado silencioso vacio cuando
    /// alguien escribe tokens que no son del enum (p.ej. Int32/DateTime).</summary>
    public static string? ValidateParametersJson(string? json) => ValidateTokens(json, checkBinding: true);

    /// <summary>Valida el JSON de CAMPOS de salida: lista de objetos con Type valido. Error legible o null.</summary>
    public static string? ValidateFieldsJson(string? json) => ValidateTokens(json, checkBinding: false);

    private static string? ValidateTokens(string? json, bool checkBinding)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { return $"El JSON no es valido ({ex.Message})."; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return "El JSON debe ser una lista: [ { \"Name\": ..., \"Type\": ... } ].";
            }
            var badTypes = new List<string>();
            var badBindings = new List<string>();
            var index = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                index++;
                if (el.ValueKind != JsonValueKind.Object) { return $"El elemento #{index} no es un objeto."; }
                var type = ReadProp(el, "type");
                if (type is null || !ValidTypeTokens.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)))
                {
                    badTypes.Add(string.IsNullOrWhiteSpace(type) ? "(vacio)" : type);
                }
                if (checkBinding)
                {
                    var binding = ReadProp(el, "binding");
                    if (!string.IsNullOrWhiteSpace(binding)
                        && !ValidBindingTokens.Any(b => string.Equals(b, binding, StringComparison.OrdinalIgnoreCase)))
                    {
                        badBindings.Add(binding);
                    }
                }
            }
            var parts = new List<string>();
            if (badTypes.Count > 0)
            {
                parts.Add($"Type invalido: {string.Join(", ", badTypes.Distinct())}. Validos: {string.Join(", ", ValidTypeTokens)}.");
            }
            if (badBindings.Count > 0)
            {
                parts.Add($"Binding invalido: {string.Join(", ", badBindings.Distinct())}. Validos: {string.Join(", ", ValidBindingTokens)}.");
            }
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
    }

    private static string? ReadProp(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
            }
        }
        return null;
    }

    public static string SerializeFields(IReadOnlyList<ExternalDataSetField> fields) =>
        JsonSerializer.Serialize(fields, Options);

    public static IReadOnlyList<ExternalDataSetField> DeserializeFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ExternalDataSetField>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ExternalDataSetField>>(json, Options)
                ?? new List<ExternalDataSetField>();
        }
        catch (JsonException)
        {
            return Array.Empty<ExternalDataSetField>();
        }
    }
}
