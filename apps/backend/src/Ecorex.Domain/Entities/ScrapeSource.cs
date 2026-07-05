using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Fuente de extraccion de datos (modulo 000730 - EXTRACCION DE DATOS, ADR-0025).
/// Reencuadre seguro del maestro legacy WEB_SCRAPING: en lugar de scripts JS inyectados
/// en un WebBrowser, cada fuente declara una URL http(s) publica y, para HTML, un
/// selector CSS. La ejecucion es un GET acotado (15s, 2 MB) tras el guard anti-SSRF.
/// </summary>
public class ScrapeSource : TenantEntity
{
    /// <summary>Nombre visible de la fuente (ej. "Precios competencia Homecenter").</summary>
    public string Name { get; set; } = null!;

    /// <summary>URL absoluta http(s) a consultar. Se re-valida contra el guard SSRF en cada corrida.</summary>
    public string Url { get; set; } = null!;

    /// <summary>Selector CSS de extraccion (obligatorio para Kind=Html; ignorado en Json).</summary>
    public string? Selector { get; set; }

    /// <summary>Tipo de contenido: Html (selector CSS) o Json (conteo + preview).</summary>
    public ScrapeSourceKind Kind { get; set; } = ScrapeSourceKind.Json;

    /// <summary>Estado de la fuente (Active/Inactive/Error).</summary>
    public ScrapeSourceStatus Status { get; set; } = ScrapeSourceStatus.Active;

    /// <summary>Momento de la ultima corrida (exitosa o fallida).</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Resumen humano de la ultima corrida ("148 items en 320 ms" / "Fallo: ...").</summary>
    public string? LastResultSummary { get; set; }

    public ICollection<ScrapeRun> Runs { get; set; } = new List<ScrapeRun>();
}
