namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de contenido que extrae una fuente de datos (modulo 000730, ADR-0025).
/// Alcance acotado de esta ola: GET simple y parseo declarativo (sin scripts inyectados
/// como el legacy WEB_SCRAPING_RS.SCRIPT, que era RCE en el host del bot).
/// </summary>
public enum ScrapeSourceKind
{
    /// <summary>Pagina HTML: se extrae texto por selector CSS (AngleSharp).</summary>
    Html = 0,

    /// <summary>Endpoint JSON: se parsea el documento y se cuentan/previsualizan items.</summary>
    Json = 1
}
