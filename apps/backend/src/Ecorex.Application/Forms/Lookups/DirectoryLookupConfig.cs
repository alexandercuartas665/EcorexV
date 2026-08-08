using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.DataLookups;

namespace Ecorex.Application.Forms.Lookups;

/// <summary>
/// Configuracion de un campo de tarea tipo "Lista del Directorio" (ADR-0065): una lista alimentada
/// por el Directorio General de terceros (modulo 000232). Reusa el motor de lookups de formularios
/// (<c>FormSourceKind.Tercero</c>) filtrando por perfil. Se guarda serializada como JSON en la
/// columna Options del campo, igual que el Lookup del Contenedor de datos: por eso NO requiere
/// columnas nuevas. El valor guardado es el Id del tercero (referencia viva): la etiqueta se
/// resuelve al mostrar, asi que corregir el tercero en el Directorio se refleja en la tarea.
/// </summary>
/// <param name="Perfil">Perfil de tercero a listar: "Cliente", "Proveedor", "Empleado" o
/// null/"" = todos los terceros del tenant.</param>
/// <param name="DisplayMode">Presentacion: buscador (Typeahead) o lista desplegable (List).</param>
public sealed record DirectoryLookupConfig(
    string? Perfil = null,
    DataLookupDisplayMode DisplayMode = DataLookupDisplayMode.Typeahead)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>
    /// Filtro para el motor de lookups del Directorio: acota el catalogo al perfil elegido
    /// (Cliente/Proveedor/Empleado). Null = sin filtro (todos los terceros del tenant). El perfil
    /// sale de una lista cerrada del configurador y ademas el adaptador lo valida con Enum.TryParse,
    /// asi que un valor desconocido simplemente se ignora (no rompe la busqueda).
    /// </summary>
    public string? ToFilterJson()
        => string.IsNullOrWhiteSpace(Perfil) ? null : $"{{\"perfil\":\"{Perfil}\"}}";

    /// <summary>
    /// Lee la config desde el texto guardado. Devuelve null si no es un JSON de directorio (p.ej.
    /// las opciones de un Select, una por linea). Un campo sin perfil (todos) tambien es valido.
    /// </summary>
    public static DirectoryLookupConfig? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var texto = raw.TrimStart();
        if (texto.Length == 0 || texto[0] != '{') { return null; }
        try
        {
            return JsonSerializer.Deserialize<DirectoryLookupConfig>(texto, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
