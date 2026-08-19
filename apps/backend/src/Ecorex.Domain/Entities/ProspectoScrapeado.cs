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

    /// <summary>Direccion/ubicacion del negocio (texto libre). Se captura del scraping (ficha de Maps).</summary>
    public string? Direccion { get; set; }

    /// <summary>Sitio web PROPIO del negocio (URL http/https). NO confundir con <see cref="OrigenUrl"/>
    /// (la ficha en Maps). Se filtra a http/https en la ingesta.</summary>
    public string? SitioWeb { get; set; }

    /// <summary>Foto/logo del contacto (URL http/https). Se pinta como avatar circular en la Bolsa;
    /// si es null cae a las iniciales.</summary>
    public string? ImagenUrl { get; set; }

    /// <summary>URL de donde el agente localizo el contacto (ficha/pagina). Se muestra como enlace
    /// "ver origen". Solo http/https (se filtra en la ingesta).</summary>
    public string? OrigenUrl { get; set; }

    /// <summary>Frase efectiva con que se encontro el contacto (ej. "centros medicos Bogota Colombia",
    /// o "LinkedIn: &lt;empresa&gt;" en el enriquecimiento). La setea el runner al ingerir; al promover
    /// se copia a la ficha Base (frase_busqueda) para trazabilidad del origen.</summary>
    public string? FraseBusqueda { get; set; }

    /// <summary>JSON crudo del scraper (portabilidad). jsonb en PG.</summary>
    public string? DataJson { get; set; }

    /// <summary>Tercero al que se promovio (null = aun no calificado).</summary>
    public Guid? TerceroId { get; set; }
    public Tercero? Tercero { get; set; }

    /// <summary>Enriquecimiento Maps -&gt; LinkedIn: FK (self) al prospecto-EMPRESA del que salio esta
    /// persona. Es el amarre FUERTE por dato (no por nombre): al promover la persona se garantiza el
    /// Tercero-empresa (promocion encadenada) y se liga por <see cref="Tercero.EmpresaId"/>. Null = no
    /// proviene de enriquecimiento por empresa.</summary>
    public Guid? EmpresaProspectoId { get; set; }
    public ProspectoScrapeado? EmpresaProspecto { get; set; }

    public DateTimeOffset? FechaCaptura { get; set; }
}
