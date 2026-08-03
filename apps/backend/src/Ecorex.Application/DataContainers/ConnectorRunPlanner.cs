using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Traduce la configuracion PERSISTIDA de un conector REST (el <c>MappingJson</c>, que es el mismo
/// RestFetchSpec que consume el agente: arrayPath + paginacion + mapeo campo->columna) en una
/// <see cref="ApiImportRequest"/> lista para el importador server-direct (<see cref="IApiImportService"/>).
///
/// Vive en Application (no en la capa web) para poder unit-testearlo sin levantar la API ni referenciar
/// la libreria de contratos del agente: se parsea el JSON con una forma local minima (solo los campos
/// que el importador in-process necesita). El modo de re-carga (Append/Replace/Upsert) y la columna
/// clave son POLITICA de la corrida (llegan por la peticion de run), no parte de la definicion del
/// conector, de modo que el mismo conector se puede correr en distintos modos.
///
/// NOTA (alcance FASE 1): el importador server-direct mapea por NOMBRE de propiedad plano; las rutas
/// anidadas (ej. <c>id_type.name</c>, <c>phones[0].number</c>) se declaran y persisten aqui, pero solo
/// el runner via agente las aplana. Ver ADR-0058.
/// </summary>
public static class ConnectorRunPlanner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Resultado del planeamiento: la peticion lista, o un error de validacion legible.</summary>
    public readonly record struct PlanResult(ApiImportRequest? Request, string? Error)
    {
        public bool Ok => Request is not null && Error is null;
    }

    /// <summary>
    /// Construye la peticion de importacion.
    /// </summary>
    /// <param name="connectorId">Conector origen.</param>
    /// <param name="targetContainerId">Tabla destino (ContainerId del conector).</param>
    /// <param name="mappingJson">MappingJson persistido del conector (RestFetchSpec). Puede venir null.</param>
    /// <param name="targetColumnsByName">Columnas escalares de la tabla destino: nombre -> id.</param>
    /// <param name="mode">Modo de re-carga de la corrida.</param>
    /// <param name="keyColumnName">Nombre de la columna clave (solo para Upsert).</param>
    /// <param name="arrayPathOverride">Ruta del arreglo si la corrida la fuerza (gana sobre la del mapeo).</param>
    public static PlanResult Build(
        Guid connectorId,
        Guid targetContainerId,
        string? mappingJson,
        IReadOnlyDictionary<string, Guid> targetColumnsByName,
        ApiImportMode mode,
        string? keyColumnName,
        string? arrayPathOverride = null)
    {
        if (targetContainerId == Guid.Empty)
        {
            return new PlanResult(null, "El conector no tiene tabla destino (ContainerId).");
        }

        MappingShape? shape = null;
        if (!string.IsNullOrWhiteSpace(mappingJson))
        {
            try { shape = JsonSerializer.Deserialize<MappingShape>(mappingJson, Json); }
            catch (JsonException) { return new PlanResult(null, "El mapeo del conector (MappingJson) no es un JSON valido."); }
        }

        // Mapeo columnaId -> ruta/campo del JSON, resuelto por NOMBRE de columna (case-insensitive).
        var columnToField = new Dictionary<Guid, string>();
        if (shape?.Fields is { Count: > 0 } fields)
        {
            foreach (var f in fields)
            {
                if (f is null || string.IsNullOrWhiteSpace(f.Column) || string.IsNullOrWhiteSpace(f.Path)) { continue; }
                if (targetColumnsByName.TryGetValue(f.Column.Trim(), out var colId))
                {
                    columnToField[colId] = f.Path.Trim();
                }
            }
        }
        if (columnToField.Count == 0)
        {
            return new PlanResult(null,
                "El conector no tiene un mapeo campo->columna utilizable. Define 'fields' en el mapeo con columnas que existan en la tabla destino.");
        }

        Guid? keyColumnId = null;
        if (mode == ApiImportMode.Upsert)
        {
            if (string.IsNullOrWhiteSpace(keyColumnName))
            {
                return new PlanResult(null, "Para Upsert indica 'keyColumn' (nombre de la columna clave).");
            }
            if (!targetColumnsByName.TryGetValue(keyColumnName.Trim(), out var kId))
            {
                return new PlanResult(null, $"La columna clave '{keyColumnName}' no existe en la tabla destino.");
            }
            if (!columnToField.ContainsKey(kId))
            {
                return new PlanResult(null, $"La columna clave '{keyColumnName}' debe estar mapeada a un campo del API.");
            }
            keyColumnId = kId;
        }

        ApiPaging? paging = null;
        if (shape?.Paging is { } p && !string.Equals(p.Mode, "None", StringComparison.OrdinalIgnoreCase))
        {
            var pmode = string.Equals(p.Mode, "Page", StringComparison.OrdinalIgnoreCase) ? PagingMode.Page : PagingMode.Offset;
            paging = new ApiPaging(
                pmode,
                OffsetParam: string.IsNullOrWhiteSpace(p.OffsetParam) ? "start" : p.OffsetParam,
                PageParam: string.IsNullOrWhiteSpace(p.PageParam) ? "page" : p.PageParam,
                LimitParam: string.IsNullOrWhiteSpace(p.LimitParam) ? "limit" : p.LimitParam,
                PageSize: p.PageSize <= 0 ? 100 : p.PageSize,
                StartValue: p.StartValue,
                MaxPages: p.MaxPages <= 0 ? 1000 : p.MaxPages);
        }

        var arrayPath = !string.IsNullOrWhiteSpace(arrayPathOverride) ? arrayPathOverride!.Trim() : shape?.ArrayPath;

        var request = new ApiImportRequest(
            connectorId, targetContainerId, columnToField, arrayPath, paging, mode, keyColumnId);
        return new PlanResult(request, null);
    }

    // ---- Forma local minima del RestFetchSpec persistido (solo lo que el importador necesita) ----

    private sealed class MappingShape
    {
        [JsonPropertyName("arrayPath")] public string? ArrayPath { get; set; }
        [JsonPropertyName("paging")] public PagingShape? Paging { get; set; }
        [JsonPropertyName("fields")] public List<FieldShape>? Fields { get; set; }
    }

    private sealed class PagingShape
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "None";
        [JsonPropertyName("offsetParam")] public string? OffsetParam { get; set; }
        [JsonPropertyName("limitParam")] public string? LimitParam { get; set; }
        [JsonPropertyName("pageParam")] public string? PageParam { get; set; }
        [JsonPropertyName("startValue")] public int StartValue { get; set; }
        [JsonPropertyName("pageSize")] public int PageSize { get; set; }
        [JsonPropertyName("maxPages")] public int MaxPages { get; set; }
    }

    private sealed class FieldShape
    {
        [JsonPropertyName("column")] public string? Column { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
    }
}
