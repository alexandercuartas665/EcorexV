using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Ola A1 del capitulo "Tareas de proceso - Arranque y encargado del flujo".
///
/// Camina el grafo del flujo EN SECO (sin instancia, sin pasos, sin persistir) desde el startEvent
/// hasta el primer nodo Task, para saber QUIEN lo va a atender ANTES de crear la actividad.
///
/// La navegacion replica la del motor (<see cref="WorkflowEngine"/>): los startEvent y los gateways
/// se atraviesan solos, y las compuertas se resuelven con <see cref="WorkflowConditionEvaluator"/>
/// usando <c>approvalResult = null</c> -- que es exactamente el estado en el que el motor las
/// evalua al arrancar. Por eso el nodo que devuelve este servicio es el mismo que el motor
/// activara despues.
///
/// Solo LEE. No toca el motor ni el estado. Multi-tenant por el query filter global.
/// </summary>
public sealed class WorkflowStartService : IWorkflowStartService
{
    /// <summary>Tope de saltos de la caminata (mismo espiritu que MaxAdvanceIterations del motor).</summary>
    private const int MaxWalkIterations = 50;

    private static readonly IReadOnlyList<FirstStepCargoDto> NoCargos = [];
    private static readonly IReadOnlyList<Guid> NoCandidates = [];

    private readonly IApplicationDbContext _db;
    private readonly INodeAssigneeResolver _assignees;

    public WorkflowStartService(IApplicationDbContext db, INodeAssigneeResolver assignees)
    {
        _db = db;
        _assignees = assignees;
    }

    public async Task<FirstStepDto> ResolveFirstStepAsync(
        Guid subcategoriaId, CancellationToken cancellationToken = default)
    {
        var definitionId = await _db.ActividadSubcategorias
            .AsNoTracking()
            .Where(s => s.Id == subcategoriaId)
            .Select(s => s.WorkflowDefinitionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (definitionId is not Guid defId)
        {
            // Sin flujo (o la subcategoria no existe): es una actividad simple, no un proceso.
            return Fail(FirstStepStatus.SinFlujo, null, null, null);
        }

        var definition = await _db.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == defId, cancellationToken);

        if (definition is null || !definition.IsPublished || definition.IsArchived)
        {
            // D3: la hoja igual se ve en el menu, pero el arranque debe AVISAR que nacera sin proceso.
            return Fail(FirstStepStatus.FlujoNoPublicado, defId, null, null);
        }

        var firstTask = await WalkToFirstTaskAsync(defId, cancellationToken);
        if (firstTask is null)
        {
            return Fail(FirstStepStatus.SinNodoTask, defId, null, null);
        }

        var cargos = await _db.WorkflowNodePolicies
            .AsNoTracking()
            .Where(p => p.WorkflowNodeId == firstTask.Id)
            .OrderBy(p => p.SortOrder)
            .Select(p => new FirstStepCargoDto(p.OrgUnitId, p.OrgUnit!.Name))
            .ToListAsync(cancellationToken);

        if (cargos.Count == 0)
        {
            return Fail(FirstStepStatus.SinCargo, defId, firstTask.Id, firstTask.Name);
        }

        // Reusa el resolver que ya expande cargo -> ocupantes (el mismo que usan la bandeja y el tablero).
        var candidates = await _assignees.ResolveCandidatesAsync(firstTask.Id, cancellationToken);

        var status = candidates.Count == 0 ? FirstStepStatus.SinCandidatos : FirstStepStatus.Ok;
        return new FirstStepDto(status, defId, firstTask.Id, firstTask.Name, cargos, candidates);
    }

    /// <summary>
    /// Camina desde el startEvent atravesando gateways hasta topar con el primer nodo Task.
    /// Devuelve null si no hay startEvent, si la rama muere en un EndEvent, si una compuerta no
    /// resuelve ninguna arista, o si el grafo cicla.
    /// </summary>
    private async Task<WorkflowNode?> WalkToFirstTaskAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var nodes = await _db.WorkflowNodes
            .AsNoTracking()
            .Where(n => n.DefinitionId == definitionId)
            .OrderBy(n => n.StepNumber)
            .ToListAsync(cancellationToken);

        var edges = await _db.WorkflowEdges
            .AsNoTracking()
            .Where(e => e.DefinitionId == definitionId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var nodesById = nodes.ToDictionary(n => n.Id);
        var edgesBySource = edges
            .GroupBy(e => e.SourceNodeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var current = nodes.FirstOrDefault(n => n.NodeType == WorkflowNodeType.StartEvent);
        if (current is null)
        {
            return null;
        }

        var visited = new HashSet<Guid>();

        for (var i = 0; i < MaxWalkIterations; i++)
        {
            if (!visited.Add(current.Id))
            {
                return null; // ciclo: no hay primer Task alcanzable de forma determinista.
            }

            var outgoing = ResolveOutgoingDry(current, edgesBySource);
            if (outgoing.Count == 0)
            {
                return null; // compuerta sin rama aplicable, o nodo terminal.
            }

            if (!nodesById.TryGetValue(outgoing[0].TargetNodeId, out var next))
            {
                return null; // arista huerfana.
            }

            switch (next.NodeType)
            {
                case WorkflowNodeType.Task:
                    return next;
                case WorkflowNodeType.EndEvent:
                    return null; // el flujo termina sin ninguna tarea humana.
                default:
                    current = next; // startEvent encadenado o gateway: se atraviesa solo.
                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// Salientes de un nodo, resolviendo las compuertas exclusivas como lo hace el motor al arrancar
    /// (sin resultado de aprobacion todavia): primero una condicion que aplique, si no la rama por
    /// defecto. Espejo de <c>WorkflowEngine.ResolveOutgoing</c> con <c>approvalResult = null</c>.
    /// </summary>
    private static IReadOnlyList<WorkflowEdge> ResolveOutgoingDry(
        WorkflowNode node, Dictionary<Guid, List<WorkflowEdge>> edgesBySource)
    {
        var outgoing = edgesBySource.TryGetValue(node.Id, out var list) ? list : [];
        if (node.NodeType != WorkflowNodeType.ExclusiveGateway)
        {
            return outgoing;
        }

        var match = outgoing.FirstOrDefault(e => WorkflowConditionEvaluator.Evaluate(e.ConditionExpression, null));
        if (match is not null)
        {
            return [match];
        }

        var fallback = outgoing.FirstOrDefault(e => WorkflowConditionEvaluator.IsDefault(e.ConditionExpression));
        return fallback is null ? [] : [fallback];
    }

    private static FirstStepDto Fail(FirstStepStatus status, Guid? definitionId, Guid? nodeId, string? nodeName)
        => new(status, definitionId, nodeId, nodeName, NoCargos, NoCandidates);
}
