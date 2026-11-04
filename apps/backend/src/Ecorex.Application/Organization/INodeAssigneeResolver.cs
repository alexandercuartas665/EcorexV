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

    /// <summary>
    /// Version EN LOTE: resuelve los candidatos de VARIOS nodos en una sola pasada, cargando el
    /// organigrama (unidades + miembros) UNA vez y todas las policies en UNA consulta. Devuelve el
    /// mapa nodeId -&gt; candidatos (un nodo sin policies no aparece en el mapa = candidatos vacios).
    /// Mata el N+1 de los listados (bandeja/tablero) que antes resolvian paso por paso, recargando
    /// el organigrama en cada iteracion.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ResolveCandidatesForNodesAsync(
        IReadOnlyCollection<Guid> workflowNodeIds, CancellationToken cancellationToken = default);
}
