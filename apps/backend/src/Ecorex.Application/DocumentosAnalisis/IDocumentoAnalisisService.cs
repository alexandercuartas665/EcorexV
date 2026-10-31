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
}
