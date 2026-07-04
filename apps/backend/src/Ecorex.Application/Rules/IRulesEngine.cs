namespace Ecorex.Application.Rules;

/// <summary>
/// Ejecutor del motor de reglas (FASE 4 ola 3, ADR-0016: port de cl_gestion_reglas).
/// Resuelve el verbo por VerbName en el REGISTRO TIPADO de DI (verbo desconocido = error
/// tipado, nunca reflexion sobre texto como el ejecutor legacy), mide con Stopwatch y
/// SIEMPRE escribe RuleExecutionLog (exito, fallo o skip) con TTL de 90 dias. El modo
/// Execute (SQL directo) del legacy esta PROHIBIDO: no existe verbo que reciba SQL.
/// </summary>
public interface IRulesEngine
{
    /// <summary>TTL del historial de ejecuciones (dias).</summary>
    const int HistoryTtlDays = 90;

    /// <summary>
    /// Ejecuta UNA regla con el contexto de invocacion dado. Registra SIEMPRE la ejecucion
    /// en RuleExecutionLog. Verbo no registrado o params invalidos -> Invalid (tipado) con
    /// el outcome Failed ya registrado en el historial. Reglas Inactive -> outcome Skipped.
    /// </summary>
    Task<RuleResult<RuleExecutionOutcome>> ExecuteRuleAsync(
        Guid ruleId, RuleInvocation invocation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta las reglas ACTIVAS vinculadas a una pregunta de formulario (FormFieldRule)
    /// en SortOrder, propagando el FormData entre reglas (una regla ve los valores que dejo
    /// la anterior). Devuelve las acciones de UI agregadas y el FormData final.
    /// </summary>
    Task<FormFieldRulesOutcome> ExecuteForFormFieldAsync(
        Guid formQuestionId, IReadOnlyDictionary<string, string?> formData,
        Guid? formResponseId = null, Guid? executedByTenantUserId = null,
        Guid? actorUserId = null, string actorName = "Sistema",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta las reglas ACTIVAS y AUTONOMAS de un nodo de flujo (WorkflowNodeRule) en
    /// SortOrder. AutoCompleteStep solo si TODAS tuvieron exito y alguna lo pidio.
    /// </summary>
    Task<WorkflowNodeRulesOutcome> ExecuteForWorkflowNodeAsync(
        Guid workflowNodeId, Guid? workflowInstanceId = null, Guid? taskItemId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Catalogo de verbos registrados (para el combo y el form de params de la UI).</summary>
    IReadOnlyList<RuleVerbDescriptor> GetVerbCatalog();

    /// <summary>Descriptor del verbo por nombre (null si no esta registrado).</summary>
    RuleVerbDescriptor? FindVerb(string verbName);
}
