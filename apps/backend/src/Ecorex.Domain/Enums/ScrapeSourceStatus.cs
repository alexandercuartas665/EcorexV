namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de una fuente de extraccion (legacy WEB_SCRAPING.ESTADO: ACTIVO/DESACTIVADO/ERROR).
/// </summary>
public enum ScrapeSourceStatus
{
    /// <summary>Fuente activa: se puede ejecutar.</summary>
    Active = 0,

    /// <summary>Fuente desactivada por el usuario (no se elimina si tiene historial).</summary>
    Inactive = 1,

    /// <summary>La ultima ejecucion fallo; vuelve a Active con la proxima corrida exitosa.</summary>
    Error = 2
}
