using Ecorex.Application.Common;
using Ecorex.Application.Organization;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion de la bandeja operativa de flujos (ola F2, ADR-0036). Resuelve los pasos
/// atendibles cruzando WorkflowStepHistory (current+Pending de instancias Running) con la
/// asignacion por nodo (INodeAssigneeResolver), y delega el avance en IWorkflowEngine.
///
/// Resolucion de "gateway adelante" y opciones de aprobacion (documentada por el spec de F2):
/// para un paso de un nodo Task, si alguna arista SALIENTE del nodo apunta a un ExclusiveGateway,
/// las OPCIONES de decision son los Name de las aristas SALIENTES DE ese gateway (p.ej.
/// "Aprobada"/"Rechazada"). El valor elegido se pasa como approvalResult a CompleteStep, donde
/// el motor lo evalua contra el ConditionExpression de las aristas del gateway (misma semantica
/// que WorkflowEngine.ResolveOutgoing). Asi la UI ofrece exactamente las salidas modeladas en el
/// BPMN, sin adivinar. Todo tenant-scoped (filtro global) y sin SQL crudo.
/// </summary>
public sealed class WorkflowInboxService : IWorkflowInboxService
{
    private const string ConflictMessage = "El paso ya fue reclamado por otro usuario.";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly INodeAssigneeResolver _resolver;
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowDesignService _design;

    public WorkflowInboxService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        INodeAssigneeResolver resolver,
        IWorkflowEngine engine,
        IWorkflowDesignService design)
    {
        _db = db;
        _tenantContext = tenantContext;
        _resolver = resolver;
        _engine = engine;
        _design = design;
    }

    public async Task<TaskFlowDiagramDto?> GetTaskFlowDiagramAsync(
        Guid taskId, Guid viewerTenantUserId, CancellationToken cancellationToken = default)
    {
        // La tarea debe venir de un flujo: instancia -> definicion (geometria) + historial (estado).
        var task = await _db.TaskItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task?.WorkflowInstanceId is not Guid instanceId) { return null; }
        var instance = await _db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);
        if (instance is null) { return null; }

        // Geometria + aristas + nombre (reusa el canvas del editor; solo lectura).
        var canvas = await _design.GetCanvasAsync(instance.DefinitionId, cancellationToken);
        if (canvas is null || canvas.Nodes.Count == 0) { return null; }

        // Estado por nodo: el step del CICLO vigente (mayor CycleIndex) de cada nodo de esta instancia.
        var histories = await _db.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == instanceId)
            .Select(s => new
            {
                s.Id, s.NodeId, s.Status, s.IsCurrent, s.CycleIndex,
                s.ExecutedByTenantUserId, s.ApprovalComment,
                s.AssignedToTenantUserId, s.CreatedAt, s.CompletedAt
            })
            .ToListAsync(cancellationToken);
        var latestByNode = histories
            .GroupBy(h => h.NodeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.CycleIndex).First());
        var stepIdToNodeId = histories.ToDictionary(h => h.Id, h => h.NodeId);

        // Nodos con agente (automaticos): a lo sumo uno por nodo.
        var nodeIds = canvas.Nodes.Select(n => n.Id).ToList();
        var agentByNode = await (
            from a in _db.WorkflowNodeAgents.AsNoTracking()
            where nodeIds.Contains(a.NodeId)
            join ag in _db.AiAgents.AsNoTracking() on a.AiAgentId equals ag.Id into agj
            from ag in agj.DefaultIfEmpty()
            select new { a.NodeId, AgentName = ag != null ? ag.Name : null })
            .ToDictionaryAsync(x => x.NodeId, x => x.AgentName, cancellationToken);

        var userLabels = await _db.TenantUsers.AsNoTracking()
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        // Cargo(s) del nodo (WorkflowNodePolicy -> OrgUnit del organigrama): el "cargo que atiende".
        var cargoRows = await (
            from p in _db.WorkflowNodePolicies.AsNoTracking()
            where nodeIds.Contains(p.WorkflowNodeId)
            join ou in _db.OrgUnits.AsNoTracking() on p.OrgUnitId equals ou.Id
            orderby p.SortOrder
            select new { p.WorkflowNodeId, ou.Name }).ToListAsync(cancellationToken);
        var cargoByNode = cargoRows
            .GroupBy(x => x.WorkflowNodeId)
            .ToDictionary(g => g.Key, g => string.Join(" / ", g.Select(x => x.Name)));

        // Pasos que el VIEWER puede atender en esta tarea (reusa candidatura + opciones de gateway).
        var myPending = (await GetMyPendingStepsAsync(viewerTenantUserId, cancellationToken))
            .Where(s => s.TaskItemId == taskId)
            .ToList();
        var myByNode = myPending
            .Where(p => stepIdToNodeId.ContainsKey(p.StepId))
            .GroupBy(p => stepIdToNodeId[p.StepId])
            .ToDictionary(g => g.Key, g => g.First());

        // Rutas de compuerta: si un nodo entra a un ExclusiveGateway, sus salidas son las RUTAS
        // (aprobado/rechazado + paso destino) que se muestran en el menu, coloreadas verde/rojo.
        var nameById = canvas.Nodes.ToDictionary(x => x.Id, x => x.Name);
        var gatewayIds = canvas.Nodes
            .Where(x => x.NodeType == WorkflowNodeType.ExclusiveGateway)
            .Select(x => x.Id).ToHashSet();
        var routesByNode = new Dictionary<Guid, IReadOnlyList<TaskFlowRouteDto>>();
        foreach (var cn in canvas.Nodes)
        {
            var gwEdge = canvas.Edges.FirstOrDefault(x => x.SourceNodeId == cn.Id && gatewayIds.Contains(x.TargetNodeId));
            if (gwEdge is null) { continue; }
            var routes = canvas.Edges
                .Where(x => x.SourceNodeId == gwEdge.TargetNodeId && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => new TaskFlowRouteDto(
                    x.Name!.Trim(),
                    nameById.TryGetValue(x.TargetNodeId, out var tn) ? tn : null,
                    ClassifyRoute(x.Name, x.ConditionExpression)))
                .ToList();
            if (routes.Count > 0) { routesByNode[cn.Id] = routes; }
        }

        var nodes = canvas.Nodes.Select(n =>
        {
            latestByNode.TryGetValue(n.Id, out var h);
            var state = h is null
                ? TaskFlowNodeState.Pending
                : h.Status == WorkflowStepStatus.Completed ? TaskFlowNodeState.Completed
                : h.Status == WorkflowStepStatus.Rejected ? TaskFlowNodeState.Rejected
                : h.Status == WorkflowStepStatus.Skipped ? TaskFlowNodeState.Skipped
                : h.IsCurrent ? TaskFlowNodeState.Current
                : TaskFlowNodeState.Pending;
            myByNode.TryGetValue(n.Id, out var my);
            var isAuto = agentByNode.TryGetValue(n.Id, out var agentName);
            var by = h?.ExecutedByTenantUserId is Guid ex && userLabels.TryGetValue(ex, out var em) ? em : null;
            var assignee = h?.AssignedToTenantUserId is Guid asg && userLabels.TryGetValue(asg, out var al) ? al : null;
            // Ultima actividad reportada: cierre si ya cerro, si no el momento en que el paso quedo vigente.
            DateTimeOffset? lastAt = h?.CompletedAt ?? h?.CreatedAt;
            // En espera: solo tiene sentido en el paso vigente (aun sin cerrar).
            DateTimeOffset? waitingSince = state == TaskFlowNodeState.Current ? h?.CreatedAt : null;
            return new TaskFlowNodeDto(
                NodeId: n.Id,
                Name: n.Name,
                NodeType: n.NodeType,
                X: n.X, Y: n.Y, W: n.W, H: n.H,
                State: state,
                IsCurrent: h?.IsCurrent ?? false,
                IsMine: my?.IsMine ?? false,
                IsClaimable: my?.IsClaimable ?? false,
                StepId: my?.StepId,
                HasForm: my?.HasForm ?? false,
                ApprovalOptions: my?.ApprovalOptions ?? Array.Empty<string>(),
                IsAuto: isAuto,
                AgentName: agentName,
                ByLabel: by,
                Note: h?.ApprovalComment,
                HasNote: !string.IsNullOrWhiteSpace(h?.ApprovalComment),
                AssigneeLabel: assignee,
                LastActivityAt: lastAt,
                WaitingSince: waitingSince,
                Routes: routesByNode.TryGetValue(n.Id, out var nodeRoutes) ? nodeRoutes : null,
                CargoLabel: cargoByNode.TryGetValue(n.Id, out var cargo) ? cargo : null);
        }).ToList();

        var edges = canvas.Edges
            .Select(e => new TaskFlowEdgeDto(
                e.SourceNodeId, e.TargetNodeId, e.Name,
                !string.IsNullOrWhiteSpace(e.ConditionExpression),
                gatewayIds.Contains(e.SourceNodeId) ? ClassifyRoute(e.Name, e.ConditionExpression) : TaskFlowRouteKind.Neutral))
            .ToList();

        var minX = canvas.Nodes.Min(n => n.X);
        var minY = canvas.Nodes.Min(n => n.Y);
        var maxX = canvas.Nodes.Max(n => n.X + n.W);
        var maxY = canvas.Nodes.Max(n => n.Y + n.H);

        return new TaskFlowDiagramDto(
            FlowName: canvas.Name,
            MinX: minX, MinY: minY, Width: maxX - minX, Height: maxY - minY,
            Nodes: nodes, Edges: edges);
    }

    public async Task<IReadOnlyList<PendingStepDto>> GetMyPendingStepsAsync(
        Guid tenantUserId, CancellationToken cancellationToken = default)
    {
        // Pasos current+Pending de instancias Running del tenant (filtro global) con su nodo,
        // instancia y (si la hay) tarea. Se trae el conjunto a memoria para resolver candidatos
        // por nodo (INodeAssigneeResolver hace su propia consulta del organigrama).
        var rows = await (
            from step in _db.WorkflowStepHistories.AsNoTracking()
            where step.IsCurrent && step.Status == WorkflowStepStatus.Pending
            join instance in _db.WorkflowInstances.AsNoTracking()
                on step.InstanceId equals instance.Id
            where instance.Status == WorkflowInstanceStatus.Running
            join node in _db.WorkflowNodes.AsNoTracking()
                on step.NodeId equals node.Id
            join definition in _db.WorkflowDefinitions.AsNoTracking()
                on instance.DefinitionId equals definition.Id
            select new
            {
                Step = step,
                Node = node,
                ProcessName = definition.Name,
                ProcessCode = definition.ProcessCode,
                DefinitionId = definition.Id,
                instance.TaskItemId
            }).ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // Datos de las tareas asociadas (numero + titulo) para el encabezado de cada paso.
        var taskIds = rows.Where(r => r.TaskItemId is not null).Select(r => r.TaskItemId!.Value).Distinct().ToList();
        var tasks = taskIds.Count == 0
            ? new Dictionary<Guid, (string? Number, string Title)>()
            : await _db.TaskItems.AsNoTracking()
                .Where(t => taskIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Number, t.Title })
                .ToDictionaryAsync(
                    t => t.Id,
                    t => ((string? Number, string Title))((string?)t.Number, t.Title),
                    cancellationToken);

        // Etiquetas de usuario (asignado / candidatos) por email para mostrar "de Fulano".
        var userLabels = await _db.TenantUsers.AsNoTracking()
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        // Nodos con formulario (WorkflowNodeForm): "hasForm" del paso.
        var nodeIds = rows.Select(r => r.Node.Id).Distinct().ToList();
        var formNodeIds = (await _db.WorkflowNodeForms.AsNoTracking()
            .Where(f => nodeIds.Contains(f.NodeId))
            .Select(f => f.NodeId)
            .ToListAsync(cancellationToken)).ToHashSet();

        // Aristas de las definiciones involucradas: para detectar gateway adelante y sus opciones.
        var definitionIds = rows.Select(r => r.DefinitionId).Distinct().ToList();
        var edges = (await _db.WorkflowEdges.AsNoTracking()
            .Where(e => definitionIds.Contains(e.DefinitionId))
            .Select(e => new { e.SourceNodeId, e.TargetNodeId, e.Name })
            .ToListAsync(cancellationToken))
            .Select(e => new WorkflowInboxProjection.EdgeRow(e.SourceNodeId, e.TargetNodeId, e.Name))
            .ToList();
        // Solo las compuertas AUTOMATICAS (sin asignacion) cuentan como "gateway adelante" de un Task: alli la
        // decision la toma el paso anterior. Una compuerta ATENDIDA (ADR-0068) es un punto de decision en si
        // misma; el paso anterior se completa normal y la ruta se pide EN la compuerta.
        var gatewayNodeIds = (await _db.WorkflowNodes.AsNoTracking()
            .Where(n => definitionIds.Contains(n.DefinitionId)
                && n.NodeType == WorkflowNodeType.ExclusiveGateway && !n.AllowsAssignment)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var result = new List<PendingStepDto>();
        foreach (var row in rows)
        {
            var step = row.Step;
            var node = row.Node;
            var isMine = step.AssignedToTenantUserId == tenantUserId;
            var isUnassigned = step.AssignedToTenantUserId is null;

            // Candidato = asignado a mi, o (sin asignar y soy candidato de la policy del nodo). Aplica a los
            // nodos que ESPERAN a un humano (Task siempre; compuerta/fin atendidos, ADR-0068).
            var isCandidate = isMine;
            if (isUnassigned && node.WaitsForHuman)
            {
                var candidates = await _resolver.ResolveCandidatesAsync(node.Id, cancellationToken);
                isCandidate = candidates.Contains(tenantUserId);
            }
            if (!isMine && !isCandidate)
            {
                continue;
            }

            // Opciones de decision (logica pura): una compuerta ATENDIDA ofrece SUS PROPIAS rutas (ADR-0068);
            // los demas nodos, las de una compuerta AUTOMATICA que tengan adelante.
            var (isGatewayAhead, approvalOptions) =
                node.NodeType == WorkflowNodeType.ExclusiveGateway && node.WaitsForHuman
                    ? (true, WorkflowInboxProjection.OwnRoutes(node.Id, edges))
                    : WorkflowInboxProjection.ResolveGatewayAhead(node.Id, edges, gatewayNodeIds);

            string? taskNumber = null;
            string? taskTitle = null;
            if (row.TaskItemId is Guid tid && tasks.TryGetValue(tid, out var t))
            {
                taskNumber = t.Number;
                taskTitle = t.Title;
            }

            var assignedLabel = step.AssignedToTenantUserId is Guid assignee
                ? (userLabels.TryGetValue(assignee, out var email) ? email : "(usuario)")
                : "Sin reclamar";

            result.Add(new PendingStepDto(
                StepId: step.Id,
                InstanceId: step.InstanceId,
                TaskItemId: row.TaskItemId,
                TaskNumber: taskNumber,
                TaskTitle: taskTitle,
                ProcessName: row.ProcessName,
                ProcessCode: row.ProcessCode,
                NodeName: node.Name,
                NodeType: node.NodeType,
                AssignedToTenantUserId: step.AssignedToTenantUserId,
                AssignedToLabel: assignedLabel,
                IsMine: isMine,
                IsClaimable: isUnassigned && isCandidate,
                AllowsReassignment: node.AllowsAssignment,
                HasForm: formNodeIds.Contains(node.Id),
                IsGatewayAhead: isGatewayAhead,
                ApprovalOptions: approvalOptions,
                CycleIndex: step.CycleIndex,
                CreatedAt: step.CreatedAt));
        }

        return result
            .OrderBy(s => s.CreatedAt)
            .ThenBy(s => s.TaskNumber, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<WorkflowResult<bool>> ClaimStepAsync(
        Guid stepId, Guid tenantUserId, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadCurrentStepAsync(stepId, cancellationToken);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }
        var (step, node) = loaded.Value;

        if (step.AssignedToTenantUserId == tenantUserId)
        {
            return WorkflowResult<bool>.Ok(true);
        }
        if (step.AssignedToTenantUserId is not null && !node.AllowsAssignment)
        {
            return WorkflowResult<bool>.Conflict(ConflictMessage);
        }

        if (!await IsCandidateAsync(node, tenantUserId, cancellationToken))
        {
            return WorkflowResult<bool>.Invalid("No eres candidato para atender este paso.");
        }

        step.AssignedToTenantUserId = tenantUserId;
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<bool>> ReassignStepAsync(
        Guid stepId, Guid toTenantUserId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadCurrentStepAsync(stepId, cancellationToken);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }
        var (step, node) = loaded.Value;

        if (!node.AllowsAssignment)
        {
            return WorkflowResult<bool>.Invalid("El nodo no admite reasignacion.");
        }
        if (!await IsCandidateAsync(node, toTenantUserId, cancellationToken))
        {
            return WorkflowResult<bool>.Invalid("El destino no es candidato para atender este paso.");
        }

        step.AssignedToTenantUserId = toTenantUserId;

        // Auditoria en la actividad de la tarea (si el flujo esta ligado a una tarea).
        var instance = await _db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == step.InstanceId, cancellationToken);
        if (instance?.TaskItemId is Guid taskId)
        {
            var task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
            var toEmail = await _db.TenantUsers.AsNoTracking()
                .Where(u => u.Id == toTenantUserId).Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);
            if (task is not null)
            {
                _db.TaskItemActivities.Add(new TaskItemActivity
                {
                    TenantId = task.TenantId,
                    TaskItemId = task.Id,
                    Type = TaskActivityType.Action,
                    ActorUserId = actorUserId,
                    ActorName = "Usuario",
                    Text = $"reasigno el paso {node.Name ?? node.BpmnElementId} del flujo a {toEmail ?? "otro usuario"}"
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<WorkflowInstanceDto>> CompletePendingStepAsync(
        Guid stepId, Guid tenantUserId, string? approvalResult, string? approvalComment,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadCurrentStepAsync(stepId, cancellationToken);
        if (loaded.Error is not null)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid(loaded.Error.Error ?? "Paso no vigente.");
        }
        var (step, node) = loaded.Value;

        // Solo el asignado o un candidato (paso sin asignar) puede completar.
        var authorized = step.AssignedToTenantUserId == tenantUserId
            || (step.AssignedToTenantUserId is null && await IsCandidateAsync(node, tenantUserId, cancellationToken));
        if (!authorized)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("No estas autorizado para completar este paso.");
        }

        // La decision (approvalResult) se captura EN el paso Task que entra a la compuerta. El
        // motor la propaga: al avanzar, el exclusiveGateway se auto-resuelve heredando este
        // ApprovalResult y enruta por el ConditionExpression de sus aristas (ADR-0037). La
        // bandeja ya no completa el gateway a mano: es una responsabilidad del motor.
        return await _engine.CompleteStepAsync(
            step.InstanceId, step.Id, tenantUserId, approvalResult, approvalComment,
            cancellationToken: cancellationToken);
    }

    // ---- Helpers ----

    private readonly record struct LoadedStep(
        (WorkflowStepHistory Step, WorkflowNode Node) Value, WorkflowResult<bool>? Error);

    /// <summary>Carga el paso vigente (current+Pending) y su nodo, ambos tenant-scoped.</summary>
    private async Task<LoadedStep> LoadCurrentStepAsync(Guid stepId, CancellationToken cancellationToken)
    {
        var step = await _db.WorkflowStepHistories.FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null)
        {
            return new LoadedStep(default, WorkflowResult<bool>.NotFound("El paso no existe."));
        }
        if (!step.IsCurrent || step.Status != WorkflowStepStatus.Pending)
        {
            return new LoadedStep(default, WorkflowResult<bool>.Invalid("El paso ya no esta vigente."));
        }
        var node = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == step.NodeId, cancellationToken);
        if (node is null)
        {
            return new LoadedStep(default, WorkflowResult<bool>.NotFound("El nodo del paso no existe."));
        }
        return new LoadedStep((step, node), null);
    }

    private async Task<bool> IsCandidateAsync(WorkflowNode node, Guid tenantUserId, CancellationToken cancellationToken)
    {
        var candidates = await _resolver.ResolveCandidatesAsync(node.Id, cancellationToken);
        return candidates.Contains(tenantUserId);
    }

    /// <summary>Clasifica una rama de compuerta: aprobado (verde) o rechazado (rojo). Prioriza el NOMBRE
    /// de la rama (lo que ve el usuario: "Aprobada"/"Rechazada") y solo cae a la condicion si el nombre no
    /// dice nada. Ojo: la condicion de "Rechazada" suele NEGAR "Approved" (contiene 'approv'), por eso NO
    /// se debe clasificar por la condicion cuando el nombre ya es claro (bug: rechazada salia verde).</summary>
    private static TaskFlowRouteKind ClassifyRoute(string? name, string? condition)
    {
        var n = (name ?? "").ToLowerInvariant();
        // Rechazo primero: si el nombre dice rechazo, es rojo aunque la condicion mencione "approved".
        if (n.Contains("rech") || n.Contains("reject") || n.Contains("deneg") || n.Contains("declin")) { return TaskFlowRouteKind.Reject; }
        if (n.Contains("aprob") || n.Contains("approv") || n.Contains("acept") || n.Contains("autoriz")) { return TaskFlowRouteKind.Approve; }
        // Fallback: solo si el nombre no clasifica, mirar la condicion (con el mismo orden rechazo-primero).
        var c = (condition ?? "").ToLowerInvariant();
        if (c.Contains("rech") || c.Contains("reject") || c.Contains("!=") || c.Contains("false")) { return TaskFlowRouteKind.Reject; }
        if (c.Contains("aprob") || c.Contains("approv") || c.Contains("==") || c.Contains("true")) { return TaskFlowRouteKind.Approve; }
        return TaskFlowRouteKind.Neutral;
    }
}
