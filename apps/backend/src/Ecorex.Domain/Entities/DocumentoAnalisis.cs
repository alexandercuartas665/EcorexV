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

    /// <summary>Estado del documento (Cargado -> EnAnalisis -> Analizado; Error si fallo la carga o el analisis).</summary>
    public DocumentoAnalisisEstado Estado { get; set; } = DocumentoAnalisisEstado.Cargado;

    /// <summary>Motivo del fallo cuando <see cref="Estado"/> es Error (null en el camino feliz).</summary>
    public string? MotivoError { get; set; }

    // --- Resultado del analisis (fase 2: separacion por IA). Se llenan al pasar a Analizado. ---

    /// <summary>Agente de IA que proceso el documento (referencia al modulo de Agentes). Null si no se ha procesado.</summary>
    public Guid? ProcesadoPorAgentId { get; set; }

    /// <summary>
    /// Version del agente usada: fecha del snapshot activo del agente (AiAgent.PromptHistoryJson[0].SavedAt) al
    /// momento de procesar. Deja trazado QUE version del prompt/agente separo el documento. Null si el agente no
    /// tenia historial.
    /// </summary>
    public DateTimeOffset? AgenteVersionAt { get; set; }

    /// <summary>Motor de IA usado (proveedor del agente: DeepSeek, Gemini, etc.).</summary>
    public AiProvider? Proveedor { get; set; }

    /// <summary>Modelo concreto del proveedor usado (p. ej. deepseek-v4-flash-vision-exp).</summary>
    public string? Modelo { get; set; }

    /// <summary>Tokens de entrada consumidos por el analisis (incluye las imagenes de las paginas).</summary>
    public int? InputTokens { get; set; }

    /// <summary>Tokens de salida consumidos por el analisis.</summary>
    public int? OutputTokens { get; set; }

    /// <summary>Tiempo total de procesamiento en milisegundos (cronometrado alrededor del pipeline completo).</summary>
    public long? ProcesamientoMs { get; set; }

    /// <summary>Momento en que termino el analisis (UTC). Null si no se ha procesado.</summary>
    public DateTimeOffset? ProcesadoAt { get; set; }

    /// <summary>Numero de paquetes en que se separo el documento. Null si no se ha procesado.</summary>
    public int? PaquetesCount { get; set; }

    /// <summary>JSON crudo que devolvio el agente (para depurar/re-separar). longtext/jsonb. Lo "que penso" el agente.</summary>
    public string? ResultadoJson { get; set; }

    /// <summary>Prompt efectivo que se le envio al agente (system prompt + bloque de campos). Lo "que recibio" el agente,
    /// util para depurar cuando el analisis falla. No incluye las imagenes de las paginas. longtext.</summary>
    public string? PromptEnviado { get; set; }

    /// <summary>Numero de paginas (imagenes) que se enviaron al agente. Deja trazado el tamano real del lote analizado.</summary>
    public int? PaginasEnviadas { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByTenantUserId { get; set; }
}
