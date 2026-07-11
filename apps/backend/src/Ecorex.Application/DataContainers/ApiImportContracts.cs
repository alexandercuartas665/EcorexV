namespace Ecorex.Application.DataContainers;

// ==== Importacion desde API REST (motor minimo, disparo manual) ====
// Un conector RestApi guarda endpoint + auth + credenciales cifradas. "Probe" hace el GET y
// descubre los campos (llaves del primer elemento del arreglo JSON). "Import" mapea cada
// elemento a una fila de una tabla del contenedor segun el mapeo columna->campo. Solo lectura
// del lado externo; las credenciales se descifran en el servidor y nunca se devuelven.

/// <summary>Resultado de sondear el endpoint de un conector: campos disponibles + una muestra.</summary>
public sealed record ApiProbeResult(
    bool Success,
    IReadOnlyList<string> Fields,
    int Count,
    string? DetectedArrayPath,
    string? SamplePretty,
    string? Error);

/// <summary>Estilo de paginacion del API. None = una sola llamada; Offset = desplazamiento
/// (ej. start/limit de Alegra); Page = numero de pagina incremental (ej. page/limit).</summary>
public enum PagingMode { None, Offset, Page }

/// <summary>Config de paginacion para recorrer TODAS las paginas. El motor incrementa el
/// desplazamiento (o el numero de pagina) reescribiendo esos parametros en el query string y
/// se detiene cuando una pagina trae menos de PageSize elementos, viene vacia o se alcanza MaxPages.</summary>
public sealed record ApiPaging(
    PagingMode Mode,
    string? OffsetParam,   // Offset: parametro de desplazamiento (ej. "start")
    string? PageParam,     // Page: parametro de numero de pagina (ej. "page")
    string? LimitParam,    // ambos: parametro de tamano de pagina (ej. "limit")
    int PageSize,
    int StartValue,        // valor inicial (Offset: 0; Page: 1 tipico)
    int MaxPages);

/// <summary>Peticion de importacion: conector origen, tabla destino y mapeo columnaId -> campo JSON.</summary>
public sealed record ApiImportRequest(
    Guid ConnectorId,
    Guid TargetContainerId,
    IReadOnlyDictionary<Guid, string> ColumnToField,
    string? ArrayPath = null,
    ApiPaging? Paging = null);

/// <summary>
/// Motor minimo de importacion desde API REST (disparo manual). Reusa DataImportResult.
/// Tenant-scoped por el filtro global; el conector y la tabla deben ser del tenant activo.
/// </summary>
public interface IApiImportService
{
    /// <summary>Hace el GET del conector y descubre los campos del primer elemento del arreglo JSON.
    /// arrayPath opcional (ruta con puntos) cuando el arreglo viene envuelto en un objeto.</summary>
    Task<ApiProbeResult> ProbeAsync(Guid connectorId, string? arrayPath = null, CancellationToken ct = default);

    /// <summary>Trae los datos del API y crea una fila por cada elemento, mapeando campos->columnas.</summary>
    Task<DataImportResult> ImportAsync(ApiImportRequest req, Guid actorUserId, CancellationToken ct = default);
}
