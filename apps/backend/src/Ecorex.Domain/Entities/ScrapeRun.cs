using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Corrida de una fuente de extraccion (historial del modulo 000730, ADR-0025). El legacy
/// no tenia bitacora de ejecucion (riesgo documentado en la spec); aqui TODA corrida se
/// persiste, exitosa o fallida, con duracion, conteo y el resultado recortado.
/// </summary>
public class ScrapeRun : TenantEntity
{
    public Guid SourceId { get; set; }
    public ScrapeSource? Source { get; set; }

    public ScrapeRunStatus Status { get; set; }

    /// <summary>Items extraidos (elementos JSON o nodos que casaron con el selector CSS).</summary>
    public int ItemCount { get; set; }

    /// <summary>Duracion total de la corrida (validacion SSRF + GET + parseo) en milisegundos.</summary>
    public int DurationMs { get; set; }

    /// <summary>Motivo del fallo (solo Status=Failed). Nunca incluye el cuerpo de la respuesta.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Resultado estructurado de la corrida (documento JSON valido SIEMPRE: jsonb en PG lo
    /// exige). Recortado a ScrapeGuardOptions.MaxResultJsonBytes (64 KB) reduciendo la
    /// preview de items, nunca truncando bytes crudos.
    /// </summary>
    public string? ResultJson { get; set; }
}
