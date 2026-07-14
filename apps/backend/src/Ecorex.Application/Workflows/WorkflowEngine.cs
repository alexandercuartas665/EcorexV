using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Ecorex.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion del motor de flujos (port del AdmWorkflow legacy, ver ADR-0014).
/// El avance en cascada (port de SiguienteEstado) es un while con tope de 50 iteraciones
/// que procesa pasos Completed+IsCurrent, evalua compuertas contra el resultado de
/// aprobacion, abre ciclos por RestartNodeId (CycleIndex+1, en LINQ/memoria: el grafo de
/// una definicion es pequeno y se carga completo, sin SQL crudo ni CTE) y completa la
/// instancia al alcanzar un endEvent. El historial es append-only. Emite TaskChanged via
/// ITaskBroadcaster y registra TaskItemActivity cuando hay TaskItem asociado.
/// </summary>
public sealed class WorkflowEngine : IWorkflowEngine
{
    /// <summary>Tope anti-loop-infinito heredado del motor legacy (SiguienteEstado).</summary>
    public const int MaxAdvanceIterations = 50;

    private const string ConflictMessage = "La instancia de flujo fue modificada por otro usuario. Recarga e intenta de nuevo.";

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IWorkflowRuleHook _ruleHook;
    private readonly ITaskBroadcaster _broadcaster;

    public WorkflowEngine(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        IWorkflowRuleHook ruleHook,
        ITaskBroadcaster broadcaster)
    {
        _db = db;
        _tenantContext = tenantContext;
        _ruleHook = ruleHook;
        _broadcaster = broadcaster;
    }

    // ---- Importacion y publicacion ----

    public async Task<WorkflowResult<WorkflowDefinitionDto>> ImportBpmnAsync(ImportBpmnRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return WorkflowResult<WorkflowDefinitionDto>.Invalid("No hay tenant activo.");
        }
        var processCode = (request.ProcessCode ?? "").Trim();
        if (processCode.Length is 0 or > 25)
        {
            return WorkflowResult<WorkflowDefinitionDto>.Invalid("ProcessCode es obligatorio (maximo 25 caracteres).");
        }
        var name = (request.Name ?? "").Trim();
        if (name.Length is 0 or > 150)
        {
            return WorkflowResult<WorkflowDefinitionDto>.Invalid("El nombre es obligatorio (maximo 150 caracteres).");
        }

        var parsed = BpmnProcessParser.Parse(request.BpmnXml);
        if (!parsed.IsValid)
        {
            return WorkflowResult<WorkflowDefinitionDto>.Invalid(
                "XML BPMN invalido: " + string.Join(" | ", parsed.Errors));
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        // Versionado: reimportar el mismo ProcessCode crea la version max+1, NO publicada.
        var maxVersion = await _db.WorkflowDefinitions
            .Where(d => d.ProcessCode == processCode)
            .MaxAsync(d => (int?)d.Version, cancellationToken) ?? 0;

        var definition = new WorkflowDefinition
        {
            TenantId = tenantId,
            ProcessCode = processCode,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            // El XML se guarda TAL CUAL llego (portabilidad bpmn.io: el motor no lo modifica).
            BpmnXml = request.BpmnXml,
            Version = maxVersion + 1,
            IsPublished = false
        };
        _db.WorkflowDefinitions.Add(definition);

        // Layout del canvas (ADR-0022): coordenadas del bpmndi del XML; si el XML no trae
        // DI para ningun nodo, auto-layout determinista de respaldo.
        var hasDi = parsed.Nodes.Any(n => n.X is not null && n.Y is not null);
        var autoLayout = hasDi
            ? null
            : WorkflowAutoLayout.Compute(
                parsed.Nodes.Select(n => (n.BpmnElementId, n.NodeType, n.StepNumber)).ToList(),
                parsed.Edges.Select(e => (e.SourceRef, e.TargetRef)).ToList());

        var nodesByElementId = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
        foreach (var parsedNode in parsed.Nodes)
        {
            var fallback = autoLayout is not null && autoLayout.TryGetValue(parsedNode.BpmnElementId, out var slot)
                ? slot
                : default((int X, int Y, int W, int H)?);
            var (defaultW, defaultH) = BpmnXmlWriter.DefaultSize(parsedNode.NodeType);
            var node = new WorkflowNode
            {
                TenantId = tenantId,
                DefinitionId = definition.Id,
                BpmnElementId = parsedNode.BpmnElementId,
                Name = parsedNode.Name,
                NodeType = parsedNode.NodeType,
                StepNumber = parsedNode.StepNumber,
                AllowsAssignment = parsedNode.NodeType == WorkflowNodeType.Task,
                X = parsedNode.X ?? fallback?.X ?? 0,
                Y = parsedNode.Y ?? fallback?.Y ?? 0,
                W = parsedNode.W ?? fallback?.W ?? defaultW,
                H = parsedNode.H ?? fallback?.H ?? defaultH
            };
            nodesByElementId[parsedNode.BpmnElementId] = node;
            _db.WorkflowNodes.Add(node);
        }

        foreach (var parsedEdge in parsed.Edges)
        {
            _db.WorkflowEdges.Add(new WorkflowEdge
            {
                TenantId = tenantId,
                DefinitionId = definition.Id,
                SourceNodeId = nodesByElementId[parsedEdge.SourceRef].Id,
                TargetNodeId = nodesByElementId[parsedEdge.TargetRef].Id,
                BpmnElementId = parsedEdge.BpmnElementId,
                Name = parsedEdge.Name,
                ConditionExpression = parsedEdge.ConditionExpression
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return WorkflowResult<WorkflowDefinitionDto>.Ok(await BuildDefinitionDtoAsync(definition, cancellationToken));
    }

    public async Task<WorkflowResult<WorkflowDefinitionDto>> PublishAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return WorkflowResult<WorkflowDefinitionDto>.NotFound("Definicion de flujo no encontrada.");
        }
        if (definition.IsArchived)
        {
            return WorkflowResult<WorkflowDefinitionDto>.Invalid("La definicion esta archivada.");
        }

        // Ola C1: no publicar un flujo IRRECUPERABLE. Un concepto ligado a este flujo arranca su
        // instancia al crear la actividad; si el flujo no tiene ningun paso humano (nodo Task) no
        // hay NADA que atender: la actividad naceria enrolada y sin sentido. Se bloquea aqui, una
        // sola vez, en vez de dejar que falle en cada alta.
        //   (El caso "paso sin cargo" NO se bloquea al publicar -- seria demasiado rigido para el
        //    admin a mitad de autoria y rompe flujos mecanicos --; se AVISA al crear la actividad,
        //    donde IWorkflowStartService devuelve SinCargo/SinCandidatos y la UI muestra el banner.)
        var hasTaskNode = await _db.WorkflowNodes.AsNoTracking()
            .AnyAsync(n => n.DefinitionId == definitionId && n.NodeType == WorkflowNodeType.Task, cancellationToken);
        if (!hasTaskNode)
        {
            return WorkflowResult<WorkflowDefinitionDto>.Invalid(
                "El flujo no tiene ningun paso (nodo de tarea): no hay nada que atender. Agrega al menos un paso antes de publicar.");
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        // Solo una version publicada por ProcessCode: despublicar las demas.
        var published = await _db.WorkflowDefinitions
            .Where(d => d.ProcessCode == definition.ProcessCode && d.Id != definition.Id && d.IsPublished)
            .ToListAsync(cancellationToken);
        foreach (var other in published)
        {
            other.IsPublished = false;
        }
        definition.IsPublished = true;

        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return WorkflowResult<WorkflowDefinitionDto>.Ok(await BuildDefinitionDtoAsync(definition, cancellationToken));
    }

    public async Task<WorkflowResult<bool>> SetRestartTargetAsync(Guid nodeId, Guid? restartNodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<bool>.NotFound("Nodo de flujo no encontrado.");
        }
        if (restartNodeId is Guid targetId
            && !await _db.WorkflowNodes.AnyAsync(n => n.Id == targetId && n.DefinitionId == node.DefinitionId, cancellationToken))
        {
            return WorkflowResult<bool>.Invalid("El nodo destino del reinicio no pertenece a la misma definicion.");
        }

        node.RestartNodeId = restartNodeId;
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    // ---- Ejecucion ----

    public async Task<WorkflowResult<WorkflowInstanceDto>> StartInstanceAsync(
        Guid definitionId, Guid? taskItemId = null, Guid? actorUserId = null, string actorName = "Sistema",
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("No hay tenant activo.");
        }
        var definition = await _db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return WorkflowResult<WorkflowInstanceDto>.NotFound("Definicion de flujo no encontrada.");
        }
        if (definition.IsArchived)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("La definicion esta archivada.");
        }
        if (definition.IsPaused)
        {
            // Estado "Pausado" del indice de flujos (ADR-0022): publicada pero sin aceptar
            // instancias nuevas; las que ya corren siguen su curso normal.
            return WorkflowResult<WorkflowInstanceDto>.Invalid(
                "El flujo esta pausado: no se pueden iniciar instancias nuevas.");
        }

        var graph = await LoadGraphAsync(definition.Id, cancellationToken);
        var startNode = graph.Nodes.FirstOrDefault(n => n.NodeType == WorkflowNodeType.StartEvent);
        if (startNode is null)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("La definicion no tiene startEvent.");
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        var instance = new WorkflowInstance
        {
            TenantId = tenantId,
            DefinitionId = definition.Id,
            TaskItemId = taskItemId,
            Status = WorkflowInstanceStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            CurrentCycle = 0
        };
        _db.WorkflowInstances.Add(instance);

        TaskItem? task = null;
        if (taskItemId is Guid tid)
        {
            task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == tid, cancellationToken);
            if (task is null)
            {
                return WorkflowResult<WorkflowInstanceDto>.Invalid("La tarea no existe en el tenant.");
            }
            if (task.WorkflowInstanceId is not null)
            {
                return WorkflowResult<WorkflowInstanceDto>.Invalid("La tarea ya tiene una instancia de flujo asociada.");
            }
            task.WorkflowInstanceId = instance.Id;
            // Respeta la maquina de estados: solo pasa a Active si la transicion es valida
            // (Pending -> Active); si nacio Active (asignada) no hay nada que cambiar.
            if (task.Status != TaskItemStatus.Active
                && TaskItemStateMachine.CanTransition(task.Status, TaskItemStatus.Active))
            {
                task.Status = TaskItemStatus.Active;
            }
            AddTaskActivity(task, actorUserId, actorName,
                $"inicio el flujo {definition.Name} v{definition.Version} ({definition.ProcessCode})");
        }

        // Paso del startEvent (ciclo 0): los startEvent se completan solos y el avance
        // automatico deja como current el/los siguientes.
        var steps = new List<WorkflowStepHistory>();
        await ActivateNodeAsync(instance, steps, startNode, cycleIndex: 0, isCycleStart: false, inheritedApprovalResult: null, cancellationToken);
        var stuck = await AdvanceAsync(instance, steps, graph, task, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowResult<WorkflowInstanceDto>.Conflict(ConflictMessage);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        await BroadcastTaskAsync(task, cancellationToken);

        var dto = BuildInstanceDto(instance, steps, graph);
        return stuck
            ? WorkflowResult<WorkflowInstanceDto>.Stuck(dto, StuckMessage())
            : WorkflowResult<WorkflowInstanceDto>.Ok(dto);
    }

    public async Task<IReadOnlyList<WorkflowStepDto>> GetCurrentStepsAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == instanceId && s.IsCurrent)
            .Join(_db.WorkflowNodes.AsNoTracking(), s => s.NodeId, n => n.Id, (s, n) => new { Step = s, Node = n })
            .ToListAsync(cancellationToken);
        return rows
            .OrderBy(r => r.Step.CycleIndex).ThenBy(r => r.Step.CreatedAt)
            .Select(r => new WorkflowStepDto(
                r.Step.Id, r.Step.InstanceId, r.Step.NodeId, r.Node.BpmnElementId, r.Node.Name, r.Node.NodeType,
                r.Step.CycleIndex, r.Step.IsCurrent, r.Step.IsCycleStart, r.Step.Status,
                r.Step.AssignedToTenantUserId, r.Step.ExecutedByTenantUserId,
                r.Step.ApprovalResult, r.Step.ApprovalComment, r.Step.CompletedAt))
            .ToList();
    }

    public async Task<WorkflowResult<WorkflowInstanceDto>> CompleteStepAsync(
        Guid instanceId, Guid stepId, Guid? executedByTenantUserId,
        string? approvalResult = null, string? approvalComment = null,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadRunningInstanceAsync(instanceId, cancellationToken);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }
        var (instance, steps, graph, task) = loaded.Value;

        var step = steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null)
        {
            return WorkflowResult<WorkflowInstanceDto>.NotFound("El paso no existe en la instancia.");
        }
        if (!step.IsCurrent || step.Status != WorkflowStepStatus.Pending)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("El paso no esta vigente (solo se completan pasos current y Pending).");
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        step.Status = WorkflowStepStatus.Completed;
        step.ExecutedByTenantUserId = executedByTenantUserId;
        step.CompletedAt = DateTimeOffset.UtcNow;
        step.ApprovalResult = Normalize(approvalResult);
        step.ApprovalComment = Normalize(approvalComment);

        var stuck = await AdvanceAsync(instance, steps, graph, task, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowResult<WorkflowInstanceDto>.Conflict(ConflictMessage);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        await BroadcastTaskAsync(task, cancellationToken);

        var dto = BuildInstanceDto(instance, steps, graph);
        return stuck
            ? WorkflowResult<WorkflowInstanceDto>.Stuck(dto, StuckMessage())
            : WorkflowResult<WorkflowInstanceDto>.Ok(dto);
    }

    public async Task<WorkflowResult<WorkflowInstanceDto>> RejectStepAsync(
        Guid instanceId, Guid stepId, Guid? tenantUserId, string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("El motivo del rechazo es obligatorio.");
        }
        var loaded = await LoadRunningInstanceAsync(instanceId, cancellationToken);
        if (loaded.Error is not null)
        {
            return loaded.Error;
        }
        var (instance, steps, graph, task) = loaded.Value;

        var step = steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null)
        {
            return WorkflowResult<WorkflowInstanceDto>.NotFound("El paso no existe en la instancia.");
        }
        if (!step.IsCurrent || step.Status != WorkflowStepStatus.Pending)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("El paso no esta vigente (solo se rechazan pasos current y Pending).");
        }

        // Paso anterior reactivable: el paso Completed mas reciente de un nodo con arista
        // hacia el nodo rechazado (nunca el startEvent: no es reactivable por un humano). Los
        // exclusiveGateway tampoco son reactivables (se auto-resuelven, ADR-0037): se ATRAVIESAN
        // hacia sus propias fuentes hasta llegar a un nodo humano (Task).
        var sourceNodeIds = ResolveReactivableSources(step.NodeId, graph);
        var previous = steps
            .Where(s => sourceNodeIds.Contains(s.NodeId) && s.Status == WorkflowStepStatus.Completed)
            .OrderByDescending(s => s.CompletedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefault();
        if (previous is null)
        {
            return WorkflowResult<WorkflowInstanceDto>.Invalid("No hay paso anterior reactivable.");
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        // Append-only: el rechazo se conserva y la reactivacion es una fila NUEVA.
        step.Status = WorkflowStepStatus.Rejected;
        step.IsCurrent = false;
        step.ExecutedByTenantUserId = tenantUserId;
        step.CompletedAt = DateTimeOffset.UtcNow;
        step.ApprovalResult = "Rejected";
        step.ApprovalComment = reason.Trim();

        var previousNode = graph.NodesById[previous.NodeId];
        await ActivateNodeAsync(instance, steps, previousNode, previous.CycleIndex, isCycleStart: false, inheritedApprovalResult: null, cancellationToken);
        if (task is not null)
        {
            AddTaskActivity(task, null, "Sistema",
                $"rechazo el paso {graph.NodesById[step.NodeId].Name ?? graph.NodesById[step.NodeId].BpmnElementId} del flujo: {reason.Trim()}");
        }
        var stuck = await AdvanceAsync(instance, steps, graph, task, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowResult<WorkflowInstanceDto>.Conflict(ConflictMessage);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        await BroadcastTaskAsync(task, cancellationToken);

        var dto = BuildInstanceDto(instance, steps, graph);
        return stuck
            ? WorkflowResult<WorkflowInstanceDto>.Stuck(dto, StuckMessage())
            : WorkflowResult<WorkflowInstanceDto>.Ok(dto);
    }

    // ---- Avance interno (port de SiguienteEstado) ----

    /// <summary>
    /// Avanza la instancia en cascada. Procesa pasos Completed+IsCurrent, apaga su
    /// IsCurrent y activa los targets segun las aristas (compuertas evaluan condiciones
    /// contra el ApprovalResult). Reinicios: el nodo alcanzado con RestartNodeId abre un
    /// ciclo nuevo (CycleIndex+1, IsCycleStart) en el nodo destino, en memoria (sin CTE).
    /// endEvent completa la instancia (TaskItem asociado pasa a Done). Devuelve true si se
    /// alcanzo el tope de 50 iteraciones (instancia marcada Stuck).
    /// </summary>
    private async Task<bool> AdvanceAsync(
        WorkflowInstance instance, List<WorkflowStepHistory> steps, WorkflowGraph graph,
        TaskItem? task, CancellationToken cancellationToken)
    {
        var iteration = 0;
        var hasWork = steps.Any(IsReady);
        while (hasWork && iteration < MaxAdvanceIterations)
        {
            iteration++;
            var ready = steps.Where(IsReady).ToList();
            foreach (var step in ready)
            {
                step.IsCurrent = false;
                var node = graph.NodesById[step.NodeId];
                foreach (var edge in ResolveOutgoing(node, step, graph))
                {
                    var target = graph.NodesById[edge.TargetNodeId];
                    if (target.RestartNodeId is Guid restartId
                        && graph.NodesById.TryGetValue(restartId, out var restartNode))
                    {
                        // REINICIO (port de ProcesarReinicio, en LINQ/memoria): en lugar de
                        // continuar, se abre el ciclo siguiente en el nodo destino del loop.
                        var cycle = step.CycleIndex + 1;
                        var restartInherited = restartNode.NodeType == WorkflowNodeType.ExclusiveGateway
                            ? step.ApprovalResult
                            : null;
                        await ActivateNodeAsync(instance, steps, restartNode, cycle, isCycleStart: true, restartInherited, cancellationToken);
                        instance.CurrentCycle = Math.Max(instance.CurrentCycle, cycle);
                    }
                    else if (target.NodeType == WorkflowNodeType.EndEvent)
                    {
                        // Historial completo: el endEvent alcanzado queda registrado.
                        steps.Add(AddStep(instance, target, step.CycleIndex, isCycleStart: false,
                            status: WorkflowStepStatus.Completed, isCurrent: false));
                        CompleteInstance(instance, task);
                    }
                    else
                    {
                        // Gateways: NUNCA esperan a un humano. Al activar un exclusiveGateway se
                        // hereda el ApprovalResult del paso que ENTRO (la decision se capturo en el
                        // paso previo: bandeja, formulario o CompleteStep directo) para que en la
                        // MISMA pasada el bucle lo procese (IsReady) y enrute por ConditionExpression
                        // (o por la arista default). Sin herencia el gateway quedaba Pending-current
                        // y estancaba el caso (GAP corregido, ADR-0037).
                        var inherited = target.NodeType == WorkflowNodeType.ExclusiveGateway
                            ? step.ApprovalResult
                            : null;
                        await ActivateNodeAsync(instance, steps, target, step.CycleIndex, isCycleStart: false, inherited, cancellationToken);
                    }
                }
            }

            if (instance.Status == WorkflowInstanceStatus.Completed)
            {
                // Ramas paralelas que quedaron vivas: sin efecto (Skipped), nunca se borran.
                foreach (var stale in steps.Where(s => s.IsCurrent).ToList())
                {
                    stale.IsCurrent = false;
                    if (stale.Status == WorkflowStepStatus.Pending)
                    {
                        stale.Status = WorkflowStepStatus.Skipped;
                    }
                }
                return false;
            }
            hasWork = steps.Any(IsReady);
        }

        if (hasWork)
        {
            // Tope de 50 alcanzado con trabajo pendiente: flujo mal modelado -> Stuck.
            MarkStuck(instance, task, $"el avance supero el tope de {MaxAdvanceIterations} iteraciones");
            return true;
        }

        if (instance.Status == WorkflowInstanceStatus.Running && !steps.Any(s => s.IsCurrent))
        {
            // Sin pasos vigentes y sin endEvent alcanzado: el flujo no tiene salida.
            MarkStuck(instance, task, "no quedan pasos vigentes y el flujo no alcanzo un endEvent");
        }
        return false;

        static bool IsReady(WorkflowStepHistory s) => s.IsCurrent && s.Status == WorkflowStepStatus.Completed;
    }

    /// <summary>
    /// Aristas salientes a seguir desde un nodo completado. Compuerta exclusiva: la primera
    /// arista cuya condicion aplica al ApprovalResult del paso; si ninguna, la arista sin
    /// condicion (default). Otros nodos: TODAS las salientes (ramas paralelas).
    /// </summary>
    private static IReadOnlyList<WorkflowEdge> ResolveOutgoing(WorkflowNode node, WorkflowStepHistory step, WorkflowGraph graph)
    {
        var outgoing = graph.EdgesBySource.TryGetValue(node.Id, out var list) ? list : [];
        if (node.NodeType != WorkflowNodeType.ExclusiveGateway)
        {
            return outgoing;
        }
        var match = outgoing.FirstOrDefault(e => WorkflowConditionEvaluator.Evaluate(e.ConditionExpression, step.ApprovalResult));
        if (match is not null)
        {
            return [match];
        }
        var fallback = outgoing.FirstOrDefault(e => WorkflowConditionEvaluator.IsDefault(e.ConditionExpression));
        return fallback is null ? [] : [fallback];
    }

    /// <summary>
    /// Nodos fuente reactivables por un rechazo del paso <paramref name="targetNodeId"/>: los
    /// origenes directos, saltando startEvents (no reactivables) y ATRAVESANDO exclusiveGateway
    /// (auto-resueltos, ADR-0037) hacia sus propias fuentes, hasta nodos humanos. Evita ciclos.
    /// </summary>
    private static HashSet<Guid> ResolveReactivableSources(Guid targetNodeId, WorkflowGraph graph)
    {
        var result = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(targetNodeId);
        while (stack.Count > 0)
        {
            var nodeId = stack.Pop();
            foreach (var edge in graph.Edges.Where(e => e.TargetNodeId == nodeId))
            {
                var source = graph.NodesById[edge.SourceNodeId];
                if (source.NodeType == WorkflowNodeType.StartEvent)
                {
                    continue;
                }
                if (source.NodeType == WorkflowNodeType.ExclusiveGateway)
                {
                    if (visited.Add(source.Id))
                    {
                        stack.Push(source.Id);
                    }
                    continue;
                }
                result.Add(source.Id);
            }
        }
        return result;
    }

    /// <summary>
    /// Crea el paso Pending del nodo y lo activa: los startEvent se completan solos; los
    /// exclusiveGateway se completan AUTOMATICAMENTE heredando el ApprovalResult del paso que
    /// los activo (nunca esperan a un humano, ADR-0037), de modo que el bucle de AdvanceAsync
    /// los procese en la misma pasada y enrute; los Task consultan el hook de reglas
    /// (AutoComplete = regla autonoma del legacy) y quedan Pending esperando atencion.
    /// <paramref name="inheritedApprovalResult"/> solo aplica al exclusiveGateway.
    /// </summary>
    private async Task<WorkflowStepHistory> ActivateNodeAsync(
        WorkflowInstance instance, List<WorkflowStepHistory> steps, WorkflowNode node,
        int cycleIndex, bool isCycleStart, string? inheritedApprovalResult, CancellationToken cancellationToken)
    {
        var step = AddStep(instance, node, cycleIndex, isCycleStart, WorkflowStepStatus.Pending, isCurrent: true);
        steps.Add(step);

        if (node.NodeType == WorkflowNodeType.StartEvent)
        {
            step.Status = WorkflowStepStatus.Completed;
            step.CompletedAt = DateTimeOffset.UtcNow;
        }
        else if (node.NodeType == WorkflowNodeType.ExclusiveGateway)
        {
            // Auto-resuelto: hereda la decision del paso de origen y se completa en el acto. El
            // bucle lo tomara como IsReady y ResolveOutgoing evaluara sus aristas contra este
            // ApprovalResult (o tomara la default). Sigue siendo una fila de historial (auditoria).
            step.Status = WorkflowStepStatus.Completed;
            step.CompletedAt = DateTimeOffset.UtcNow;
            step.ApprovalResult = Normalize(inheritedApprovalResult);
        }
        else if (node.NodeType == WorkflowNodeType.Task)
        {
            var result = await _ruleHook.OnNodeActivatedAsync(new WorkflowRuleContext(
                instance.TenantId, instance.Id, instance.DefinitionId, node.Id,
                node.BpmnElementId, node.Name, cycleIndex, instance.TaskItemId), cancellationToken);
            if (result.Outcome == RuleHookOutcome.AutoComplete)
            {
                step.Status = WorkflowStepStatus.Completed;
                step.CompletedAt = DateTimeOffset.UtcNow;
                step.ApprovalResult = Normalize(result.ApprovalResult);
                step.ApprovalComment = Normalize(result.Comment);
            }
        }
        return step;
    }

    private WorkflowStepHistory AddStep(
        WorkflowInstance instance, WorkflowNode node, int cycleIndex, bool isCycleStart,
        WorkflowStepStatus status, bool isCurrent)
    {
        var step = new WorkflowStepHistory
        {
            TenantId = instance.TenantId,
            InstanceId = instance.Id,
            NodeId = node.Id,
            CycleIndex = cycleIndex,
            IsCurrent = isCurrent,
            IsCycleStart = isCycleStart,
            Status = status,
            CompletedAt = status == WorkflowStepStatus.Completed ? DateTimeOffset.UtcNow : null
        };
        _db.WorkflowStepHistories.Add(step);
        return step;
    }

    private void CompleteInstance(WorkflowInstance instance, TaskItem? task)
    {
        if (instance.Status != WorkflowInstanceStatus.Running)
        {
            return;
        }
        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;

        if (task is null)
        {
            return;
        }
        // La tarea asociada pasa a Done respetando la maquina de estados (Active/InProgress
        // -> Done; desde otros estados no se fuerza, solo queda la actividad).
        if (task.Status != TaskItemStatus.Done
            && TaskItemStateMachine.CanTransition(task.Status, TaskItemStatus.Done))
        {
            task.Status = TaskItemStatus.Done;
        }
        AddTaskActivity(task, null, "Sistema", "flujo completado");
    }

    private void MarkStuck(WorkflowInstance instance, TaskItem? task, string detail)
    {
        if (instance.Status != WorkflowInstanceStatus.Running)
        {
            return;
        }
        instance.Status = WorkflowInstanceStatus.Stuck;
        if (task is not null)
        {
            AddTaskActivity(task, null, "Sistema", $"flujo atascado (Stuck): {detail}");
        }
    }

    // ---- Helpers ----

    private sealed record WorkflowGraph(
        IReadOnlyList<WorkflowNode> Nodes,
        IReadOnlyList<WorkflowEdge> Edges,
        Dictionary<Guid, WorkflowNode> NodesById,
        Dictionary<Guid, List<WorkflowEdge>> EdgesBySource);

    /// <summary>Carga el grafo COMPLETO de la definicion (pequeno por diseno) a memoria.</summary>
    private async Task<WorkflowGraph> LoadGraphAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var nodes = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == definitionId)
            .OrderBy(n => n.StepNumber)
            .ToListAsync(cancellationToken);
        var edges = await _db.WorkflowEdges.AsNoTracking()
            .Where(e => e.DefinitionId == definitionId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
        return new WorkflowGraph(
            nodes, edges,
            nodes.ToDictionary(n => n.Id),
            edges.GroupBy(e => e.SourceNodeId).ToDictionary(g => g.Key, g => g.ToList()));
    }

    private readonly record struct LoadedInstance(
        (WorkflowInstance Instance, List<WorkflowStepHistory> Steps, WorkflowGraph Graph, TaskItem? Task) Value,
        WorkflowResult<WorkflowInstanceDto>? Error);

    private async Task<LoadedInstance> LoadRunningInstanceAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var instance = await _db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);
        if (instance is null)
        {
            return new LoadedInstance(default, WorkflowResult<WorkflowInstanceDto>.NotFound("Instancia de flujo no encontrada."));
        }
        if (instance.Status != WorkflowInstanceStatus.Running)
        {
            return new LoadedInstance(default, WorkflowResult<WorkflowInstanceDto>.Invalid(
                $"La instancia no esta en ejecucion (estado {instance.Status})."));
        }
        var steps = await _db.WorkflowStepHistories
            .Where(s => s.InstanceId == instanceId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        var graph = await LoadGraphAsync(instance.DefinitionId, cancellationToken);
        TaskItem? task = null;
        if (instance.TaskItemId is Guid taskId)
        {
            task = await _db.TaskItems.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        }
        return new LoadedInstance((instance, steps, graph, task), null);
    }

    private void AddTaskActivity(TaskItem task, Guid? actorUserId, string actorName, string text)
    {
        _db.TaskItemActivities.Add(new TaskItemActivity
        {
            TenantId = task.TenantId,
            TaskItemId = task.Id,
            Type = TaskActivityType.Action,
            ActorUserId = actorUserId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "Sistema" : actorName.Trim(),
            Text = text
        });
    }

    private async Task BroadcastTaskAsync(TaskItem? task, CancellationToken cancellationToken)
    {
        if (task is not null)
        {
            await _broadcaster.TaskChangedAsync(task.TenantId, task.Id, task.Status, cancellationToken);
        }
    }

    /// <summary>Se une a la transaccion del llamador si ya hay una abierta (null = unida).</summary>
    private async Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(CancellationToken cancellationToken)
        => _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);

    private async Task<WorkflowDefinitionDto> BuildDefinitionDtoAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var graph = await LoadGraphAsync(definition.Id, cancellationToken);
        return new WorkflowDefinitionDto(
            definition.Id, definition.ProcessCode, definition.Name, definition.Description,
            definition.Version, definition.IsPublished, definition.IsArchived,
            graph.Nodes.Select(n => new WorkflowNodeDto(
                n.Id, n.BpmnElementId, n.Name, n.NodeType, n.StepNumber, n.AllowsAssignment, n.RestartNodeId)).ToList(),
            graph.Edges.Select(e => new WorkflowEdgeDto(
                e.Id, e.SourceNodeId, e.TargetNodeId, e.BpmnElementId, e.Name, e.ConditionExpression)).ToList());
    }

    private static WorkflowInstanceDto BuildInstanceDto(WorkflowInstance instance, List<WorkflowStepHistory> steps, WorkflowGraph graph)
    {
        var current = steps.Where(s => s.IsCurrent).Select(s =>
        {
            var node = graph.NodesById[s.NodeId];
            return new WorkflowStepDto(
                s.Id, s.InstanceId, s.NodeId, node.BpmnElementId, node.Name, node.NodeType,
                s.CycleIndex, s.IsCurrent, s.IsCycleStart, s.Status,
                s.AssignedToTenantUserId, s.ExecutedByTenantUserId,
                s.ApprovalResult, s.ApprovalComment, s.CompletedAt);
        }).ToList();
        return new WorkflowInstanceDto(
            instance.Id, instance.DefinitionId, instance.TaskItemId, instance.Status,
            instance.StartedAt, instance.CompletedAt, instance.CurrentCycle, current);
    }

    private static string StuckMessage()
        => $"El flujo quedo atascado (Stuck): se alcanzo el tope de {MaxAdvanceIterations} iteraciones.";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
