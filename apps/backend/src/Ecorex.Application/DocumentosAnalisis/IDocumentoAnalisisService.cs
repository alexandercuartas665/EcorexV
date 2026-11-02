using Ecorex.Domain.Enums;

namespace Ecorex.Application.DocumentosAnalisis;

/// <summary>Un documento del Analizador tal como lo ve la parrilla (sin el binario).</summary>
public sealed record DocumentoAnalisisDto(
    Guid Id,
    Guid LoteId,
    Guid StoredFileId,
    string FileName,
    string Extension,
    string? ContentType,
    long SizeBytes,
    DocumentoAnalisisEstado Estado,
    string? MotivoError,
    DateTimeOffset CreatedAt)
{
    /// <summary>URL para ver/descargar el binario (proxy por el server; contenedor privado).</summary>
    public string Url => $"/archivo/{StoredFileId}";
}

/// <summary>Un archivo a subir: bytes ya leidos por la UI. El servicio valida y lo guarda.</summary>
public sealed record DocumentoAnalisisUploadItem(string FileName, string? ContentType, byte[] Content);

/// <summary>
/// Un agente configurado en el Analizador: referencia a un agente del tenant (nombre resuelto para la
/// UI) con su prompt principal de analisis y si es el agente activo del modulo.
/// </summary>
public sealed record DocumentoAnalisisAgenteDto(
    Guid AgentId, string AgentName, string? AgentRole, string PromptPrincipal, bool IsActive);

/// <summary>Un agente del tenant disponible para seleccionar en la config (viene del modulo de Agentes).</summary>
public sealed record DocumentoAnalisisAgenteOpcionDto(Guid AgentId, string AgentName, string? AgentRole);

/// <summary>Un campo de metadata configurable (self-serve): define que extrae el agente y la columna del modal.</summary>
public sealed record DocumentoAnalisisMetadataCampoDto(
    Guid Id, string Clave, string Etiqueta, string Tipo, string? Descripcion, int Orden, bool Activo);

/// <summary>Un paquete resultante de separar un documento (un PDF nuevo + su metadata + su revision QA).</summary>
public sealed record DocumentoAnalisisPaqueteDto(
    Guid Id, int Orden, string NombreSugerido, Guid StoredFileId,
    int PaginaInicio, int PaginaFin, int Paginas, long SizeBytes,
    double? Confianza, string? MetadataJson, string? DocumentosJson,
    PaqueteRevisionEstado RevisionEstado, DateTimeOffset? RevisadoAt, string? RevisadoPor)
{
    /// <summary>URL para ver/descargar el PDF separado (proxy por el server; contenedor privado).</summary>
    public string Url => $"/archivo/{StoredFileId}";
}

/// <summary>
/// Traza del analisis de un documento: lo que el agente RECIBIO (prompt + nro de paginas) y lo que DEVOLVIO
/// (respuesta cruda), mas motor/tokens/tiempo y el motivo de error. Para depurar cuando el analisis falla.
/// </summary>
public sealed record DocumentoAnalisisTrazaDto(
    Guid Id, string FileName, DocumentoAnalisisEstado Estado, string? MotivoError,
    string? AgenteNombre, DateTimeOffset? AgenteVersionAt, AiProvider? Proveedor, string? Modelo,
    int? InputTokens, int? OutputTokens, long? ProcesamientoMs, DateTimeOffset? ProcesadoAt,
    int? PaginasEnviadas, int? PaquetesCount, string? PromptEnviado, string? ResultadoJson);

/// <summary>Un documento ya procesado tal como lo ve el tab "Procesados" (cabecera + trazas del analisis).</summary>
public sealed record DocumentoAnalisisProcesadoDto(
    Guid Id, string FileName, DocumentoAnalisisEstado Estado, int? PaquetesCount,
    long? ProcesamientoMs, DateTimeOffset? ProcesadoAt, string? AgenteNombre, DateTimeOffset? AgenteVersionAt,
    AiProvider? Proveedor, string? Modelo, int? InputTokens, int? OutputTokens, string? MotivoError, DateTimeOffset CreatedAt);

/// <summary>
/// Modulo "Analizador de documentos" (fase 1: carga). Recibe PDFs, los guarda en el almacenamiento
/// unificado (Azure Blob o disco, via IFileStorageService) y deja una fila por documento para la
/// parrilla. Tenant-scoped por el filtro global (no se filtra por TenantId a mano) y con soft-delete.
/// El motor de analisis en si es fase siguiente; esta interfaz no lo expone todavia.
/// </summary>
public interface IDocumentoAnalisisService
{
    /// <summary>
    /// Valida el archivo (PDF, firma por bytes magicos, tamano), lo sube al almacenamiento y registra
    /// el documento dentro del lote dado. Todo en transaccion. Lanza si el archivo no es valido o la
    /// subida falla (en cuyo caso NO queda fila ni binario). Devuelve el documento registrado.
    /// </summary>
    Task<DocumentoAnalisisDto> SubirAsync(Guid loteId, DocumentoAnalisisUploadItem item, CancellationToken ct = default);

    /// <summary>Lista los documentos del tenant, opcionalmente acotados a un lote, mas reciente primero.</summary>
    Task<IReadOnlyList<DocumentoAnalisisDto>> ListarAsync(Guid? loteId = null, CancellationToken ct = default);

    /// <summary>Elimina el documento (soft-delete de la fila + borrado del binario). True si existia.</summary>
    Task<bool> EliminarAsync(Guid id, CancellationToken ct = default);

    // ---- Configuracion: agentes de IA del modulo (seleccion + prompt principal) ----

    /// <summary>
    /// Agentes del tenant disponibles para seleccionar en la config (del modulo de Agentes). Solo lectura;
    /// el Analizador NO crea ni edita agentes, solo referencia los existentes. Excluye los ya agregados.
    /// </summary>
    Task<IReadOnlyList<DocumentoAnalisisAgenteOpcionDto>> ListarAgentesDisponiblesAsync(CancellationToken ct = default);

    /// <summary>Agentes ya configurados en el Analizador (con su prompt y cual es el activo), nombre resuelto.</summary>
    Task<IReadOnlyList<DocumentoAnalisisAgenteDto>> ListarAgentesConfigAsync(CancellationToken ct = default);

    /// <summary>Agrega un agente existente a la config del modulo (prompt vacio). No-op si ya estaba.</summary>
    Task AgregarAgenteAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Guarda el prompt principal de analisis de un agente ya agregado.</summary>
    Task GuardarPromptAgenteAsync(Guid agentId, string prompt, CancellationToken ct = default);

    /// <summary>Marca un agente como el ACTIVO del modulo (desmarca los demas). Uno por tenant.</summary>
    Task MarcarAgenteActivoAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Quita un agente de la config del modulo (borrado real de la fila de config).</summary>
    Task<bool> QuitarAgenteAsync(Guid agentId, CancellationToken ct = default);

    // ---- Configuracion: campos de metadata (self-serve; se inyectan al prompt y son columnas del modal) ----

    /// <summary>Lista los campos de metadata configurados (ordenados). Incluye activos e inactivos para la config.</summary>
    Task<IReadOnlyList<DocumentoAnalisisMetadataCampoDto>> ListarMetadataCamposAsync(CancellationToken ct = default);

    /// <summary>Crea (id null) o actualiza un campo de metadata. La clave es unica por tenant.</summary>
    Task<DocumentoAnalisisMetadataCampoDto> GuardarMetadataCampoAsync(
        Guid? id, string clave, string etiqueta, string tipo, string? descripcion, int orden, bool activo, CancellationToken ct = default);

    /// <summary>Elimina un campo de metadata (borrado real). True si existia.</summary>
    Task<bool> EliminarMetadataCampoAsync(Guid id, CancellationToken ct = default);

    // ---- Procesamiento (separacion por IA, en background) ----

    /// <summary>
    /// Encola los documentos dados para analisis (los pasa a EnAnalisis). El worker los toma y los procesa.
    /// Valida que haya un agente activo configurado. Devuelve cuantos quedaron encolados.
    /// </summary>
    Task<int> EncolarAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>Ids de documentos del tenant actual en estado EnAnalisis sin procesar aun (para el worker).</summary>
    Task<IReadOnlyList<Guid>> ListarEnAnalisisIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Procesa UN documento de punta a punta: rasteriza, llama al agente de vision, separa el PDF en paquetes,
    /// guarda cada PDF y su metadata, y registra tiempo/tokens/agente/version/motor. Pasa el documento a
    /// Analizado o Error. Pensado para correr en el worker (su propio scope + tenant ambiente).
    /// </summary>
    Task ProcesarAsync(Guid documentoId, CancellationToken ct = default);

    /// <summary>Documentos ya procesados (o en proceso/error) del tenant, mas reciente primero, para el tab "Procesados".</summary>
    Task<IReadOnlyList<DocumentoAnalisisProcesadoDto>> ListarProcesadosAsync(CancellationToken ct = default);

    /// <summary>Paquetes de un documento procesado, en orden, para el modal de resultados.</summary>
    Task<IReadOnlyList<DocumentoAnalisisPaqueteDto>> ListarPaquetesAsync(Guid documentoId, CancellationToken ct = default);

    /// <summary>Traza del analisis de un documento (prompt enviado, respuesta cruda, motor/tokens/tiempo, error). Null si no existe.</summary>
    Task<DocumentoAnalisisTrazaDto?> ObtenerTrazaAsync(Guid documentoId, CancellationToken ct = default);

    /// <summary>Marca un paquete como Correcto/Incorrecto (o Pendiente) en la revision QA. revisadoPor = usuario que marca.</summary>
    Task MarcarPaqueteAsync(Guid paqueteId, PaqueteRevisionEstado estado, string? revisadoPor, CancellationToken ct = default);
}
