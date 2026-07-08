namespace Ecorex.Application.Organization;

/// <summary>
/// Asignacion por nodo (ADR-0035, ola F1): que Dependencias/Cargos del organigrama atienden
/// un paso Task del flujo. NO se asignan usuarios directos; el resolver (INodeAssigneeResolver)
/// expande cada unidad a sus TenantUserIds candidatos. Solo se admiten unidades con Classifier
/// Dependencia o Cargo (un Funcionario nunca es asignable). Tenant-scoped por el filtro global.
/// El motor de ejecucion (bandeja/atender) que consume el resolver es la ola F2.
/// </summary>
public interface IWorkflowNodePolicyService
{
    /// <summary>Dependencias/Cargos asignados al nodo, con el conteo de candidatos resueltos.</summary>
    Task<IReadOnlyList<NodePolicyDto>> ListNodePoliciesAsync(Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>Unidades asignables (Classifier Dependencia|Cargo, no archivadas) para el selector.</summary>
    Task<IReadOnlyList<AssignableOrgUnitDto>> ListAssignableUnitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Asigna una Dependencia|Cargo al nodo. Rechaza Funcionario y duplicados.</summary>
    Task<OrgResult<NodePolicyDto>> AddNodePolicyAsync(Guid nodeId, Guid orgUnitId, CancellationToken cancellationToken = default);

    Task<OrgResult<bool>> RemoveNodePolicyAsync(Guid policyId, CancellationToken cancellationToken = default);
}
