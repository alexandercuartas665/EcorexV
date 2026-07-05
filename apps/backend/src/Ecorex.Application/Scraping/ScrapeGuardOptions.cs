namespace Ecorex.Application.Scraping;

/// <summary>
/// Limites de seguridad del ejecutor de extraccion (ADR-0025). Valores unicos para todo el
/// sistema; NO son configurables por tenant (un SaaS multi-tenant no puede dejar que un
/// tenant relaje el guard SSRF de la maquina compartida).
/// </summary>
public sealed record ScrapeGuardOptions
{
    /// <summary>
    /// Excepcion EXPLICITA para loopback (127.0.0.0/8, ::1, localhost), pensada SOLO para
    /// Development: permite ejecutar la fuente demo contra el endpoint propio de la app
    /// (/api/demo/scrape-sample) sin depender de internet. En produccion DEBE ser false:
    /// loopback es el vector SSRF clasico contra la propia app y sus servicios internos.
    /// </summary>
    public bool AllowLoopback { get; init; }

    /// <summary>Tamano maximo de la respuesta HTTP (bytes). Por encima, la corrida falla.</summary>
    public int MaxResponseBytes { get; init; } = 2 * 1024 * 1024;

    /// <summary>Redirecciones maximas; CADA salto se re-valida contra el guard SSRF.</summary>
    public int MaxRedirects { get; init; } = 3;

    /// <summary>Timeout total de la corrida HTTP (DNS + conexion + descarga).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>User-Agent propio e identificable (no suplanta navegadores).</summary>
    public string UserAgent { get; init; } = "EcorexScraper/1.0 (+https://ecorex.tareas; extraccion-datos)";

    /// <summary>Tope del documento ResultJson persistido (se recorta la preview, no los bytes).</summary>
    public int MaxResultJsonBytes { get; init; } = 64 * 1024;

    /// <summary>Items maximos incluidos en la preview del resultado.</summary>
    public int MaxPreviewItems { get; init; } = 20;
}
