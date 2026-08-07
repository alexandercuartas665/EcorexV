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
