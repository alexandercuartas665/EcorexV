using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Un paquete resultante de SEPARAR un <see cref="DocumentoAnalisis"/>: el PDF unido (varios soportes de
/// pago en secuencia) se parte en N paquetes, uno por beneficiario/pago. Cada paquete es un PDF nuevo
/// (guardado via IFileStorageService, llave en <see cref="StoredFileId"/>) mas la metadata que extrajo el
/// agente. Para el tab "Procesados": un registro padre (DocumentoAnalisis) con sus paquetes hijos.
///
/// Multi-tenant por el filtro global (TenantEntity) y con borrado logico (ISoftDeletable): al eliminar el
/// documento padre, sus paquetes tambien se marcan borrados; el binario se borra aparte.
/// </summary>
public sealed class DocumentoAnalisisPaquete : TenantEntity, ISoftDeletable
{
    /// <summary>Documento padre (el PDF unido) del que salio este paquete.</summary>
    public Guid DocumentoAnalisisId { get; set; }

    /// <summary>Orden del paquete dentro del documento (1-based), en el orden original de las paginas.</summary>
    public int Orden { get; set; }

    /// <summary>Nombre sugerido por el agente para el archivo separado (p. ej. beneficiario + valor).</summary>
    public string NombreSugerido { get; set; } = string.Empty;

    /// <summary>Llave del PDF separado en el almacenamiento (StoredFile.Id). Se sirve por /archivo/{StoredFileId}.</summary>
    public Guid StoredFileId { get; set; }

    /// <summary>Primera pagina (1-based) del documento unido que compone este paquete.</summary>
    public int PaginaInicio { get; set; }

    /// <summary>Ultima pagina (1-based) del documento unido que compone este paquete.</summary>
    public int PaginaFin { get; set; }

    /// <summary>Cantidad de paginas del paquete.</summary>
    public int Paginas { get; set; }

    /// <summary>Tamano en bytes del PDF separado.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Confianza del agente en este corte (0..1), si la reporto.</summary>
    public double? Confianza { get; set; }

    /// <summary>Metadata extraida por el agente, como objeto JSON keyed por la clave del campo configurado. longtext/jsonb.</summary>
    public string? MetadataJson { get; set; }

    /// <summary>Desglose de documentos detectados dentro del paquete (tipo + paginas), como JSON. longtext/jsonb.</summary>
    public string? DocumentosJson { get; set; }

    // --- Revision humana del resultado (QA): el usuario marca cada soporte como Correcto o Incorrecto. ---

    /// <summary>Estado de revision del paquete (Pendiente por defecto; Correcto/Incorrecto cuando el usuario lo marca).</summary>
    public PaqueteRevisionEstado RevisionEstado { get; set; } = PaqueteRevisionEstado.Pendiente;

    /// <summary>Momento (UTC) en que se marco la revision. Null si sigue Pendiente.</summary>
    public DateTimeOffset? RevisadoAt { get; set; }

    /// <summary>Quien marco la revision (nombre o correo del usuario). Null si sigue Pendiente.</summary>
    public string? RevisadoPor { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
