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
    // Color del ENCABEZADO de la columna (hex). Null = sin color.
    string? Color = null,
    // Etiqueta de identidad de la columna. Solo se usa para columnas de campo de FORMULARIO
    // (key "form:{defId}:{code}"), donde no hay catalogo que provea el nombre; para las
    // incorporadas y los campos del tablero queda null (el nombre sale del catalogo).
    string? Label = null,
    // Color del CUERPO de la columna (celdas), independiente del encabezado. Null = sin tinte.
    string? CellColor = null,
    // Supra-titulo: columnas CONSECUTIVAS con el mismo Group se agrupan bajo un encabezado que
    // las abarca (colspan) en una fila superior de la cabecera. Null/"" = sin grupo.
    string? Group = null,
    // Ancho de la columna en px. Null = automatico (se ajusta al contenido). Cuando ALGUNA columna
    // trae ancho, la tabla pasa a layout fijo y las celdas recortan con elipsis. Se persiste por
    // tablero y se puede ajustar arrastrando el borde del encabezado en la vista Lista.
    int? Width = null);

/// <summary>
/// Detalle EXPANDIBLE de la vista Lista (ADR-0065): cada fila (tarea/cotizacion) se puede abrir para
/// mostrar las filas de un GridDetail de uno de sus formularios (p.ej. los items de una cotizacion).
/// Es UN solo grid por tablero (se elige el formulario + su campo grilla) y se configura CUALES de sus
/// columnas se ven. Null = sin detalle (la expansion cae a subtareas, comportamiento anterior).
/// </summary>
public sealed record TaskListDetailConfig(
    Guid FormDefId,        // definicion de formulario cuyo GridDetail alimenta el detalle
    string GridFieldCode,  // field_code del GridDetail (ej. "items")
    IReadOnlyList<TaskListDetailColumn> Columns);

/// <summary>Una columna del GridDetail elegida para el detalle. <c>Key</c> = id de columna del grid.</summary>
public sealed record TaskListDetailColumn(string Key, bool Visible = true, string? Title = null);

/// <summary>
/// Config de la vista Lista de un tablero (que columnas se ven, su orden, color y titulos). Se
/// guarda serializada como JSON en <c>TaskBoard.ListViewConfigJson</c> (jsonb PG / nvarchar(max)
/// SQL). Es COMPARTIDA por tablero: todos los que abren el tablero ven las mismas columnas. El
/// encabezado y la primera columna quedan fijos por CSS (no requieren configuracion).
/// </summary>
public sealed record TaskListViewConfig(
    IReadOnlyList<TaskListColumnConfig> Columns,
    // Detalle expandible opcional (GridDetail de un formulario). Aditivo: configs viejas parsean igual.
    TaskListDetailConfig? Detail = null)
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
