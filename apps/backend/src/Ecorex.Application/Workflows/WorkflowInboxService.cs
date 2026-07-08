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

    public WorkflowInboxService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        INodeAssigneeResolver resolver,
        IWorkflowEngine engine)
    {
        _db = db;
        _tenantContext = tenantContext;
        _resolver = resolver;
        _engine = engine;
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
        var gatewayNodeIds = (await _db.WorkflowNodes.AsNoTracking()
            .Where(n => definitionIds.Contains(n.DefinitionId) && n.NodeType == WorkflowNodeType.ExclusiveGateway)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var result = new List<PendingStepDto>();
        foreach (var row in rows)
        {
            var step = row.Step;
            var node = row.Node;
            var isMine = step.AssignedToTenantUserId == tenantUserId;
            var isUnassigned = step.AssignedToTenantUserId is null;

            // Candidato = asignado a mi, o (sin asignar y soy candidato de la policy del nodo).
            var isCandidate = isMine;
            if (isUnassigned && node.NodeType == WorkflowNodeType.Task)
            {
                var candidates = await _resolver.ResolveCandidatesAsync(node.Id, cancellationToken);
                isCandidate = candidates.Contains(tenantUserId);
            }
            if (!isMine && !isCandidate)
            {
                continue;
            }

            // Gateway adelante y opciones de aprobacion (logica pura, documentada en el proyector).
            var (isGatewayAhead, approvalOptions) =
                WorkflowInboxProjection.ResolveGatewayAhead(node.Id, edges, gatewayNodeIds);

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
            step.InstanceId, step.Id, tenantUserId, approvalResult, approvalComment, cancellationToken);
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
}
