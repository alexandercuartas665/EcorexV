using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Configuracion de UNA columna de la vista Lista de un tablero (Ola 2 de ADR-0065). <c>Key</c>
/// identifica el dato: las columnas incorporadas llevan prefijo "@" (ej. "@title", "@due"); las
/// de un campo personalizado llevan su <c>FieldKey</c> tal cual. <c>Title</c>/<c>Subtitle</c>
/// sobrescriben el encabezado; <c>Color</c> es un acento hex opcional para la columna.
/// </summary>
public sealed record TaskListColumnConfig(
    string Key,
    bool Visible = true,
    string? Title = null,
    string? Subtitle = null,
    string? Color = null,
    // Etiqueta de identidad de la columna. Solo se usa para columnas de campo de FORMULARIO
    // (key "form:{defId}:{code}"), donde no hay catalogo que provea el nombre; para las
    // incorporadas y los campos del tablero queda null (el nombre sale del catalogo).
    string? Label = null);

/// <summary>
/// Config de la vista Lista de un tablero (que columnas se ven, su orden, color y titulos). Se
/// guarda serializada como JSON en <c>TaskBoard.ListViewConfigJson</c> (jsonb PG / nvarchar(max)
/// SQL). Es COMPARTIDA por tablero: todos los que abren el tablero ven las mismas columnas. El
/// encabezado y la primera columna quedan fijos por CSS (no requieren configuracion).
/// </summary>
public sealed record TaskListViewConfig(IReadOnlyList<TaskListColumnConfig> Columns)
{
    // Claves de las columnas INCORPORADAS (las que no son campos personalizados del tablero).
    public const string KeyTitle = "@title";
    public const string KeyNumber = "@number";
    public const string KeyStage = "@stage";     // columna del kanban (etapa)
    public const string KeyStatus = "@status";   // estado del TaskItem (Pendiente/Terminada...)
    public const string KeyAssignee = "@assignee";
    public const string KeyPriority = "@priority";
    public const string KeyProgress = "@progress";
    public const string KeyDue = "@due";
    public const string KeyStart = "@start";
    public const string KeyCreated = "@created";
    public const string KeyTags = "@tags";

    /// <summary>True si la clave es de una columna incorporada (prefijo "@"), no de un campo.</summary>
    public static bool IsBuiltin(string key) => key.StartsWith('@');

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static TaskListViewConfig? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var t = raw.TrimStart();
        if (t.Length == 0 || t[0] != '{') { return null; }
        try
        {
            var cfg = JsonSerializer.Deserialize<TaskListViewConfig>(t, JsonOpts);
            return cfg?.Columns is null ? null : cfg;
        }
        catch (JsonException) { return null; }
    }
}
