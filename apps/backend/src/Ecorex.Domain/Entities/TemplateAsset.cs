using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Recurso (imagen) de la galeria de plantillas de cotizacion. Entidad TENANT-SCOPED.
/// Las imagenes subidas (logos, hoteles, aerolineas) quedan disponibles para insertarlas en
/// cualquier plantilla HTML de la agencia mediante su URL.
/// </summary>
public class TemplateAsset : TenantEntity
{
    /// <summary>Nombre original/visible del archivo.</summary>
    public string FileName { get; set; } = null!;

    /// <summary>Ruta servible del recurso (/uploads/templates/...).</summary>
    public string Url { get; set; } = null!;

    public string? MimeType { get; set; }

    public long SizeBytes { get; set; }
}
