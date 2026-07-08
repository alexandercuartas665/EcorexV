namespace Ecorex.Application.Organization;

/// <summary>
/// Resuelve los usuarios candidatos a atender un nodo de flujo (asignacion por nodo,
/// ADR-0035, ola F1). Para cada Dependencia/Cargo asignado al nodo (WorkflowNodePolicy),
/// expande al conjunto de TenantUserIds via OrgAssigneeTree (funcionarios descendientes +
/// miembros + responsables), distinct. Lo consume la ola F2 (bandeja/atender). Tenant-scoped.
/// </summary>
public interface INodeAssigneeResolver
{
    /// <summary>
    /// TenantUserIds distintos que pueden atender el nodo. Vacio si el nodo no tiene policies
    /// (sin asignacion = nadie resuelto todavia).
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveCandidatesAsync(Guid workflowNodeId, CancellationToken cancellationToken = default);
}
