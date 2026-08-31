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

    /// <summary>
    /// Formulario del concepto (ActividadSubcategoria.FormDefinitionId) de la tarea + sus cotizaciones
    /// (todas las respuestas ancladas al numero de la tarea, como tarjetas). NO crea ninguna: la primera
    /// la crea el wizard y las demas se agregan con CreateTaskConceptFormAsync. Null si la tarea no tiene
    /// subcategoria, la subcategoria no define formulario, o el formulario no esta Active.
    /// </summary>
    Task<TaskConceptFormsDto?> GetTaskConceptFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Formularios de la tarea agrupados por GENERO (una definicion = un genero), para presentarlos y
    /// gestionarlos todos igual que los del concepto: el genero del concepto (si hay) + los generos del FLUJO
    /// (definiciones de WorkflowNodeForm de cualquier nodo; las de inicio siempre, las demas si tienen
    /// respuestas) + un catch-all de cualquier definicion con respuestas ancladas (p.ej. Orden de Trabajo
    /// derivada). Cada genero trae sus respuestas ("{numero}"/"{numero}-{n}") con un activo, EXCLUYENDO la
    /// respuesta del paso ACTUAL (esa se ve en "Formularios del proceso"). Vacio si la tarea no tiene generos.
    /// </summary>
    Task<IReadOnlyList<TaskConceptFormsDto>> GetTaskFormGenerosAsync(Guid taskItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Formularios del PRIMER PASO del flujo (evento de inicio) de una subcategoria, para que el wizard los
    /// ofrezca AL CREAR la actividad cuando el concepto no define formulario propio (ADR-0069). Vacio si la
    /// subcategoria no tiene flujo publicado, el inicio no tiene formularios, o estos no estan Active.
    /// </summary>
    Task<IReadOnlyList<CreationFlowFormDto>> GetSubcategoriaCreationFlowFormsAsync(Guid subcategoriaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una NUEVA cotizacion (respuesta en borrador) del formulario del concepto para la tarea,
    /// anclada a su numero. Para el boton "Agregar cotizacion". Error si la tarea no tiene formulario de
    /// concepto o esta Cerrada (Closed).
    /// </summary>
    Task<FormResult<TaskConceptFormItemDto>> CreateTaskConceptFormAsync(Guid taskItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una NUEVA respuesta (borrador) de un GENERO cualquiera (definitionId) para la tarea, anclada a su
    /// numero ("{numero}-{n}"). Generaliza CreateTaskConceptFormAsync al boton "+ Agregar" de cualquier genero
    /// (concepto o flujo). Error si la tarea no existe/esta Cerrada o la definicion no esta Active.
    /// </summary>
    Task<FormResult<TaskConceptFormItemDto>> CreateTaskFormAsync(Guid taskItemId, Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Duplica una respuesta (formulario) de la tarea: copia su Data en una nueva en borrador, con el
    /// siguiente numero heredado ("{numero tarea}-{n}"). Para el boton "Copiar" de la tarjeta. Error si la
    /// tarea esta Cerrada o el formulario no esta anclado a una tarea.
    /// </summary>
    Task<FormResult<TaskConceptFormItemDto>> DuplicateResponseAsync(Guid responseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca cual respuesta de la tarea es la ACTIVA por defecto (exclusivo por tarea: desmarca las
    /// demas del mismo conjunto de la tarea). Con una sola respuesta no hace falta: ya es la activa.
    /// </summary>
    Task<FormResult<bool>> SetActiveTaskFormAsync(Guid responseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// TRANSFORMACION (ADR-0078): crea un registro NUEVO de <paramref name="targetDefinitionId"/> a partir de
    /// <paramref name="sourceResponseId"/>, copiando los campos por field_code que el destino conozca (con el
    /// mapeo explicito {origen: destino} para los que cambian de nombre) y heredando el ANCLAJE a la tarea del
    /// origen (misma tarea, ordinal nuevo) o sin anclaje si el origen es suelto. Devuelve el id del nuevo
    /// registro (borrador). Lo usa el verbo CONVERTIR_A_FORMULARIO (p.ej. Cotizacion -> Orden de Trabajo).
    ///
    /// <paramref name="contextDefaults"/> (opcional, configurable en la regla): valores por defecto/transformacion
    /// { campoDestino: token } que RELLENAN campos del destino SIN origen (solo si quedan vacios). Un token que
    /// empieza por '@' se resuelve desde el contexto (p.ej. '@usuario.nombre' = nombre del usuario que ejecuta,
    /// '@usuario.email', '@fecha.hoy', '@fecha.hora'); cualquier otro valor se toma como constante literal.
    /// <paramref name="actorTenantUserId"/> es el TenantUser que dispara la conversion (para resolver '@usuario.*').
    /// </summary>
    Task<FormResult<Guid>> CreateDerivedFormAsync(
        Guid sourceResponseId, Guid targetDefinitionId,
        IReadOnlyDictionary<string, string>? fieldMapping,
        IReadOnlyDictionary<string, string>? contextDefaults = null,
        Guid? actorTenantUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Formularios DERIVADOS anclados a la tarea (ADR-0078): respuestas cuyo Reference es el numero de la
    /// tarea (o "{numero}-{n}") pero cuya definicion NO es la del concepto -- p.ej. una Orden de Trabajo
    /// creada por transformacion desde una cotizacion. Para mostrarlas como tarjetas extra en la pestana
    /// Formularios de la tarea. Excluye la definicion del concepto (esas ya salen por GetTaskConceptFormsAsync).
    /// </summary>
    Task<IReadOnlyList<TaskRelatedFormDto>> GetTaskRelatedFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// El registro DERIVADO de una respuesta (ADR-0078): si <paramref name="sourceResponseId"/> ya fue
    /// convertida (verbo CONVERTIR_A_FORMULARIO), devuelve el destino (Orden de Trabajo) con su numero; null si
    /// no. La UI lo usa para marcar la origen como "convertida" y para reabrir el derivado (idempotente).
    /// </summary>
    Task<DerivedFormRefDto?> GetDerivedRecordAsync(Guid sourceResponseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Formularios relevantes para un tablero (ADR-0065): el del concepto de sus tareas + los de los
    /// pasos de su flujo. Para el selector "Elijo un formulario" del configurador de columnas.
    /// </summary>
    Task<IReadOnlyList<BoardFormDto>> GetBoardFormsAsync(Guid boardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Para cada tarea del tablero y cada definicion pedida, el Data del formulario EFECTIVO
    /// (activo si es del concepto; si es de paso, la respuesta Submitted/mas reciente anclada a la
    /// tarea). Solo lectura, para pintar campos de formulario como columnas de la vista Lista.
    /// </summary>
    Task<IReadOnlyList<TaskFormDataDto>> GetBoardTaskFormValuesAsync(Guid boardId, IReadOnlyList<Guid> definitionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Campos GridDetail de los formularios del tablero (ADR-0065), con sus columnas, para elegir UNO
    /// como fuente del DETALLE expandible de la vista Lista (p.ej. los items de una cotizacion).
    /// </summary>
    Task<IReadOnlyList<BoardGridSourceDto>> GetBoardGridSourcesAsync(Guid boardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filas del GridDetail <paramref name="gridFieldCode"/> (del formulario EFECTIVO de cada tarea) para
    /// cada tarea del tablero. Solo lectura, para pintar el detalle expandible (los items) en la Lista.
    /// </summary>
    Task<IReadOnlyList<TaskGridRowsDto>> GetBoardTaskGridRowsAsync(Guid boardId, Guid formDefId, string gridFieldCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reabre una respuesta Finalizada (Submitted -> Draft) para volver a editarla ("Reabrir"). Guard:
    /// la tarea asociada no puede estar Cerrada (Closed). Sin efecto si ya es borrador.
    /// </summary>
    Task<FormResult<FormResponseDto>> ReopenResponseAsync(Guid responseId, CancellationToken cancellationToken = default);

    /// <summary>Anula un registro transaccional confirmado (ola F3): Voided + motivo + auditoria; no libera el numero.</summary>
    Task<FormResult<FormResponseDto>> VoidAsync(Guid responseId, string reason, Guid? byTenantUserId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// BORRA de verdad un registro (a diferencia de <see cref="VoidAsync"/>, que solo lo anula).
    /// Uso: quitar registros de prueba o cargados por error. Limpia en una transaccion los enlaces
    /// que lo referencian (maestro-detalle y notas de tercero) y libera su numero; los vinculos de
    /// flujo caen por cascada de BD. Irreversible: la UI debe confirmar.
    /// </summary>
    Task<FormResult<bool>> DeleteRecordAsync(Guid responseId, CancellationToken cancellationToken = default);

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
