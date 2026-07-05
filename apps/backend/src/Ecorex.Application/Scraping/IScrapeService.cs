namespace Ecorex.Application.Scraping;

/// <summary>
/// Extraccion de datos / web scraping acotado (modulo 000730, ADR-0025). Tenant-scoped.
/// CRUD de fuentes declarativas (URL + tipo + selector CSS) y ejecucion bajo demanda:
/// GET seguro (guard SSRF, 15s, 2 MB) + parseo (JSON o selector CSS) + historial
/// persistido de CADA corrida (exito o fallo). Sin schedulers en esta ola (TODO ADR-0025).
/// </summary>
public interface IScrapeService
{
    /// <summary>Fuentes del tenant con metricas (conteos de corridas, exito 30d, items totales).</summary>
    Task<IReadOnlyList<ScrapeSourceDto>> ListSourcesAsync(CancellationToken cancellationToken = default);

    Task<ScrapeSourceDto?> GetSourceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea o actualiza una fuente. Valida: nombre obligatorio, URL absoluta http(s) sin
    /// credenciales embebidas (el guard SSRF completo con DNS corre en cada ejecucion),
    /// selector CSS obligatorio para Kind=Html y nombre unico dentro del tenant.
    /// </summary>
    Task<ScrapeOpResult<ScrapeSourceDto>> SaveSourceAsync(SaveScrapeSourceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina una fuente SIN corridas. Con historial devuelve Invalid (mismo criterio que
    /// las reglas, ADR-0023): la UI ofrece desactivarla para conservar la trazabilidad.
    /// </summary>
    Task<ScrapeOpResult<bool>> DeleteSourceAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Historial de corridas de una fuente (mas recientes primero, con ResultJson).</summary>
    Task<IReadOnlyList<ScrapeRunDto>> ListRunsAsync(Guid sourceId, int take = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta la fuente AHORA: guard SSRF -> GET acotado -> parseo por tipo. SIEMPRE
    /// persiste la corrida (Success o Failed) y actualiza LastRunAt / LastResultSummary /
    /// Status de la fuente, en una transaccion. Devuelve la corrida persistida (el fallo
    /// de red/parseo NO es NotFound/Invalid: es una corrida Failed con su motivo).
    /// </summary>
    Task<ScrapeOpResult<ScrapeRunDto>> RunAsync(Guid sourceId, CancellationToken cancellationToken = default);
}
