using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Recurso (imagen) de la GALERIA DE PLANTILLAS del tenant. Entidad TENANT-SCOPED. Las imagenes subidas
/// (logos, fotos, banners) quedan disponibles para insertarlas en cualquier plantilla HTML (correos del
/// agente editor de plantillas, ADR-0177) mediante su URL publica.
/// Soft-delete: "Eliminar" la oculta de la galeria pero el binario se sigue sirviendo, para que los correos
/// YA ENVIADOS que la usan no queden con la imagen rota.
/// </summary>
public class TemplateAsset : TenantEntity, ISoftDeletable
{
    /// <summary>Nombre original/visible del archivo.</summary>
    public string FileName { get; set; } = null!;

    /// <summary>Ruta servible del recurso: /i/{archivo} (publica, para correos) o /uploads/templates/... (legado).</summary>
    public string Url { get; set; } = null!;

    public string? MimeType { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>Texto alternativo / descripcion (lo usa el agente como alt y para elegir la imagen).</summary>
    public string? Alt { get; set; }

    /// <summary>Ancho real en pixeles (si se pudo leer del archivo).</summary>
    public int? Width { get; set; }

    /// <summary>Alto real en pixeles (si se pudo leer del archivo).</summary>
    public int? Height { get; set; }

    /// <summary>Binario en el gestor unificado de archivos (StoredFile). Null en los recursos legados.</summary>
    public Guid? StoredFileId { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
