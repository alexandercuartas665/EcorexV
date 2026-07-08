using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Logica PURA (sin EF, testeable en unit tests) de la bandeja de pasos (ola F2, ADR-0036):
/// deteccion de "compuerta adelante" y sus opciones de decision, y la regla de candidatura de
/// un usuario sobre un paso. Aisla del acceso a datos las dos decisiones que el spec pide
/// documentar, para verificarlas sin base de datos (patron de OrgAssigneeTree).
/// </summary>
public static class WorkflowInboxProjection
{
    /// <summary>Arista minima del grafo para el proyector (source, target, name).</summary>
    public readonly record struct EdgeRow(Guid SourceNodeId, Guid TargetNodeId, string? Name);

    /// <summary>
    /// Deteccion de gateway adelante y opciones de aprobacion. Si alguna arista SALIENTE del
    /// nodo apunta a un ExclusiveGateway (gatewayNodeIds), las OPCIONES son los Name de las
    /// aristas SALIENTES DE ese gateway (p.ej. Aprobada/Rechazada), distintos y no vacios.
    /// Esos nombres se pasan luego como approvalResult a CompleteStep, donde el motor los evalua
    /// contra el ConditionExpression de cada arista del gateway (misma semantica que el avance).
    /// </summary>
    public static (bool IsGatewayAhead, IReadOnlyList<string> ApprovalOptions) ResolveGatewayAhead(
        Guid nodeId, IReadOnlyList<EdgeRow> edges, IReadOnlySet<Guid> gatewayNodeIds)
    {
        Guid? gatewayId = null;
        foreach (var edge in edges)
        {
            if (edge.SourceNodeId == nodeId && gatewayNodeIds.Contains(edge.TargetNodeId))
            {
                gatewayId = edge.TargetNodeId;
                break;
            }
        }
        if (gatewayId is not Guid gw)
        {
            return (false, []);
        }

        var options = edges
            .Where(e => e.SourceNodeId == gw && !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => e.Name!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return (true, options);
    }

    /// <summary>
    /// Un usuario puede atender un paso si es el asignado, o si el paso esta sin asignar y el
    /// usuario esta entre los candidatos resueltos de la policy del nodo (INodeAssigneeResolver).
    /// Regla del modelo "cualquiera lo toma".
    /// </summary>
    public static bool CanAttend(
        Guid tenantUserId, Guid? assignedToTenantUserId, IReadOnlyCollection<Guid> nodeCandidates)
    {
        if (assignedToTenantUserId is Guid assignee)
        {
            return assignee == tenantUserId;
        }
        return nodeCandidates.Contains(tenantUserId);
    }
}
