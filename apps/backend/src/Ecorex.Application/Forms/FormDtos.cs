using Ecorex.Domain.Enums;

namespace Ecorex.Application.Forms;

// ---- Formularios dinamicos (FASE 4, ADR-0015) ----

/// <summary>Opcion de un control Select/MultiCheck/Radio ([{id,label,value}] en OptionsJson).</summary>
public sealed record FormOption(string Id, string Label, string? Value = null);

/// <summary>Reglas de validacion declaradas en ValidationJson de la pregunta.</summary>
public sealed record FormValidationRules(
    int? MinLength = null, int? MaxLength = null, string? Pattern = null,
    decimal? MinValue = null, decimal? MaxValue = null);

/// <summary>Valor de un campo en el documento de respuesta: { fieldCode: { value, type } }.</summary>
public sealed record FormFieldValue(string? Value, string Type);

/// <summary>ResponseCount y RuleCount alimentan los KPIs del indice (ADR-0021).</summary>
public sealed record FormDefinitionListItemDto(
    Guid Id, string Code, string Title, string? Description, FormStatus Status,
    int Revision, bool IsArchived, int QuestionCount, long Version,
    int ResponseCount = 0, int RuleCount = 0);

public sealed record FormContainerDto(
    Guid Id, string Name, FormContainerType ContainerType, Guid? ParentId,
    int SortOrder, string? Style,
    string? TabsJson = null, int Width = 12, bool IsLocked = false, bool IsHidden = false,
    // Etiquetas en linea (label al frente del valor). Config-driven por contenedor (Row/Col).
    bool InlineLabels = false,
    // Acceso por cargo (ADR-0082): Guids de OrgUnit-Cargo autorizados a operar la seccion. Null/vacio = todos.
    string? AllowedCargosJson = null,
    // Visibilidad condicional de la seccion por valor de una pregunta ({field,op,value}). Null = siempre visible.
    string? VisibleWhenJson = null);

public sealed record FormQuestionDto(
    Guid Id, Guid? ContainerId, string FieldCode, string Label, string? Caption,
    string? HelpText, FormControlType ControlType, string? OptionsJson, bool Required,
    int SortOrder, string GridCol, string? Numeral, string? ValidationJson,
    int Width = 12, string? PlaceholderText = null, string? DefaultValue = null,
    bool IsLocked = false, bool IsHidden = false,
    // Origen de datos / lookup (ola F1, doc 01 D4).
    FormSourceKind SourceKind = FormSourceKind.Options, string? SourceRef = null,
    string? DisplayField = null, string? ValueField = null, string? FilterJson = null,
    string? AutofillMapJson = null, FormFieldPresentation Presentation = FormFieldPresentation.Autocomplete,
    // Calculo / agregacion (ola F2, doc 01 D5).
    string? CalcExpression = null, FormAggregate Aggregate = FormAggregate.None,
    // Maestro-detalle (ola F5, doc 01 D7): definicion hija del campo Subform.
    Guid? SubformDefinitionId = null,
    // Transversales (ola F6, doc 01 D8): default dinamico + formato + permisos por campo.
    FormDefaultDynamic DefaultDynamic = FormDefaultDynamic.None, string? Format = null,
    string? FieldVisibilityJson = null,
    // Configurador en cascada (motor generico): taxonomia de la pregunta CascadeConfigurator.
    string? CascadeConfigJson = null,
    // Visibilidad condicional por valor de otra pregunta ({field,op,value}). Null = siempre visible.
    string? VisibleWhenJson = null);

public sealed record FormDefinitionDetailDto(
    Guid Id, string Code, string Title, string? Description, FormStatus Status,
    int Revision, bool IsArchived, long Version,
    IReadOnlyList<FormContainerDto> Containers,
    IReadOnlyList<FormQuestionDto> Questions,
    // Transaccionalidad (ola F3, doc 01 D2/D3).
    bool IsTransactional = false, FormIdentityMode IdentityMode = FormIdentityMode.None,
    string? IdentitySourceFieldCode = null,
    // Formulario como modulo (ola F4, doc 01 D1).
    bool IsModule = false, string? ModuleIcon = null,
    string? ListColumnsJson = null, string? FilterFieldsJson = null,
    // Ancho de la tarjeta al llenar (Normal/Ancho/Completo). Configurable por formulario.
    FormCardLayout CardLayout = FormCardLayout.Normal,
    // CSS personalizado de todo el formulario (pestana Estilos del disenador).
    string? CustomCss = null,
    // Consecutivo configurable (Sequence): prefijo/padding del numero + el next_value ACTUAL de la
    // secuencia (solo lectura, para el preview del disenador). IdentityPrefix null => usar Code.
    string? IdentityPrefix = null, int IdentityPadding = 6, long SequenceNext = 0,
    // Oculta Enviar/Imprimir/autoguardado cuando el formulario se llena dentro del wizard de crear tarea.
    bool HideSubmitBar = false,
    // Escalon de estados calculados del registro (P1#5, config-driven). Null = sin escalon.
    string? StatusLadderJson = null);

/// <summary>Config transaccional de la definicion (ola F3): se edita en el panel "Propiedades del
/// formulario". Lleva ademas el ancho de tarjeta (CardLayout), que vive en el mismo panel. Prefijo/padding
/// del consecutivo solo aplican cuando IsTransactional && IdentityMode==Sequence.</summary>
public sealed record SetFormTransactionalRequest(
    bool IsTransactional, FormIdentityMode IdentityMode, string? IdentitySourceFieldCode,
    FormCardLayout CardLayout = FormCardLayout.Normal,
    string? IdentityPrefix = null, int IdentityPadding = 6,
    bool HideSubmitBar = false);

/// <summary>Fija el "proximo numero" (next_value) del consecutivo de un formulario. Operacion separada
/// del guardado del panel: valida anti-colision (no bajar por debajo de un numero ya emitido).</summary>
public sealed record SetFormSequenceNextRequest(long Next);

/// <summary>CSS personalizado de todo el formulario (pestana Estilos del disenador). Null/vacio lo borra.</summary>
public sealed record SetFormCssRequest(string? CustomCss);

/// <summary>Fila de la bandeja del formulario-modulo (ola F4): un registro enviado. <see cref="Fields"/>
/// son los valores de campo (fieldCode -> valor) para las columnas configurables de la bandeja / BI.</summary>
public sealed record FormRecordListItemDto(
    Guid Id, string? RecordNumber, FormRecordStatus RecordStatus,
    DateTimeOffset? TransactionDate, DateTimeOffset? SubmittedAt, string? Reference,
    IReadOnlyDictionary<string, string?> Fields);

/// <summary>Config de formulario-modulo (ola F4). Al promover, el usuario elige la vista de menu y el
/// grupo padre DONDE colgar el modulo; el icono es opcional. <see cref="ListColumns"/> y
/// <see cref="FilterFields"/> son field codes para las columnas/filtros de la bandeja.</summary>
public sealed record SetFormModuleRequest(
    bool IsModule, Guid? MenuViewId, Guid? ParentNodeId, string? Icon,
    IReadOnlyList<string>? ListColumns = null, IReadOnlyList<string>? FilterFields = null,
    string? MenuLabel = null);

public sealed record CreateFormDefinitionRequest(string Code, string Title, string? Description = null);

/// <summary>Version es el token de concurrencia optimista leido por el cliente (ADR-0013).</summary>
public sealed record UpdateFormDefinitionRequest(string Title, string? Description, long Version);

public sealed record SaveFormContainerRequest(
    string Name, FormContainerType ContainerType = FormContainerType.Segment,
    Guid? ParentId = null, string? Style = null,
    string? TabsJson = null, int Width = 12, bool IsLocked = false, bool IsHidden = false,
    // Etiquetas en linea (label al frente del valor). Config-driven por contenedor (Row/Col).
    bool InlineLabels = false,
    // Acceso por cargo (ADR-0082): Guids de OrgUnit-Cargo autorizados a operar la seccion. Null/vacio = todos.
    string? AllowedCargosJson = null,
    // Visibilidad condicional de la seccion por valor de una pregunta ({field,op,value}). Null = siempre visible.
    string? VisibleWhenJson = null);

/// <summary>
/// Width (1..12) es la fuente del layout del constructor (ADR-0021). Si viene en 12 (el
/// default) y GridCol trae una columna bootstrap parseable, Width se deriva de GridCol
/// (compatibilidad con callers previos); en cualquier otro caso GridCol se sincroniza
/// desde Width (col-12 / col-md-N).
/// </summary>
public sealed record SaveFormQuestionRequest(
    Guid? ContainerId, string FieldCode, string Label, FormControlType ControlType,
    string? Caption = null, string? HelpText = null, string? OptionsJson = null,
    bool Required = false, string GridCol = "col-12", string? Numeral = null,
    string? ValidationJson = null,
    int Width = 12, string? PlaceholderText = null, string? DefaultValue = null,
    bool IsLocked = false, bool IsHidden = false,
    // Origen de datos / lookup (ola F1, doc 01 D4).
    FormSourceKind SourceKind = FormSourceKind.Options, string? SourceRef = null,
    string? DisplayField = null, string? ValueField = null, string? FilterJson = null,
    string? AutofillMapJson = null, FormFieldPresentation Presentation = FormFieldPresentation.Autocomplete,
    // Calculo / agregacion (ola F2, doc 01 D5).
    string? CalcExpression = null, FormAggregate Aggregate = FormAggregate.None,
    // Maestro-detalle (ola F5, doc 01 D7): definicion hija del campo Subform.
    Guid? SubformDefinitionId = null,
    // Transversales (ola F6, doc 01 D8): default dinamico + formato + permisos por campo.
    FormDefaultDynamic DefaultDynamic = FormDefaultDynamic.None, string? Format = null,
    string? FieldVisibilityJson = null,
    // Configurador en cascada (motor generico): taxonomia de la pregunta CascadeConfigurator.
    string? CascadeConfigJson = null,
    // Visibilidad condicional por valor de otra pregunta ({field,op,value}). Null = siempre visible.
    string? VisibleWhenJson = null);

public sealed record FormResponseDto(
    Guid Id, Guid DefinitionId, string? Reference, FormResponseStatus Status,
    IReadOnlyDictionary<string, FormFieldValue> Data,
    DateTimeOffset? SubmittedAt, Guid? SubmittedByTenantUserId, long Version,
    // Registro transaccional (ola F3, doc 01 D2).
    string? RecordNumber = null, FormRecordStatus RecordStatus = FormRecordStatus.Draft,
    DateTimeOffset? TransactionDate = null);

/// <summary>Opciones de emision de un token de publicacion por URL.</summary>
public sealed record EmitFormTokenRequest(
    string? Reference = null, int ExpirationHours = 24,
    bool SingleUse = false, bool AllowAnonymous = true);

/// <summary>El Token viaja EN CLARO una unica vez (solo se persiste el hash SHA-256).</summary>
public sealed record EmitFormTokenResult(Guid TokenId, string Token, DateTimeOffset ExpiresAt);

public sealed record FormTokenDto(
    Guid Id, Guid DefinitionId, string? Reference, DateTimeOffset ExpiresAt,
    bool SingleUse, DateTimeOffset? UsedAt, DateTimeOffset? RevokedAt,
    bool AllowAnonymous, DateTimeOffset CreatedAt);

/// <summary>
/// Resultado de validar un token del visor publico. Cuando IsValid es false NO se expone el
/// motivo (expirado/usado/revocado/inexistente) para no filtrar informacion: el visor muestra
/// siempre el mismo mensaje neutro.
/// </summary>
public sealed record FormTokenValidation(
    bool IsValid, Guid? TokenId = null, Guid? TenantId = null, Guid? DefinitionId = null,
    string? Reference = null, bool SingleUse = false, bool AllowAnonymous = false)
{
    public static readonly FormTokenValidation Invalid = new(false);
}

/// <summary>
/// Formulario exigido por un paso current del flujo de una tarea (ADR-0015). Si el nodo
/// tiene una compuerta exclusiva adelante (IsGatewayAhead), ApprovalOptions trae las salidas
/// modeladas del gateway (p.ej. Aprobada/Rechazada): la UI pide esa decision JUNTO al
/// formulario y la propaga al enviar, para que el paso lleve el ApprovalResult y el motor
/// resuelva el gateway (ADR-0037). Sin gateway adelante, ApprovalOptions va vacio.
/// </summary>
public sealed record TaskStepFormDto(
    Guid ResponseId, Guid DefinitionId, string FormCode, string FormTitle,
    Guid WorkflowInstanceId, Guid WorkflowNodeId, string? NodeName,
    FormFlowLinkStatus LinkStatus, FormResponseStatus ResponseStatus, string? Reference,
    bool IsGatewayAhead = false, IReadOnlyList<string>? ApprovalOptions = null,
    // Ancho de tarjeta del formulario (Normal/Ancho/Completo), para dimensionar el modal del detalle.
    FormCardLayout CardLayout = FormCardLayout.Normal);

/// <summary>
/// Formulario del PRIMER PASO del flujo (evento de inicio) que el wizard ofrece diligenciar AL CREAR
/// la actividad cuando el concepto no define un formulario propio (ADR-0069). Se llena como los del
/// concepto (tarjeta + modal) y se ancla al numero EXACTO de la tarea, para que la continuidad (mismo
/// formulario en pasos siguientes) cargue los mismos datos.
/// </summary>
public sealed record CreationFlowFormDto(
    Guid DefinitionId, string FormCode, string FormTitle, FormCardLayout CardLayout);

/// <summary>
/// Formulario por defecto que el concepto definio para la actividad
/// (ActividadSubcategoria.FormDefinitionId, 000131). Se deriva de la subcategoria de la tarea y se
/// ancla como borrador idempotente al numero de la tarea, igual que los formularios del paso, pero
/// NO pertenece a un paso del flujo: al enviarlo solo se guarda la respuesta (no completa nada).
/// Null si la tarea no tiene subcategoria, la subcategoria no define formulario, o este no esta activo.
/// </summary>
public sealed record TaskConceptFormDto(
    Guid ResponseId, Guid DefinitionId, string FormCode, string FormTitle,
    string? Reference, FormResponseStatus ResponseStatus);

/// <summary>
/// Una respuesta del formulario del concepto anclada a la tarea (una "cotizacion"). Varias por tarea:
/// la UI las muestra como tarjetas. Titulo/Cliente son un resumen extraido del Data para la tarjeta.
/// Status: Draft = borrador editable; Submitted = finalizada (solo lectura hasta reabrir).
/// </summary>
public sealed record TaskConceptFormItemDto(
    Guid ResponseId, string? Reference, FormResponseStatus Status,
    string? Titulo, string? Cliente, DateTimeOffset CreatedAt,
    // ADR-0065: es el formulario activo por defecto de la tarea (el efectivo si ninguno fue marcado).
    bool IsActive = false);

/// <summary>
/// Formulario del concepto (subcategoria) de la tarea + sus cotizaciones (respuestas). Null si la
/// subcategoria no define formulario o este no esta activo. Items puede venir vacio (aun sin cotizaciones):
/// la UI ofrece "Agregar cotizacion" igual, porque el concepto SI tiene formulario.
/// </summary>
public sealed record TaskConceptFormsDto(
    Guid DefinitionId, string FormCode, string FormTitle,
    IReadOnlyList<TaskConceptFormItemDto> Items,
    // Ancho de tarjeta del formulario (Normal/Ancho/Completo), para dimensionar el modal del detalle.
    FormCardLayout CardLayout = FormCardLayout.Normal,
    // HEREDADO de la tarea PADRE (salto de flujo, ADR-0076): estos formularios pertenecen al padre y en la
    // tarea hija se muestran SOLO para consulta (sin +Agregar / activar / copiar / eliminar).
    bool Inherited = false);

/// <summary>Formulario DERIVADO anclado a la tarea (ADR-0078): de OTRA definicion (no la del concepto),
/// creado por transformacion (p.ej. Orden de Trabajo desde una cotizacion). Tarjeta extra en Formularios.</summary>
public sealed record TaskRelatedFormDto(
    Guid ResponseId, Guid DefinitionId, string FormCode, string FormTitle,
    string? Reference, string? RecordNumber, FormResponseStatus Status, DateTimeOffset CreatedAt,
    FormCardLayout CardLayout = FormCardLayout.Normal);

/// <summary>ADR-0078: apunta al registro DERIVADO de una respuesta (la Orden de Trabajo creada desde una
/// cotizacion). La UI del renderer lo usa para marcar la origen como "convertida" y para reabrir el derivado.
/// Null si la respuesta aun no se ha convertido.</summary>
public sealed record DerivedFormRefDto(
    Guid ResponseId, string FormCode, string FormTitle, string? RecordNumber, FormResponseStatus Status);

/// <summary>
/// Un formulario relevante para un tablero (ADR-0065, columnas de campo de formulario): el del
/// concepto de sus tareas o uno de los pasos de su flujo. Para el selector "Elijo un formulario".
/// </summary>
public sealed record BoardFormDto(Guid DefinitionId, string Code, string Title, bool IsConcept);

/// <summary>
/// El documento Data del formulario EFECTIVO de una tarea para una definicion dada (ADR-0065):
/// el activo si es del concepto, o la respuesta del paso (Submitted/mas reciente). La UI extrae
/// de aqui los campos elegidos para pintarlos como columnas informativas de la lista.
/// </summary>
public sealed record TaskFormDataDto(Guid TaskItemId, Guid DefinitionId, string Data);

/// <summary>
/// Una fuente de DETALLE para la vista Lista (ADR-0065): un campo GridDetail de un formulario del
/// tablero, con sus columnas. La UI ofrece estas fuentes para elegir UNA y cuales columnas mostrar.
/// </summary>
public sealed record BoardGridSourceDto(Guid FormDefId, string FormTitle, string GridFieldCode,
    string GridLabel, IReadOnlyList<BoardGridColumnDto> Columns);

/// <summary>Columna de un GridDetail (id de columna + etiqueta + formato de presentacion).</summary>
public sealed record BoardGridColumnDto(string Id, string Label, string? Format);

/// <summary>Filas del GridDetail (detalle) de una tarea: cada fila es un dict columnaId -> valor.</summary>
public sealed record TaskGridRowsDto(Guid TaskItemId, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);
