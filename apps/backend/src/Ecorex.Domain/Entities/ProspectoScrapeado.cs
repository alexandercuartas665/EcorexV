using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Prospecto capturado por scraping (LinkedIn / Maps / Web...) que alimenta la pestana
/// "Prospectos scrapeados" del Gestor de Clientes (000740). Por ahora se siembra como datos
/// demo por tenant; la ingesta real (N8N / modulo 000730) se conecta despues. Al calificarlo se
/// promueve a un <see cref="Tercero"/> del Directorio (entra a la Bolsa como Sospechoso).
/// TENANT-SCOPED.
/// </summary>
public class ProspectoScrapeado : TenantEntity
{
    /// <summary>Fuente del scraping (LinkedIn, Maps, Web, Instagram, Facebook...).</summary>
    public string Fuente { get; set; } = "LinkedIn";

    public string NombreCompleto { get; set; } = null!;
    public string? Cargo { get; set; }
    public string? Empresa { get; set; }
    public string? Ciudad { get; set; }

    /// <summary>Metrica de la fuente (ej. "2.340 conexiones" o "4.9 estrellas - 89 resenas").</summary>
    public string? Metrica { get; set; }

    /// <summary>Etiqueta comercial (Hot / Calificado / Nuevo).</summary>
    public string? Badge { get; set; }

    public string? Telefono { get; set; }
    public string? Correo { get; set; }

    /// <summary>JSON crudo del scraper (portabilidad). jsonb en PG.</summary>
    public string? DataJson { get; set; }

    /// <summary>Tercero al que se promovio (null = aun no calificado).</summary>
    public Guid? TerceroId { get; set; }
    public Tercero? Tercero { get; set; }

    public DateTimeOffset? FechaCaptura { get; set; }
}
