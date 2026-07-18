namespace Ecorex.Application.Forms;

/// <summary>
/// Ciclo de vida de las respuestas de formularios dinamicos (ADR-0015): borrador con
/// autosave, envio con VALIDACION SERVIDOR completa por tipo (errores por fieldCode) y,
/// si la respuesta esta vinculada a un paso de flujo (FormFlowLink Pending), completa el
/// paso via IWorkflowEngine.CompleteStepAsync en la misma transaccion logica.
/// </summary>
public interface IFormResponseService
{
    /// <summary>
    /// Borrador para (definicion, referencia): si existe uno Draft lo devuelve; si no, lo
    /// crea. Con reference null SIEMPRE crea un borrador nuevo (respuesta anonima suelta).
    /// La definicion debe estar Active.
    /// </summary>
    Task<FormResult<FormResponseDto>> GetOrCreateDraftAsync(Guid definitionId, string? reference, CancellationToken cancellationToken = default);

    Task<FormResponseDto?> GetAsync(Guid responseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ancla una respuesta YA ENVIADA a una referencia (el numero de la tarea) cuando esa tarea aun
    /// no existia al diligenciarla. Lo usa el arranque FORM-FIRST (Ola B1): el usuario llena el
    /// formulario, el servidor lo valida y SOLO entonces nace la actividad; recien ahi hay numero
    /// que anclar. Idempotente y no destructivo: si la respuesta ya tiene referencia, no la pisa.
    /// </summary>
    Task<FormResult<FormResponseDto>> SetReferenceAsync(Guid responseId, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda el documento de datos. Con submit=false (autosave) solo persiste; con
    /// submit=true valida TODO por tipo (required, min/max length, pattern, rango numerico,
    /// opcion valida, fecha valida) y devuelve ValidationFailed con errores por fieldCode
    /// si algo falla. Al enviar con FormFlowLink Pending: marca el link Completed y
    /// completa el paso del workflow (misma transaccion; rollback total si el motor falla).
    /// <paramref name="approvalResult"/> (opcional) es la DECISION capturada junto al formulario
    /// cuando el nodo tiene una compuerta adelante: se propaga a CompleteStep para que el motor
    /// resuelva el gateway (ADR-0037). Si el nodo no tiene compuerta adelante, se ignora.
    /// </summary>
    Task<FormResult<FormResponseDto>> SaveAsync(
        Guid responseId, IReadOnlyDictionary<string, FormFieldValue> data, bool submit,
        Guid? submittedByTenantUserId = null, string? approvalResult = null,
        IReadOnlyCollection<string>? hiddenFieldCodes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Formularios exigidos por los pasos current del flujo de una tarea: para cada paso
    /// cuyo nodo tenga WorkflowNodeForm, asegura (idempotente) el borrador de respuesta
    /// con Reference = numero de la tarea y su FormFlowLink, y los devuelve para la UI.
    /// </summary>
    Task<IReadOnlyList<TaskStepFormDto>> GetTaskStepFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default);

    /// <summary>Anula un registro transaccional confirmado (ola F3): Voided + motivo + auditoria; no libera el numero.</summary>
    Task<FormResult<FormResponseDto>> VoidAsync(Guid responseId, string reason, Guid? byTenantUserId = null, CancellationToken cancellationToken = default);

    /// <summary>Registros (respuestas enviadas) de una definicion, para la bandeja del formulario-modulo (ola F4).</summary>
    Task<IReadOnlyList<FormRecordListItemDto>> ListRecordsAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>Exporta los registros de la bandeja a Excel (.xlsx) con las columnas configuradas (ola F4). Null si no es modulo.</summary>
    Task<byte[]?> ExportRecordsXlsxAsync(Guid definitionId, CancellationToken cancellationToken = default);

    // ---- Maestro-detalle (ola F5, doc 01 D7) ----

    /// <summary>Registros hijos enlazados a un campo Subform del padre.</summary>
    Task<IReadOnlyList<FormRecordListItemDto>> ListChildrenAsync(Guid parentResponseId, string parentFieldCode, CancellationToken cancellationToken = default);

    /// <summary>Crea un registro hijo (borrador) de la definicion dada y lo enlaza al padre. Devuelve el id del hijo.</summary>
    Task<FormResult<Guid>> AddChildAsync(Guid parentResponseId, string parentFieldCode, Guid childDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>Quita el enlace de un hijo (el registro hijo se conserva, se desengancha del padre).</summary>
    Task<FormResult<bool>> UnlinkChildAsync(Guid parentResponseId, string parentFieldCode, Guid childResponseId, CancellationToken cancellationToken = default);
}
