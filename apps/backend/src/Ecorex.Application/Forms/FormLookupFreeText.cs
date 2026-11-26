using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ecorex.Application.Forms;

/// <summary>
/// Lookup con TEXTO LIBRE (opt-in por campo): un campo de busqueda (Tercero, Contenedor, ...) que, si el usuario
/// escribe y NO elige una sugerencia, guarda lo escrito como valor del campo (p.ej. el nombre de un lead nuevo en
/// el formulario publico). Elegir una sugerencia sigue ganando (guarda el id + autollenado). El flag vive en el
/// OptionsJson de la pregunta como objeto: <c>{"allowFreeText": true}</c>. Un OptionsJson que no sea objeto (o
/// vacio) = apagado. El campo deja de garantizar un Guid: quien lo lea debe tratarlo como texto.
/// </summary>
public static class FormLookupFreeText
{
    /// <summary>Clave del flag dentro de OptionsJson.</summary>
    public const string Key = "allowFreeText";

    /// <summary>True si el OptionsJson del campo trae <c>"allowFreeText": true</c>.</summary>
    public static bool IsEnabled(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) { return false; }
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(Key, out var v)
                && v.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Devuelve el OptionsJson con el flag puesto o quitado, conservando las demas claves del objeto.
    /// Si el OptionsJson no era un objeto (vacio, arreglo, corrupto) se parte de un objeto nuevo. Sin claves
    /// restantes -> null.</summary>
    public static string? Set(string? optionsJson, bool enabled)
    {
        JsonObject obj;
        try
        {
            obj = (string.IsNullOrWhiteSpace(optionsJson) ? null : JsonNode.Parse(optionsJson) as JsonObject) ?? new JsonObject();
        }
        catch (JsonException)
        {
            obj = new JsonObject();
        }

        if (enabled) { obj[Key] = true; }
        else { obj.Remove(Key); }
        return obj.Count == 0 ? null : obj.ToJsonString();
    }
}
