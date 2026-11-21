using System.Text.Json;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// ADR-0199: PASO DE CLASIFICACION con IA de una programacion de importacion. Se guarda como JSON en
/// <c>ImportProcess.ClassifyStepJson</c>. Despues de cada corrida exitosa el motor clasifica, lote a lote, las filas
/// de <see cref="TableId"/> cuya <see cref="ColumnaMarca"/> esta vacia (es decir, SOLO lo nuevo o lo que se limpio),
/// con la misma logica de <c>clasificar_lote</c> (ADR-0197).
/// </summary>
/// <param name="TableId">Tabla (DataContainer) a clasificar. Por defecto la tabla destino del conector.</param>
/// <param name="Agente">Id del agente de IA (se guarda el id, no el nombre, para que renombrarlo no rompa el paso).</param>
/// <param name="ColumnaClave">Columna que identifica la fila (ej. id_proceso).</param>
/// <param name="ColumnaMarca">Columna que marca la fila como clasificada (ej. clasificado_en); vacia = pendiente.</param>
/// <param name="ColumnasSalida">Columnas que el agente puede escribir (ej. sector).</param>
/// <param name="ColumnasEntrada">Columnas que se le envian al agente; null = todas menos marca y salidas.</param>
/// <param name="Limite">Filas por lote (1..50).</param>
/// <param name="MaxLotes">Tope de lotes por corrida (1..200): acota el consumo de tokens de cada disparo.</param>
public sealed record ImportClassifyStep(
    Guid TableId,
    string Agente,
    string ColumnaClave,
    string ColumnaMarca,
    IReadOnlyList<string> ColumnasSalida,
    IReadOnlyList<string>? ColumnasEntrada,
    int Limite = ImportClassifyStep.LimiteDefecto,
    int MaxLotes = ImportClassifyStep.MaxLotesDefecto)
{
    public const int LimiteDefecto = 50;
    public const int MaxLotesDefecto = 20;
    public const int MaxLotesTope = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>null si no hay paso o el JSON no es valido (un JSON roto no debe tumbar la importacion).</summary>
    public static ImportClassifyStep? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            var s = JsonSerializer.Deserialize<ImportClassifyStep>(json, Json);
            return s is null || s.TableId == Guid.Empty || string.IsNullOrWhiteSpace(s.Agente) || s.ColumnasSalida is null
                ? null
                : s;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
