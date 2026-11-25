using System.Text.Json;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Directorio;

/// <summary>
/// Valores de las FICHAS de un tercero (FichasJson: seccion -> campo -> valor) aplanados por FieldKey, para
/// filtrar por campos dinamicos fuera del Directorio (Gestor de contactos 000740: lista, conteo del filtro y
/// motor de acciones). Un criterio de ficha se escribe "f:{FieldKey}" para no chocar con los campos fijos
/// (ciudad, vendedor, ...). Puro: sin BD.
/// </summary>
public static class TerceroFichaValues
{
    /// <summary>Prefijo del campo de un criterio que apunta a un campo de ficha.</summary>
    public const string CampoPrefix = "f:";

    /// <summary>Campo de criterio para un FieldKey de ficha ("f:{fieldKey}").</summary>
    public static string CampoFor(string fieldKey) => CampoPrefix + fieldKey;

    /// <summary>FieldKey de un criterio de ficha ("f:sector_economico" -> "sector_economico"); null si el campo
    /// es uno fijo.</summary>
    public static string? FieldKeyOf(string? campo)
    {
        var c = (campo ?? string.Empty).Trim();
        return c.StartsWith(CampoPrefix, StringComparison.OrdinalIgnoreCase) && c.Length > CampoPrefix.Length
            ? c[CampoPrefix.Length..].Trim()
            : null;
    }

    /// <summary>Tipos de campo que se ofrecen como filtro: los que guardan un valor legible. Se excluyen los que
    /// guardan un Id (Lookup, DirectoryLookup, Asesor), las tablas y los separadores.</summary>
    public static bool IsFilterable(TerceroFieldType type)
        => type is not (TerceroFieldType.Separator or TerceroFieldType.Table or TerceroFieldType.Lookup
            or TerceroFieldType.DirectoryLookup or TerceroFieldType.Asesor);

    /// <summary>
    /// Aplana FichasJson a FieldKey -> valor (texto). Numeros y booleanos se pasan a texto; un arreglo se une con
    /// ", "; objetos anidados (tablas) se ignoran. Vacios se omiten. JSON vacio o corrupto -> null (el criterio
    /// de ficha simplemente no coincide; no tumba el listado).
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Flatten(string? fichasJson)
    {
        if (string.IsNullOrWhiteSpace(fichasJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(fichasJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return null; }
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var seccion in doc.RootElement.EnumerateObject())
            {
                if (seccion.Value.ValueKind != JsonValueKind.Object) { continue; }
                foreach (var campo in seccion.Value.EnumerateObject())
                {
                    var texto = Text(campo.Value);
                    if (!string.IsNullOrWhiteSpace(texto)) { result.TryAdd(campo.Name, texto.Trim()); }
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(Text).Where(t => !string.IsNullOrWhiteSpace(t))),
        _ => null
    };
}
