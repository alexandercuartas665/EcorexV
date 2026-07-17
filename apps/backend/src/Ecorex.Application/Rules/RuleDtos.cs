using System.Text.Json;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Rules;

// ---- Motor de reglas (FASE 4 ola 3, ADR-0016) ----

/// <summary>Tipo de un parametro de configuracion de verbo (para que la UI lo renderice).</summary>
public enum RuleParamType
{
    Text = 0,
    Number,
    Boolean,
    /// <summary>FieldCode de una pregunta de formulario (texto con semantica de campo).</summary>
    FieldCode,
    /// <summary>Fragmento JSON (arreglos/objetos, ej. mapeos de PASAR_CAMPOS).</summary>
    Json
}

/// <summary>Parametro esperado por un verbo (port TIPADO del protocolo PARAM_XML legacy).</summary>
public sealed record RuleVerbParamDescriptor(
    string Name, string Label, RuleParamType Type, bool Required, string? Description = null);

/// <summary>
/// Contrato de configuracion de un verbo: la UI del modulo de reglas lo usa para renderizar
/// el formulario de parametros sin conocer la clase (registro tipado, nunca reflexion).
/// </summary>
public sealed record RuleVerbDescriptor(
    string VerbName, string DisplayName, string Description,
    IReadOnlyList<RuleVerbParamDescriptor> Params);

/// <summary>Tipo de accion de UI devuelta por un verbo (la aplica el DynamicFormRenderer).</summary>
public enum RuleActionKind
{
    HideField = 0,
    ShowField,
    SetFieldValue,
    SetRequired
}

/// <summary>Accion de UI tipada devuelta por la ejecucion de una regla.</summary>
public sealed record RuleAction(RuleActionKind Kind, string FieldCode, string? Value = null, bool Required = false)
{
    public static RuleAction Hide(string fieldCode) => new(RuleActionKind.HideField, fieldCode);
    public static RuleAction Show(string fieldCode) => new(RuleActionKind.ShowField, fieldCode);
    public static RuleAction SetValue(string fieldCode, string? value) => new(RuleActionKind.SetFieldValue, fieldCode, value);
    public static RuleAction SetRequired(string fieldCode, bool required) => new(RuleActionKind.SetRequired, fieldCode, Required: required);
}

/// <summary>Resultado de la ejecucion de UN verbo.</summary>
public sealed record RuleVerbResult(
    bool Success, string? Message = null, int RecordsAffected = 0,
    IReadOnlyList<RuleAction>? Actions = null, bool AutoCompleteStep = false)
{
    public IReadOnlyList<RuleAction> ActionList => Actions ?? [];

    public static RuleVerbResult Ok(string? message = null, int recordsAffected = 0,
        IReadOnlyList<RuleAction>? actions = null, bool autoCompleteStep = false)
        => new(true, message, recordsAffected, actions, autoCompleteStep);

    public static RuleVerbResult Fail(string message) => new(false, message);
}

/// <summary>
/// Contexto que recibe un verbo al ejecutarse: tenant/regla/disparador + los parametros de
/// configuracion ya deserializados + el contexto opcional del caso (tarea, instancia de
/// flujo, nodo, respuesta de formulario y el documento de datos fieldCode -> valor).
/// FormData es mutable a proposito: los verbos encadenados de un mismo campo ven los
/// valores que dejo el verbo anterior.
/// </summary>
public sealed class RuleContext
{
    public required Guid TenantId { get; init; }
    public required Guid RuleId { get; init; }
    public required RuleTriggerKind TriggerKind { get; init; }

    /// <summary>Parametros de configuracion del verbo (ParamsJson deserializado).</summary>
    public IReadOnlyDictionary<string, JsonElement> Params { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    public Guid? TaskItemId { get; init; }
    public Guid? WorkflowInstanceId { get; init; }
    public Guid? NodeId { get; init; }
    public Guid? FormResponseId { get; init; }

    /// <summary>Documento de datos del formulario (fieldCode -> valor). Mutable.</summary>
    public Dictionary<string, string?> FormData { get; init; } = new(StringComparer.Ordinal);

    /// <summary>TenantUser que origino la ejecucion (null si fue el sistema).</summary>
    public Guid? ExecutedByTenantUserId { get; init; }

    /// <summary>PlatformUser actor (para actividades de tarea creadas por verbos).</summary>
    public Guid? ActorUserId { get; init; }

    public string ActorName { get; init; } = "Sistema";

    // ---- Helpers de lectura de parametros ----

    public string? GetStringParam(string name)
    {
        if (!Params.TryGetValue(name, out var element))
        {
            return null;
        }
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
            _ => null
        };
    }

    public bool GetBoolParam(string name, bool defaultValue = false)
    {
        if (!Params.TryGetValue(name, out var element))
        {
            return defaultValue;
        }
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) ? parsed : defaultValue,
            _ => defaultValue
        };
    }

    public int GetIntParam(string name, int defaultValue)
    {
        if (!Params.TryGetValue(name, out var element))
        {
            return defaultValue;
        }
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(element.GetString(), out var s) => s,
            _ => defaultValue
        };
    }
}

/// <summary>
/// Verbo del catalogo del RulesEngine. Cada verbo es UNA clase registrada explicitamente
/// en DI (registro tipado); el motor lo resuelve por Name en un diccionario. Un VerbName
/// desconocido es un error tipado, NUNCA Activator.CreateInstance sobre texto (el RCE del
/// ejecutor legacy, ADR-0016).
/// </summary>
public interface IRuleVerb
{
    /// <summary>Clave del registro (ej. PASAR_CAMPOS). Se compara sin distinguir mayusculas.</summary>
    string Name { get; }

    /// <summary>Contrato de parametros para que la UI renderice la configuracion.</summary>
    RuleVerbDescriptor Descriptor { get; }

    Task<RuleVerbResult> ExecuteAsync(RuleContext context, CancellationToken cancellationToken);
}

// ---- Invocacion y resultados del motor ----

/// <summary>Datos de la invocacion de una regla (payload que queda en ContextJson del log).</summary>
public sealed record RuleInvocation(
    RuleTriggerKind TriggerKind,
    IReadOnlyDictionary<string, string?>? FormData = null,
    Guid? TaskItemId = null,
    Guid? WorkflowInstanceId = null,
    Guid? NodeId = null,
    Guid? FormResponseId = null,
    Guid? ExecutedByTenantUserId = null,
    Guid? ActorUserId = null,
    string ActorName = "Sistema")
{
    public static RuleInvocation Manual(Guid? executedByTenantUserId = null, Guid? actorUserId = null, string actorName = "Sistema")
        => new(RuleTriggerKind.Manual, ExecutedByTenantUserId: executedByTenantUserId,
            ActorUserId: actorUserId, ActorName: actorName);
}

/// <summary>Resultado tipado de la ejecucion de UNA regla (ya registrado en el historial).</summary>
public sealed record RuleExecutionOutcome(
    Guid RuleId, string RuleName, string VerbName, RuleExecutionStatus Status,
    string? Message, int RecordsAffected, int DurationMs,
    IReadOnlyList<RuleAction> Actions, bool AutoCompleteStep, string? ErrorMessage);

/// <summary>Resultado agregado de las reglas de un campo de formulario (en SortOrder).</summary>
public sealed record FormFieldRulesOutcome(
    IReadOnlyList<RuleExecutionOutcome> Executions,
    IReadOnlyList<RuleAction> Actions,
    IReadOnlyDictionary<string, string?> FormData);

/// <summary>Resultado agregado de las reglas autonomas de un nodo de flujo.</summary>
public sealed record WorkflowNodeRulesOutcome(
    IReadOnlyList<RuleExecutionOutcome> Executions,
    bool AllSucceeded,
    bool AutoCompleteStep);

// ---- DTOs del modulo de reglas (UI / servicios) ----

public sealed record RuleDocumentDto(
    Guid Id, string DocumentCode, string Name, string Category, string? Description,
    RuleStatus Status, bool IsArchived, int RuleCount);

public sealed record RuleDto(
    Guid Id, Guid DocumentId, string Name, string? Description, string VerbName,
    int SortOrder, string? ParamsJson, RuleStatus Status);

public sealed record SaveRuleDocumentRequest(
    string DocumentCode, string Name, string Category, string? Description = null,
    RuleStatus Status = RuleStatus.Development);

public sealed record SaveRuleRequest(
    string Name, string VerbName, string? Description = null, int SortOrder = 0,
    string? ParamsJson = null, RuleStatus Status = RuleStatus.Development,
    Guid? DocumentId = null);

/// <summary>
/// Autoria inline de una regla condicional de campo del constructor de formularios (D4). Los
/// FieldCode son los codigos de captura; Operator es equals|notEquals|empty|notEmpty; Effect es
/// hide|show|require|optional. El servicio arma el verbo BLOQUEAR_CAMPO_XCONDICION y lo vincula.
/// </summary>
public sealed record CreateFieldConditionRequest(
    Guid DefinitionId,
    Guid TriggerQuestionId,
    string SourceFieldCode,
    string Operator,
    string? Value,
    string TargetFieldCode,
    string Effect);

/// <summary>
/// Regla en la LISTA PLANA del tenant (panel izquierdo del modulo, ADR-0023): trae el
/// documento como categoria visible y el flag de archivado para filtrar.
/// </summary>
public sealed record RuleListItemDto(
    Guid Id, Guid DocumentId, string DocumentCode, string DocumentName, string DocumentCategory,
    bool DocumentIsArchived, string Name, string? Description, string VerbName, int SortOrder,
    RuleStatus Status);

/// <summary>KPIs del modulo de reglas (topbar de /reglas): conteos + ventana movil de 30 dias.</summary>
public sealed record RuleTenantStatsDto(
    int Documents, int Rules, int Executions30d, double? SuccessRate30d, int? AvgDurationMs30d);

/// <summary>
/// Metricas de UNA regla en la ventana de 30 dias (panel Propiedades). La tasa de exito
/// es Success/(Success+Failed): las Skipped no cuentan (no ejecutaron el verbo).
/// </summary>
public sealed record RuleMetricsDto(
    int Executions30d, int Success30d, int Failed30d, double? SuccessRate30d, int? AvgDurationMs30d);

/// <summary>Auditoria legible de una regla (status strip + panel Propiedades).</summary>
public sealed record RuleAuditDto(
    DateTimeOffset CreatedAt, string? CreatedByName, DateTimeOffset? UpdatedAt, string? UpdatedByName);

/// <summary>Vinculo regla -> pregunta de formulario, con etiquetas legibles para la UI.</summary>
public sealed record RuleFormLinkDto(
    Guid Id, Guid RuleId, Guid FormQuestionId, string FieldCode, string QuestionLabel,
    string FormCode, string FormTitle, int SortOrder);

/// <summary>
/// Vinculo visto DESDE la pregunta (tab Reglas del constructor, ADR-0021): reglas
/// asignadas a un FormQuestion con verbo y documento legibles.
/// </summary>
public sealed record QuestionRuleLinkDto(
    Guid Id, Guid RuleId, string RuleName, string VerbName, string DocumentCode,
    string DocumentName, int SortOrder);

/// <summary>Vinculo regla -> nodo de flujo, con etiquetas legibles para la UI.</summary>
public sealed record RuleNodeLinkDto(
    Guid Id, Guid RuleId, Guid WorkflowNodeId, string BpmnElementId, string? NodeName,
    string ProcessCode, string WorkflowName, int SortOrder, bool IsAutonomous);

public sealed record RuleExecutionLogDto(
    Guid Id, Guid RuleId, string RuleNameSnapshot, RuleTriggerKind TriggerKind,
    RuleExecutionStatus Status, int RecordsAffected, int DurationMs, string? ErrorMessage,
    Guid? ExecutedByTenantUserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    string? ExecutedByName = null);

/// <summary>Opcion generica (id + etiqueta) para los combos de vinculacion del modulo.</summary>
public sealed record RuleOption(Guid Id, string Label);
