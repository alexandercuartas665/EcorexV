using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Un documento recibido por el modulo "Analizador de documentos" (una fila por PDF). El binario
/// NO vive aqui: se guarda a traves de IFileStorageService (Azure Blob o disco) y esta fila guarda
/// solo los metadatos y la LLAVE del archivo almacenado (<see cref="StoredFileId"/>), ademas del
/// estado del documento. Asi la parrilla es propia del modulo y queda lista para que la fase de
/// analisis (extraccion/IA) agregue su resultado sin tocar el almacenamiento.
///
/// Multi-tenant por el filtro global (TenantEntity) y con borrado logico (ISoftDeletable): eliminar
/// un documento de la parrilla no borra el historico de golpe. El binario si se borra aparte.
/// </summary>
public sealed class DocumentoAnalisis : TenantEntity, ISoftDeletable
{
    /// <summary>Lote al que pertenece: agrupa los documentos subidos en una misma carga. Para filtrar la parrilla.</summary>
    public Guid LoteId { get; set; }

    /// <summary>Llave del archivo almacenado (StoredFile.Id). Se sirve/descarga por /archivo/{StoredFileId}.</summary>
    public Guid StoredFileId { get; set; }

    /// <summary>Nombre original/legible del archivo (para mostrar y al descargar).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Extension saneada (con punto, p. ej. ".pdf").</summary>
    public string Extension { get; set; } = string.Empty;

    /// <summary>MIME del contenido (p. ej. application/pdf).</summary>
    public string? ContentType { get; set; }

    /// <summary>Tamano en bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Estado del documento (Cargado en esta fase; Error si fallo la carga).</summary>
    public DocumentoAnalisisEstado Estado { get; set; } = DocumentoAnalisisEstado.Cargado;

    /// <summary>Motivo del fallo cuando <see cref="Estado"/> es Error (null en el camino feliz).</summary>
    public string? MotivoError { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
