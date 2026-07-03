namespace Ecorex.Application.Workflows;

/// <summary>Contexto que recibe el hook de reglas al activarse un nodo Task.</summary>
public sealed record WorkflowRuleContext(
    Guid TenantId,
    Guid InstanceId,
    Guid DefinitionId,
    Guid NodeId,
    string BpmnElementId,
    string? NodeName,
    int CycleIndex,
    Guid? TaskItemId);

public enum RuleHookOutcome
{
    /// <summary>Sin efecto: el paso queda Pending esperando interaccion humana.</summary>
    None = 0,
    /// <summary>La regla resolvio el paso: el motor lo completa solo y el avance continua.</summary>
    AutoComplete
}

public sealed record RuleHookResult(RuleHookOutcome Outcome, string? ApprovalResult = null, string? Comment = null)
{
    public static readonly RuleHookResult None = new(RuleHookOutcome.None);
}

/// <summary>
/// Hook del futuro RulesEngine (siguiente ola de FASE 4): el WorkflowEngine lo invoca al
/// activar cada nodo Task. Si devuelve AutoComplete, el paso se completa solo (regla
/// autonoma del motor legacy) y el avance en cascada continua. La implementacion por
/// defecto (NoOp) no hace nada; la ola RulesEngine la reemplazara en DI.
/// </summary>
public interface IWorkflowRuleHook
{
    Task<RuleHookResult> OnNodeActivatedAsync(WorkflowRuleContext ctx, CancellationToken ct);
}

/// <summary>Implementacion por defecto: ninguna regla, todos los pasos son humanos.</summary>
public sealed class NoOpWorkflowRuleHook : IWorkflowRuleHook
{
    public Task<RuleHookResult> OnNodeActivatedAsync(WorkflowRuleContext ctx, CancellationToken ct)
        => Task.FromResult(RuleHookResult.None);
}
