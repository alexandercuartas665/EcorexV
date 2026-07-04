using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Vinculo nodo de flujo -> regla (RulesEngine, FASE 4 ola 3). Las reglas IsAutonomous se
/// ejecutan solas al activarse el nodo Task (via WorkflowRuleHook); si todas tienen exito
/// y alguna pide AutoCompleteStep, el paso se completa solo (regla autonoma del legacy).
/// FK al nodo en cascada; a la regla NO ACTION. Unico por (WorkflowNodeId, RuleId).
/// TENANT-SCOPED.
/// </summary>
public class WorkflowNodeRule : TenantEntity
{
    public Guid WorkflowNodeId { get; set; }
    public WorkflowNode? WorkflowNode { get; set; }

    public Guid RuleId { get; set; }
    public Rule? Rule { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Se ejecuta sola al activarse el nodo (regla autonoma del motor legacy).</summary>
    public bool IsAutonomous { get; set; } = true;
}
