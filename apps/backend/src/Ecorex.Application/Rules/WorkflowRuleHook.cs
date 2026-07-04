using Ecorex.Application.Workflows;

namespace Ecorex.Application.Rules;

/// <summary>
/// Implementacion REAL del hook de reglas del WorkflowEngine (reemplaza al NoOp en DI,
/// FASE 4 ola 3): al activarse un nodo Task ejecuta las reglas autonomas del nodo
/// (WorkflowNodeRule via IRulesEngine). Si TODAS tuvieron exito y alguna pidio
/// AutoCompleteStep, devuelve AutoComplete y el motor completa el paso solo (regla
/// autonoma del motor legacy). Cualquier fallo de regla deja el paso Pending (humano):
/// las reglas nunca atascan el flujo, solo lo aceleran.
/// </summary>
public sealed class WorkflowRuleHook : IWorkflowRuleHook
{
    private readonly IRulesEngine _rulesEngine;

    public WorkflowRuleHook(IRulesEngine rulesEngine)
    {
        _rulesEngine = rulesEngine;
    }

    public async Task<RuleHookResult> OnNodeActivatedAsync(WorkflowRuleContext ctx, CancellationToken ct)
    {
        var outcome = await _rulesEngine.ExecuteForWorkflowNodeAsync(
            ctx.NodeId, ctx.InstanceId, ctx.TaskItemId, ct);
        if (outcome.Executions.Count == 0 || !outcome.AllSucceeded || !outcome.AutoCompleteStep)
        {
            return RuleHookResult.None;
        }

        var names = string.Join(", ", outcome.Executions.Select(e => e.RuleName));
        return new RuleHookResult(RuleHookOutcome.AutoComplete,
            Comment: $"Paso completado por reglas autonomas: {names}");
    }
}
