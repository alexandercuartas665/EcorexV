using Ecorex.Application.Rules;

namespace Ecorex.Integration.Tests;

/// <summary>
/// Doble de pruebas de IRulesEngine que NO ejecuta reglas. Se usa donde el sujeto bajo prueba no ejercita
/// el motor de reglas (p.ej. DynamicFormsTests, que valida formularios/EAV, no reglas). FormResponseService
/// solo invoca ExecuteForFormSubmitAsync; este doble devuelve un outcome vacio conservando el FormData.
/// </summary>
internal sealed class NoOpRulesEngine : IRulesEngine
{
    public Task<RuleResult<RuleExecutionOutcome>> ExecuteRuleAsync(
        Guid ruleId, RuleInvocation invocation, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("NoOpRulesEngine no ejecuta reglas.");

    public Task<FormFieldRulesOutcome> ExecuteForFormFieldAsync(
        Guid formQuestionId, IReadOnlyDictionary<string, string?> formData,
        Guid? formResponseId = null, Guid? executedByTenantUserId = null,
        Guid? actorUserId = null, string actorName = "Sistema",
        CancellationToken cancellationToken = default)
        => Task.FromResult(new FormFieldRulesOutcome(
            Array.Empty<RuleExecutionOutcome>(), Array.Empty<RuleAction>(), formData));

    public Task<FormFieldRulesOutcome> ExecuteForFormSubmitAsync(
        Guid definitionId, IReadOnlyDictionary<string, string?> formData,
        Guid? formResponseId = null, Guid? executedByTenantUserId = null,
        Guid? actorUserId = null, string actorName = "Formulario",
        CancellationToken cancellationToken = default)
        => Task.FromResult(new FormFieldRulesOutcome(
            Array.Empty<RuleExecutionOutcome>(), Array.Empty<RuleAction>(), formData));

    public Task<WorkflowNodeRulesOutcome> ExecuteForWorkflowNodeAsync(
        Guid workflowNodeId, Guid? workflowInstanceId = null, Guid? taskItemId = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("NoOpRulesEngine no ejecuta reglas de nodo de flujo.");

    public IReadOnlyList<RuleVerbDescriptor> GetVerbCatalog() => Array.Empty<RuleVerbDescriptor>();

    public RuleVerbDescriptor? FindVerb(string verbName) => null;
}
