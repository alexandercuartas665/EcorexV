using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion del servicio de diseno de flujos (ADR-0022). Ver el contrato en
/// IWorkflowDesignService: grafo editable SOLO en borradores, BpmnXml regenerado en cada
/// mutacion (BpmnXmlWriter) y metricas del indice calculadas sobre instancias reales.
/// Sin SQL crudo; todo tenant-scoped por el filtro global.
/// </summary>
public sealed class WorkflowDesignService : IWorkflowDesignService
{
    /// <summary>Estados del indice del prototipo (badge de la tarjeta).</summary>
    public const string EstadoEnMarcha = "En marcha";
    public const string EstadoPausado = "Pausado";
    public const string EstadoBorrador = "Borrador";

    private readonly IApplicationDbContext _db;
    private readonly IWorkflowEngine _engine;

    public WorkflowDesignService(IApplicationDbContext db, IWorkflowEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    // ---- Indice ----

    public async Task<FlowIndexDto> ListForIndexAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => !d.IsArchived)
            .ToListAsync(cancellationToken);

        var definitionIds = definitions.Select(d => d.Id).ToList();
        var nodeCounts = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => definitionIds.Contains(n.DefinitionId))
            .GroupBy(n => n.DefinitionId)
            .Select(g => new { DefinitionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DefinitionId, x => x.Count, cancellationToken);

        // Instancias del tenant sobre esas definiciones: el grafo de metricas es pequeno
        // (conteos por definicion) y se agrega en memoria por ProcessCode.
        var instances = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => definitionIds.Contains(i.DefinitionId))
            .Select(i => new { i.DefinitionId, i.Status, i.StartedAt })
            .ToListAsync(cancellationToken);

        // Ejecuciones (mes) = instancias INICIADAS en el mes calendario UTC en curso.
        // Deuda documentada: usar la zona horaria del tenant cuando exista en el modelo.
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var byProcess = definitions
            .GroupBy(d => d.ProcessCode, StringComparer.Ordinal)
            .Select(group =>
            {
                // Cabeza de la tarjeta: la version publicada; si no hay, la mas reciente.
                var head = group.FirstOrDefault(d => d.IsPublished)
                    ?? group.OrderByDescending(d => d.Version).First();
                var versionIds = group.Select(d => d.Id).ToHashSet();
                var processInstances = instances.Where(i => versionIds.Contains(i.DefinitionId)).ToList();

                var running = processInstances.Count(i => i.Status == WorkflowInstanceStatus.Running);
                var month = processInstances.Count(i => i.StartedAt >= monthStart);
                // Exito = Completed / (Completed + Stuck + Cancelled), en % redondeado.
                // Las Running no cuentan (aun no terminan); sin terminadas el exito es 0.
                var completed = processInstances.Count(i => i.Status == WorkflowInstanceStatus.Completed);
                var finished = processInstances.Count(i => i.Status is WorkflowInstanceStatus.Completed
                    or WorkflowInstanceStatus.Stuck or WorkflowInstanceStatus.Cancelled);
                var success = finished == 0 ? 0 : (int)Math.Round(completed * 100.0 / finished);

                return new FlowCardDto(
                    head.Id, head.ProcessCode, head.Version, head.Name, head.Category,
                    EstadoOf(head), nodeCounts.GetValueOrDefault(head.Id), running, month, success);
            })
            .OrderBy(c => c.ProcessCode, StringComparer.Ordinal)
            .ToList();

        var kpis = new FlowIndexKpisDto(
            Flows: byProcess.Count,
            RunningFlows: byProcess.Count(c => c.Estado == EstadoEnMarcha),
            ActiveInstances: instances.Count(i => i.Status == WorkflowInstanceStatus.Running),
            MonthExecutions: instances.Count(i => i.StartedAt >= monthStart));
        return new FlowIndexDto(kpis, byProcess);
    }

    public async Task<FlowCanvasDto?> GetCanvasAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        return definition is null ? null : await BuildCanvasAsync(definition, cancellationToken);
    }

    public async Task<string?> GetBpmnXmlAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Id == definitionId)
            .Select(d => d.BpmnXml)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<WorkflowResult<FlowCanvasDto>> CreateDraftAsync(string name, string? category, CancellationToken cancellationToken = default)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length is 0 or > 150)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid("El nombre es obligatorio (maximo 150 caracteres).");
        }

        // Codigo FLW-### siguiente (consecutivo simple sobre los codigos existentes).
        var codes = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.ProcessCode.StartsWith("FLW-"))
            .Select(d => d.ProcessCode)
            .ToListAsync(cancellationToken);
        var next = 1;
        foreach (var code in codes)
        {
            var tail = code.Split('-')[^1];
            if (int.TryParse(tail, out var n) && n >= next)
            {
                next = n + 1;
            }
        }
        var processCode = $"FLW-{next:000}";

        // Borrador minimo del prototipo: Inicio -> Fin, ya valido para el motor.
        var (evW, evH) = BpmnXmlWriter.DefaultSize(WorkflowNodeType.StartEvent);
        var nodes = new List<BpmnWriterNode>
        {
            new("Start_1", "Inicio", WorkflowNodeType.StartEvent, 80, 150, evW, evH),
            new("End_1", "Fin", WorkflowNodeType.EndEvent, 440, 150, evW, evH)
        };
        var edges = new List<BpmnWriterEdge> { new("Flow_1", "Start_1", "End_1", null, null) };
        var xml = BpmnXmlWriter.Write(processCode, nodes, edges);

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        var imported = await _engine.ImportBpmnAsync(new ImportBpmnRequest(processCode, trimmed, xml), cancellationToken);
        if (!imported.IsOk || imported.Value is null)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid(imported.Error ?? "No se pudo crear el flujo.");
        }
        if (!string.IsNullOrWhiteSpace(category))
        {
            var created = await _db.WorkflowDefinitions
                .FirstAsync(d => d.Id == imported.Value.Id, cancellationToken);
            created.Category = category.Trim();
            await _db.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var canvas = await GetCanvasAsync(imported.Value.Id, cancellationToken);
        return WorkflowResult<FlowCanvasDto>.Ok(canvas!);
    }

    public async Task<WorkflowResult<FlowCanvasDto>> EnsureDraftAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return WorkflowResult<FlowCanvasDto>.NotFound("Definicion de flujo no encontrada.");
        }
        if (definition.IsArchived)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid("La definicion esta archivada.");
        }
        if (!definition.IsPublished)
        {
            return WorkflowResult<FlowCanvasDto>.Ok((await BuildCanvasAsync(definition, cancellationToken))!);
        }

        // Ya existe un borrador posterior del mismo proceso? Se reutiliza (no se crean
        // versiones nuevas en cada click).
        var existingDraft = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.ProcessCode == definition.ProcessCode && !d.IsPublished && !d.IsArchived
                && d.Version > definition.Version)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingDraft is not null)
        {
            return WorkflowResult<FlowCanvasDto>.Ok((await BuildCanvasAsync(existingDraft, cancellationToken))!);
        }

        // Version borrador nueva por el camino del motor (max+1, NO publicada): el XML se
        // regenera con el layout actual para que el import materialice el mismo canvas.
        var sourceNodes = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == definition.Id).OrderBy(n => n.StepNumber)
            .ToListAsync(cancellationToken);
        var sourceEdges = await _db.WorkflowEdges.AsNoTracking()
            .Where(e => e.DefinitionId == definition.Id).OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
        var xml = WriteXml(definition.ProcessCode, sourceNodes, sourceEdges);

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        var imported = await _engine.ImportBpmnAsync(
            new ImportBpmnRequest(definition.ProcessCode, definition.Name, xml, definition.Description), cancellationToken);
        if (!imported.IsOk || imported.Value is null)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid(imported.Error ?? "No se pudo crear la version borrador.");
        }

        // Copiar lo que NO viaja en el XML: categoria, reinicios, AllowsAssignment y
        // vinculos de formulario/reglas, mapeando por BpmnElementId.
        var draft = await _db.WorkflowDefinitions.FirstAsync(d => d.Id == imported.Value.Id, cancellationToken);
        draft.Category = definition.Category;

        var draftNodes = await _db.WorkflowNodes
            .Where(n => n.DefinitionId == draft.Id).ToListAsync(cancellationToken);
        var draftByElement = draftNodes.ToDictionary(n => n.BpmnElementId, StringComparer.Ordinal);
        var sourceById = sourceNodes.ToDictionary(n => n.Id);
        foreach (var sourceNode in sourceNodes)
        {
            if (!draftByElement.TryGetValue(sourceNode.BpmnElementId, out var draftNode))
            {
                continue;
            }
            draftNode.AllowsAssignment = sourceNode.AllowsAssignment;
            if (sourceNode.RestartNodeId is Guid restartId
                && sourceById.TryGetValue(restartId, out var restartSource)
                && draftByElement.TryGetValue(restartSource.BpmnElementId, out var restartDraft))
            {
                draftNode.RestartNodeId = restartDraft.Id;
            }
        }

        var sourceNodeIds = sourceNodes.Select(n => n.Id).ToList();
        var sourceForms = await _db.WorkflowNodeForms
            .AsNoTracking().Where(f => sourceNodeIds.Contains(f.NodeId)).ToListAsync(cancellationToken);
        foreach (var form in sourceForms)
        {
            if (draftByElement.TryGetValue(sourceById[form.NodeId].BpmnElementId, out var draftNode))
            {
                _db.WorkflowNodeForms.Add(new WorkflowNodeForm
                {
                    TenantId = draft.TenantId,
                    NodeId = draftNode.Id,
                    DefinitionId = form.DefinitionId
                });
            }
        }
        var sourceRules = await _db.WorkflowNodeRules
            .AsNoTracking().Where(r => sourceNodeIds.Contains(r.WorkflowNodeId)).ToListAsync(cancellationToken);
        foreach (var rule in sourceRules)
        {
            if (draftByElement.TryGetValue(sourceById[rule.WorkflowNodeId].BpmnElementId, out var draftNode))
            {
                _db.WorkflowNodeRules.Add(new WorkflowNodeRule
                {
                    TenantId = draft.TenantId,
                    WorkflowNodeId = draftNode.Id,
                    RuleId = rule.RuleId,
                    SortOrder = rule.SortOrder,
                    IsAutonomous = rule.IsAutonomous
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<FlowCanvasDto>.Ok((await GetCanvasAsync(draft.Id, cancellationToken))!);
    }

    // ---- Guardado desde bpmn-js (ADR-0034) ----

    public async Task<WorkflowResult<FlowCanvasDto>> SaveBpmnAsync(
        Guid definitionId, string bpmnXml, CancellationToken cancellationToken = default)
    {
        // Editar publicadas es imposible: primero se deriva/reusa el borrador (EnsureDraft).
        var ensured = await EnsureDraftAsync(definitionId, cancellationToken);
        if (!ensured.IsOk || ensured.Value is null)
        {
            return ensured;
        }
        var draftId = ensured.Value.DefinitionId;

        var parsed = BpmnProcessParser.Parse(bpmnXml);
        if (!parsed.IsValid)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid(
                "XML BPMN invalido: " + string.Join(" | ", parsed.Errors));
        }

        var definition = await _db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);
        if (definition is null)
        {
            return WorkflowResult<FlowCanvasDto>.NotFound("Definicion de flujo no encontrada.");
        }
        if (definition.IsPublished || definition.IsArchived)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid("El grafo publicado/archivado no se edita.");
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        var existingNodes = await _db.WorkflowNodes
            .Where(n => n.DefinitionId == draftId).ToListAsync(cancellationToken);
        var existingByElement = existingNodes.ToDictionary(n => n.BpmnElementId, StringComparer.Ordinal);
        var parsedByElement = parsed.Nodes.ToDictionary(n => n.BpmnElementId, StringComparer.Ordinal);

        // Nodos que ya no estan en el XML: se eliminan con sus aristas, vinculos y
        // referencias de reinicio (equivalente a EliminarNodos del legacy).
        var removedNodes = existingNodes.Where(n => !parsedByElement.ContainsKey(n.BpmnElementId)).ToList();
        if (removedNodes.Count > 0)
        {
            var removedIds = removedNodes.Select(n => n.Id).ToHashSet();
            var removedForms = await _db.WorkflowNodeForms
                .Where(f => removedIds.Contains(f.NodeId)).ToListAsync(cancellationToken);
            _db.WorkflowNodeForms.RemoveRange(removedForms);
            var removedRules = await _db.WorkflowNodeRules
                .Where(r => removedIds.Contains(r.WorkflowNodeId)).ToListAsync(cancellationToken);
            _db.WorkflowNodeRules.RemoveRange(removedRules);
            foreach (var reference in existingNodes.Where(n => n.RestartNodeId is Guid rid && removedIds.Contains(rid)))
            {
                reference.RestartNodeId = null;
            }
        }

        // Alta/actualizacion de nodos por BpmnElementId (conserva config y vinculos).
        var nodeByElement = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
        foreach (var parsedNode in parsed.Nodes)
        {
            var (dw, dh) = BpmnXmlWriter.DefaultSize(parsedNode.NodeType);
            if (existingByElement.TryGetValue(parsedNode.BpmnElementId, out var node))
            {
                node.Name = parsedNode.Name;
                node.NodeType = parsedNode.NodeType;
                node.StepNumber = parsedNode.StepNumber;
                node.X = parsedNode.X ?? node.X;
                node.Y = parsedNode.Y ?? node.Y;
                node.W = parsedNode.W ?? node.W ?? dw;
                node.H = parsedNode.H ?? node.H ?? dh;
            }
            else
            {
                node = new WorkflowNode
                {
                    TenantId = definition.TenantId,
                    DefinitionId = draftId,
                    BpmnElementId = parsedNode.BpmnElementId,
                    Name = parsedNode.Name,
                    NodeType = parsedNode.NodeType,
                    StepNumber = parsedNode.StepNumber,
                    AllowsAssignment = parsedNode.NodeType == WorkflowNodeType.Task,
                    X = parsedNode.X ?? 0,
                    Y = parsedNode.Y ?? 0,
                    W = parsedNode.W ?? dw,
                    H = parsedNode.H ?? dh
                };
                _db.WorkflowNodes.Add(node);
            }
            nodeByElement[parsedNode.BpmnElementId] = node;
        }
        _db.WorkflowNodes.RemoveRange(removedNodes);

        // Aristas: se reemplazan por completo (mucho mas simple que diffear y con el mismo
        // efecto neto; las condiciones se conservan re-aplicando desde el XML).
        var existingEdges = await _db.WorkflowEdges
            .Where(e => e.DefinitionId == draftId).ToListAsync(cancellationToken);
        _db.WorkflowEdges.RemoveRange(existingEdges);
        // SaveChanges intermedio: materializa las bajas antes de insertar las aristas nuevas
        // (evita choques del indice unico BpmnElementId en la misma definicion).
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var parsedEdge in parsed.Edges)
        {
            if (!nodeByElement.TryGetValue(parsedEdge.SourceRef, out var source)
                || !nodeByElement.TryGetValue(parsedEdge.TargetRef, out var target))
            {
                continue;
            }
            _db.WorkflowEdges.Add(new WorkflowEdge
            {
                TenantId = definition.TenantId,
                DefinitionId = draftId,
                SourceNodeId = source.Id,
                TargetNodeId = target.Id,
                BpmnElementId = parsedEdge.BpmnElementId,
                Name = parsedEdge.Name,
                ConditionExpression = parsedEdge.ConditionExpression
            });
        }

        // El XML se guarda TAL CUAL lo produjo bpmn-js (portabilidad bpmn.io, ADR-0014):
        // el editor ya es la fuente del layout, no se regenera con BpmnXmlWriter.
        definition.BpmnXml = bpmnXml;
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<FlowCanvasDto>.Ok((await GetCanvasAsync(draftId, cancellationToken))!);
    }

    // ---- Mutaciones del grafo (solo borradores) ----

    public async Task<WorkflowResult<FlowCanvasNodeDto>> AddNodeAsync(
        Guid definitionId, WorkflowNodeType nodeType, int x, int y, CancellationToken cancellationToken = default)
    {
        var guarded = await LoadDraftAsync(definitionId, cancellationToken);
        if (guarded.Error is not null)
        {
            return Fail<FlowCanvasNodeDto>(guarded.Error);
        }
        var definition = guarded.Definition!;

        var nodes = await _db.WorkflowNodes
            .Where(n => n.DefinitionId == definition.Id).ToListAsync(cancellationToken);
        if (nodeType == WorkflowNodeType.StartEvent && nodes.Any(n => n.NodeType == WorkflowNodeType.StartEvent))
        {
            return WorkflowResult<FlowCanvasNodeDto>.Invalid(
                "El flujo ya tiene un startEvent (el estandar del motor exige exactamente uno).");
        }

        var usedIds = nodes.Select(n => n.BpmnElementId).ToHashSet(StringComparer.Ordinal);
        var elementId = NewElementId(nodeType, usedIds);
        var (w, h) = BpmnXmlWriter.DefaultSize(nodeType);
        var node = new WorkflowNode
        {
            TenantId = definition.TenantId,
            DefinitionId = definition.Id,
            BpmnElementId = elementId,
            Name = DefaultLabel(nodeType),
            NodeType = nodeType,
            StepNumber = (nodes.Max(n => (int?)n.StepNumber) ?? 0) + 1,
            AllowsAssignment = nodeType == WorkflowNodeType.Task,
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            W = w,
            H = h
        };
        _db.WorkflowNodes.Add(node);

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        await RegenerateXmlAsync(definition, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<FlowCanvasNodeDto>.Ok(new FlowCanvasNodeDto(
            node.Id, node.BpmnElementId, node.Name, node.NodeType, node.X, node.Y, w, h,
            node.AllowsAssignment, null, null, null, null, []));
    }

    public async Task<WorkflowResult<bool>> MoveNodeAsync(Guid nodeId, int x, int y, CancellationToken cancellationToken = default)
        => await MutateNodeAsync(nodeId, node =>
        {
            node.X = Math.Max(0, x);
            node.Y = Math.Max(0, y);
            return null;
        }, cancellationToken);

    public async Task<WorkflowResult<bool>> RenameNodeAsync(Guid nodeId, string? name, CancellationToken cancellationToken = default)
        => await MutateNodeAsync(nodeId, node =>
        {
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length > 300)
            {
                return "El nombre del nodo supera los 300 caracteres.";
            }
            node.Name = trimmed.Length == 0 ? null : trimmed;
            return null;
        }, cancellationToken);

    public async Task<WorkflowResult<FlowCanvasEdgeDto>> ConnectAsync(
        Guid sourceNodeId, Guid targetNodeId, CancellationToken cancellationToken = default)
    {
        if (sourceNodeId == targetNodeId)
        {
            return WorkflowResult<FlowCanvasEdgeDto>.Invalid("Un nodo no se puede conectar consigo mismo.");
        }
        var source = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == sourceNodeId, cancellationToken);
        var target = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == targetNodeId, cancellationToken);
        if (source is null || target is null)
        {
            return WorkflowResult<FlowCanvasEdgeDto>.NotFound("Nodo de flujo no encontrado.");
        }
        if (source.DefinitionId != target.DefinitionId)
        {
            return WorkflowResult<FlowCanvasEdgeDto>.Invalid("Los nodos pertenecen a definiciones distintas.");
        }
        var guarded = await LoadDraftAsync(source.DefinitionId, cancellationToken);
        if (guarded.Error is not null)
        {
            return Fail<FlowCanvasEdgeDto>(guarded.Error);
        }
        var definition = guarded.Definition!;
        var duplicated = await _db.WorkflowEdges.AnyAsync(
            e => e.SourceNodeId == sourceNodeId && e.TargetNodeId == targetNodeId, cancellationToken);
        if (duplicated)
        {
            return WorkflowResult<FlowCanvasEdgeDto>.Invalid("Ya existe una conexion entre esos nodos.");
        }

        var usedIds = await _db.WorkflowEdges
            .Where(e => e.DefinitionId == definition.Id && e.BpmnElementId != null)
            .Select(e => e.BpmnElementId!).ToListAsync(cancellationToken);
        var edge = new WorkflowEdge
        {
            TenantId = definition.TenantId,
            DefinitionId = definition.Id,
            SourceNodeId = sourceNodeId,
            TargetNodeId = targetNodeId,
            BpmnElementId = NewFlowId(usedIds.ToHashSet(StringComparer.Ordinal))
        };
        _db.WorkflowEdges.Add(edge);

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        await RegenerateXmlAsync(definition, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<FlowCanvasEdgeDto>.Ok(new FlowCanvasEdgeDto(
            edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.BpmnElementId, null, null));
    }

    public async Task<WorkflowResult<bool>> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<bool>.NotFound("Nodo de flujo no encontrado.");
        }
        var guarded = await LoadDraftAsync(node.DefinitionId, cancellationToken);
        if (guarded.Error is not null)
        {
            return Fail<bool>(guarded.Error);
        }
        if (node.NodeType == WorkflowNodeType.StartEvent)
        {
            // El motor exige exactamente 1 startEvent: nunca se borra (es el unico).
            return WorkflowResult<bool>.Invalid("No se puede eliminar el evento de inicio del flujo.");
        }
        var definition = guarded.Definition!;

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        var edges = await _db.WorkflowEdges
            .Where(e => e.SourceNodeId == nodeId || e.TargetNodeId == nodeId).ToListAsync(cancellationToken);
        _db.WorkflowEdges.RemoveRange(edges);
        var forms = await _db.WorkflowNodeForms.Where(f => f.NodeId == nodeId).ToListAsync(cancellationToken);
        _db.WorkflowNodeForms.RemoveRange(forms);
        var rules = await _db.WorkflowNodeRules.Where(r => r.WorkflowNodeId == nodeId).ToListAsync(cancellationToken);
        _db.WorkflowNodeRules.RemoveRange(rules);
        // Nodos que reiniciaban hacia este: el reinicio queda sin destino (null).
        var restartReferences = await _db.WorkflowNodes
            .Where(n => n.RestartNodeId == nodeId).ToListAsync(cancellationToken);
        foreach (var reference in restartReferences)
        {
            reference.RestartNodeId = null;
        }
        _db.WorkflowNodes.Remove(node);

        await RegenerateXmlAsync(definition, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<bool>> DeleteEdgeAsync(Guid edgeId, CancellationToken cancellationToken = default)
    {
        var edge = await _db.WorkflowEdges.FirstOrDefaultAsync(e => e.Id == edgeId, cancellationToken);
        if (edge is null)
        {
            return WorkflowResult<bool>.NotFound("Conexion de flujo no encontrada.");
        }
        var guarded = await LoadDraftAsync(edge.DefinitionId, cancellationToken);
        if (guarded.Error is not null)
        {
            return Fail<bool>(guarded.Error);
        }
        _db.WorkflowEdges.Remove(edge);

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        await RegenerateXmlAsync(guarded.Definition!, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<bool>> SetEdgeConditionAsync(
        Guid edgeId, string? conditionExpression, CancellationToken cancellationToken = default)
    {
        var edge = await _db.WorkflowEdges.FirstOrDefaultAsync(e => e.Id == edgeId, cancellationToken);
        if (edge is null)
        {
            return WorkflowResult<bool>.NotFound("Conexion de flujo no encontrada.");
        }
        var guarded = await LoadDraftAsync(edge.DefinitionId, cancellationToken);
        if (guarded.Error is not null)
        {
            return Fail<bool>(guarded.Error);
        }
        var normalized = string.IsNullOrWhiteSpace(conditionExpression) ? null : conditionExpression.Trim();
        if (normalized?.Length > 400)
        {
            return WorkflowResult<bool>.Invalid("La condicion supera los 400 caracteres.");
        }
        edge.ConditionExpression = normalized;

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        await RegenerateXmlAsync(guarded.Definition!, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<bool>> SetNodeConfigAsync(
        Guid nodeId, bool allowsAssignment, Guid? restartNodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<bool>.NotFound("Nodo de flujo no encontrado.");
        }
        var guarded = await LoadDraftAsync(node.DefinitionId, cancellationToken);
        if (guarded.Error is not null)
        {
            return Fail<bool>(guarded.Error);
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        // Reinicio: se REUSA el camino del motor (valida que el destino pertenezca a la
        // misma definicion). AllowsAssignment no esta en el XML: no hay que regenerarlo.
        var restart = await _engine.SetRestartTargetAsync(nodeId, restartNodeId, cancellationToken);
        if (!restart.IsOk)
        {
            return WorkflowResult<bool>.Invalid(restart.Error ?? "No se pudo configurar el reinicio.");
        }
        node.AllowsAssignment = allowsAssignment;
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<bool>.Ok(true);
    }

    /// <summary>Paleta de colores permitida para el nodo (la conoce el JS del editor). Null = sin color.</summary>
    private static readonly HashSet<string> NodeColorPalette = new(StringComparer.OrdinalIgnoreCase)
    {
        "violet", "blue", "green", "amber", "rose", "slate"
    };

    public async Task<WorkflowResult<bool>> SetNodeAppearanceAsync(
        Guid nodeId, string? color, string? note, CancellationToken cancellationToken = default)
    {
        var node = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<bool>.NotFound("Nodo de flujo no encontrado.");
        }
        // Color/nota SI se pueden editar sobre una definicion publicada (son metadatos, como el
        // formulario/regla por nodo): no cambian la topologia ni el XML. No requieren borrador.
        var key = string.IsNullOrWhiteSpace(color) ? null : color.Trim().ToLowerInvariant();
        if (key is not null && !NodeColorPalette.Contains(key))
        {
            return WorkflowResult<bool>.Invalid($"Color de nodo no valido: '{color}'.");
        }
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > 1000 })
        {
            trimmedNote = trimmedNote[..1000];
        }
        node.Color = key;
        node.Note = trimmedNote;
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    // ---- Propiedades y ciclo de vida ----

    public async Task<WorkflowResult<bool>> UpdateDefinitionPropsAsync(
        Guid definitionId, string name, string? category, string? description, CancellationToken cancellationToken = default)
    {
        var definition = await _db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return WorkflowResult<bool>.NotFound("Definicion de flujo no encontrada.");
        }
        if (definition.IsArchived)
        {
            return WorkflowResult<bool>.Invalid("La definicion esta archivada.");
        }
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length is 0 or > 150)
        {
            return WorkflowResult<bool>.Invalid("El nombre es obligatorio (maximo 150 caracteres).");
        }
        var trimmedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (trimmedCategory?.Length > 100)
        {
            return WorkflowResult<bool>.Invalid("La categoria supera los 100 caracteres.");
        }
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription?.Length > 600)
        {
            return WorkflowResult<bool>.Invalid("La descripcion supera los 600 caracteres.");
        }

        definition.Name = trimmed;
        definition.Category = trimmedCategory;
        definition.Description = trimmedDescription;
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<bool>> PauseAsync(Guid definitionId, CancellationToken cancellationToken = default)
        => await SetPausedAsync(definitionId, paused: true, cancellationToken);

    public async Task<WorkflowResult<bool>> ResumeAsync(Guid definitionId, CancellationToken cancellationToken = default)
        => await SetPausedAsync(definitionId, paused: false, cancellationToken);

    private async Task<WorkflowResult<bool>> SetPausedAsync(Guid definitionId, bool paused, CancellationToken cancellationToken)
    {
        var definition = await _db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return WorkflowResult<bool>.NotFound("Definicion de flujo no encontrada.");
        }
        if (paused && !definition.IsPublished)
        {
            return WorkflowResult<bool>.Invalid("Solo se pausan flujos publicados (los borradores no ejecutan instancias).");
        }
        definition.IsPaused = paused;
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    // ---- Importar BPMN (XML del modeler / archivo .bpmn) ----

    public async Task<WorkflowResult<FlowCanvasDto>> ImportBpmnAsync(string bpmnXml, CancellationToken cancellationToken = default)
    {
        var parsed = BpmnProcessParser.Parse(bpmnXml);
        if (!parsed.IsValid)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid(
                "XML BPMN invalido: " + string.Join(" | ", parsed.Errors));
        }

        // Nombre: primer nodo con nombre, o un rotulo generico. Codigo: FLW-### siguiente.
        var name = parsed.Nodes.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.Name))?.Name
            ?? "Flujo importado";
        var codes = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.ProcessCode.StartsWith("FLW-"))
            .Select(d => d.ProcessCode)
            .ToListAsync(cancellationToken);
        var next = 1;
        foreach (var code in codes)
        {
            var tail = code.Split('-')[^1];
            if (int.TryParse(tail, out var n) && n >= next)
            {
                next = n + 1;
            }
        }
        var processCode = $"FLW-{next:000}";

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        var imported = await _engine.ImportBpmnAsync(new ImportBpmnRequest(processCode, name, bpmnXml), cancellationToken);
        if (!imported.IsOk || imported.Value is null)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid(imported.Error ?? "No se pudo importar el flujo.");
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<FlowCanvasDto>.Ok((await GetCanvasAsync(imported.Value.Id, cancellationToken))!);
    }

    // ---- Exportar / importar JSON (formato del prototipo) ----

    public async Task<string?> ExportJsonAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.WorkflowDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return null;
        }
        var nodes = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == definitionId).OrderBy(n => n.StepNumber)
            .ToListAsync(cancellationToken);
        var nodesById = nodes.ToDictionary(n => n.Id);
        var edges = await _db.WorkflowEdges.AsNoTracking()
            .Where(e => e.DefinitionId == definitionId).OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var payload = new
        {
            id = definition.ProcessCode,
            version = definition.Version,
            nombre = definition.Name,
            categoria = definition.Category,
            estado = EstadoOf(definition),
            descripcion = definition.Description,
            nodos = nodes.Select(n => new
            {
                id = n.BpmnElementId,
                tipo = TipoOf(n.NodeType),
                label = n.Name,
                x = n.X,
                y = n.Y,
                w = n.W ?? BpmnXmlWriter.DefaultSize(n.NodeType).W,
                h = n.H ?? BpmnXmlWriter.DefaultSize(n.NodeType).H
            }),
            conexiones = edges.Select(e => new
            {
                de = nodesById[e.SourceNodeId].BpmnElementId,
                a = nodesById[e.TargetNodeId].BpmnElementId,
                nombre = e.Name,
                condicion = e.ConditionExpression
            })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<WorkflowResult<FlowCanvasDto>> ImportJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return WorkflowResult<FlowCanvasDto>.Invalid($"JSON invalido: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return WorkflowResult<FlowCanvasDto>.Invalid("El JSON debe ser un objeto con nombre, nodos y conexiones.");
            }
            var name = GetString(root, "nombre");
            if (string.IsNullOrWhiteSpace(name))
            {
                return WorkflowResult<FlowCanvasDto>.Invalid("El JSON no trae 'nombre'.");
            }
            var processCode = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(processCode))
            {
                processCode = $"FLW-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            }
            if (processCode.Length > 25)
            {
                return WorkflowResult<FlowCanvasDto>.Invalid("El 'id' (ProcessCode) supera los 25 caracteres.");
            }

            if (!root.TryGetProperty("nodos", out var nodesJson) || nodesJson.ValueKind != JsonValueKind.Array)
            {
                return WorkflowResult<FlowCanvasDto>.Invalid("El JSON no trae el arreglo 'nodos'.");
            }
            var rawNodes = new List<(string Id, string? Label, WorkflowNodeType Type, int? X, int? Y, int? W, int? H)>();
            var index = 0;
            foreach (var nodeJson in nodesJson.EnumerateArray())
            {
                index++;
                var id = GetString(nodeJson, "id") ?? $"Node_{index}";
                var tipo = GetString(nodeJson, "tipo");
                var type = ParseTipo(tipo);
                if (type is null)
                {
                    return WorkflowResult<FlowCanvasDto>.Invalid(
                        $"Tipo de nodo no soportado: '{tipo}' (usa startEvent, task, exclusiveGateway o endEvent).");
                }
                rawNodes.Add((id, GetString(nodeJson, "label") ?? GetString(nodeJson, "nombre"), type.Value,
                    GetInt(nodeJson, "x"), GetInt(nodeJson, "y"), GetInt(nodeJson, "w"), GetInt(nodeJson, "h")));
            }

            var rawEdges = new List<(string From, string To, string? Name, string? Condition)>();
            if (root.TryGetProperty("conexiones", out var edgesJson) && edgesJson.ValueKind == JsonValueKind.Array)
            {
                foreach (var edgeJson in edgesJson.EnumerateArray())
                {
                    var from = GetString(edgeJson, "de");
                    var to = GetString(edgeJson, "a");
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                    {
                        return WorkflowResult<FlowCanvasDto>.Invalid("Toda conexion necesita 'de' y 'a'.");
                    }
                    rawEdges.Add((from, to, GetString(edgeJson, "nombre"), GetString(edgeJson, "condicion")));
                }
            }

            // Nodos sin coordenadas: auto-layout de todo el grafo (mismo respaldo del import).
            var needsLayout = rawNodes.Any(n => n.X is null || n.Y is null);
            var layout = needsLayout
                ? WorkflowAutoLayout.Compute(
                    rawNodes.Select((n, i) => (n.Id, n.Type, i + 1)).ToList(),
                    rawEdges.Select(e => (e.From, e.To)).ToList())
                : null;
            var writerNodes = rawNodes.Select(n =>
            {
                var (dw, dh) = BpmnXmlWriter.DefaultSize(n.Type);
                var slot = layout is not null && layout.TryGetValue(n.Id, out var s)
                    ? s
                    : default((int X, int Y, int W, int H)?);
                return new BpmnWriterNode(n.Id, n.Label, n.Type,
                    n.X ?? slot?.X ?? 0, n.Y ?? slot?.Y ?? 0, n.W ?? dw, n.H ?? dh);
            }).ToList();
            var usedFlowIds = new HashSet<string>(StringComparer.Ordinal);
            var writerEdges = rawEdges
                .Select(e => new BpmnWriterEdge(NewFlowId(usedFlowIds), e.From, e.To, e.Name, e.Condition))
                .ToList();

            var xml = BpmnXmlWriter.Write(processCode, writerNodes, writerEdges);

            await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
            var imported = await _engine.ImportBpmnAsync(new ImportBpmnRequest(
                processCode, name.Trim(), xml, GetString(root, "descripcion")), cancellationToken);
            if (!imported.IsOk || imported.Value is null)
            {
                return WorkflowResult<FlowCanvasDto>.Invalid(imported.Error ?? "No se pudo importar el flujo.");
            }
            var category = GetString(root, "categoria");
            if (!string.IsNullOrWhiteSpace(category))
            {
                var created = await _db.WorkflowDefinitions.FirstAsync(d => d.Id == imported.Value.Id, cancellationToken);
                created.Category = category.Trim();
                await _db.SaveChangesAsync(cancellationToken);
            }
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            return WorkflowResult<FlowCanvasDto>.Ok((await GetCanvasAsync(imported.Value.Id, cancellationToken))!);
        }
    }

    // ---- Vinculos por nodo ----

    public async Task<IReadOnlyList<FlowRuleCatalogItemDto>> ListRuleCatalogAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.Rules.AsNoTracking()
            .Join(_db.RuleDocuments.AsNoTracking().Where(d => !d.IsArchived),
                r => r.DocumentId, d => d.Id, (r, d) => new { Rule = r, Document = d })
            .OrderBy(x => x.Document.Name).ThenBy(x => x.Rule.SortOrder).ThenBy(x => x.Rule.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new FlowRuleCatalogItemDto(
            x.Rule.Id, x.Rule.Name, x.Rule.VerbName, x.Rule.Status, x.Document.Name)).ToList();
    }

    public async Task<WorkflowResult<bool>> SetNodeFormAsync(Guid nodeId, Guid formDefinitionId, CancellationToken cancellationToken = default)
    {
        var node = await _db.WorkflowNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<bool>.NotFound("Nodo de flujo no encontrado.");
        }
        var form = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == formDefinitionId, cancellationToken);
        if (form is null)
        {
            return WorkflowResult<bool>.NotFound("Formulario no encontrado.");
        }
        if (form.Status != FormStatus.Active || form.IsArchived)
        {
            return WorkflowResult<bool>.Invalid("Solo se vinculan formularios publicados (activos).");
        }

        // A lo sumo un formulario por nodo (indice unico): upsert.
        var existing = await _db.WorkflowNodeForms.FirstOrDefaultAsync(f => f.NodeId == nodeId, cancellationToken);
        if (existing is null)
        {
            _db.WorkflowNodeForms.Add(new WorkflowNodeForm
            {
                TenantId = node.TenantId,
                NodeId = nodeId,
                DefinitionId = formDefinitionId
            });
        }
        else
        {
            existing.DefinitionId = formDefinitionId;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<bool>> RemoveNodeFormAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.WorkflowNodeForms.FirstOrDefaultAsync(f => f.NodeId == nodeId, cancellationToken);
        if (existing is null)
        {
            return WorkflowResult<bool>.NotFound("El nodo no tiene formulario vinculado.");
        }
        _db.WorkflowNodeForms.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<FlowNodeRuleDto>> AddNodeRuleAsync(Guid nodeId, Guid ruleId, CancellationToken cancellationToken = default)
    {
        var node = await _db.WorkflowNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<FlowNodeRuleDto>.NotFound("Nodo de flujo no encontrado.");
        }
        var rule = await _db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return WorkflowResult<FlowNodeRuleDto>.NotFound("Regla no encontrada.");
        }
        if (await _db.WorkflowNodeRules.AnyAsync(l => l.WorkflowNodeId == nodeId && l.RuleId == ruleId, cancellationToken))
        {
            return WorkflowResult<FlowNodeRuleDto>.Invalid("La regla ya esta vinculada a este nodo.");
        }
        var maxOrder = await _db.WorkflowNodeRules
            .Where(l => l.WorkflowNodeId == nodeId)
            .MaxAsync(l => (int?)l.SortOrder, cancellationToken) ?? 0;
        var link = new WorkflowNodeRule
        {
            TenantId = node.TenantId,
            WorkflowNodeId = nodeId,
            RuleId = ruleId,
            SortOrder = maxOrder + 1,
            IsAutonomous = true
        };
        _db.WorkflowNodeRules.Add(link);
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<FlowNodeRuleDto>.Ok(new FlowNodeRuleDto(
            link.Id, rule.Id, rule.Name, rule.VerbName, rule.Status, link.IsAutonomous));
    }

    public async Task<WorkflowResult<bool>> RemoveNodeRuleAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await _db.WorkflowNodeRules.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
        {
            return WorkflowResult<bool>.NotFound("Vinculo de regla no encontrado.");
        }
        _db.WorkflowNodeRules.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    public async Task<WorkflowResult<bool>> SetNodeRuleAutonomousAsync(Guid linkId, bool isAutonomous, CancellationToken cancellationToken = default)
    {
        var link = await _db.WorkflowNodeRules.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
        {
            return WorkflowResult<bool>.NotFound("Vinculo de regla no encontrado.");
        }
        link.IsAutonomous = isAutonomous;
        await _db.SaveChangesAsync(cancellationToken);
        return WorkflowResult<bool>.Ok(true);
    }

    // ---- Helpers ----

    private static string EstadoOf(WorkflowDefinition definition)
        => !definition.IsPublished ? EstadoBorrador : definition.IsPaused ? EstadoPausado : EstadoEnMarcha;

    private static string TipoOf(WorkflowNodeType type) => type switch
    {
        WorkflowNodeType.StartEvent => "startEvent",
        WorkflowNodeType.Task => "task",
        WorkflowNodeType.ExclusiveGateway => "exclusiveGateway",
        _ => "endEvent"
    };

    /// <summary>Acepta los nombres BPMN y los alias del prototipo (start/end/gw/task).</summary>
    private static WorkflowNodeType? ParseTipo(string? tipo) => tipo?.Trim().ToLowerInvariant() switch
    {
        "startevent" or "start" => WorkflowNodeType.StartEvent,
        "task" or "danger" => WorkflowNodeType.Task,
        "exclusivegateway" or "gateway" or "gw" => WorkflowNodeType.ExclusiveGateway,
        "endevent" or "end" => WorkflowNodeType.EndEvent,
        _ => null
    };

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(value.GetDouble())
            : null;

    private static string DefaultLabel(WorkflowNodeType type) => type switch
    {
        WorkflowNodeType.StartEvent => "Inicio",
        WorkflowNodeType.EndEvent => "Fin",
        WorkflowNodeType.ExclusiveGateway => "?",
        _ => "Nueva tarea"
    };

    private static string NewElementId(WorkflowNodeType type, HashSet<string> used)
    {
        var prefix = type switch
        {
            WorkflowNodeType.StartEvent => "Start_",
            WorkflowNodeType.EndEvent => "End_",
            WorkflowNodeType.ExclusiveGateway => "Gateway_",
            _ => "Task_"
        };
        string candidate;
        do
        {
            candidate = prefix + Guid.NewGuid().ToString("N")[..7];
        } while (!used.Add(candidate));
        return candidate;
    }

    private static string NewFlowId(HashSet<string> used)
    {
        string candidate;
        do
        {
            candidate = "Flow_" + Guid.NewGuid().ToString("N")[..7];
        } while (!used.Add(candidate));
        return candidate;
    }

    private static WorkflowResult<T> Fail<T>(WorkflowResult<bool> error)
        => new(error.Status, default, error.Error);

    private readonly record struct GuardedDefinition(WorkflowDefinition? Definition, WorkflowResult<bool>? Error);

    /// <summary>Carga la definicion y exige que sea BORRADOR (regla del editor, ADR-0022).</summary>
    private async Task<GuardedDefinition> LoadDraftAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var definition = await _db.WorkflowDefinitions.FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return new GuardedDefinition(null, WorkflowResult<bool>.NotFound("Definicion de flujo no encontrada."));
        }
        if (definition.IsArchived)
        {
            return new GuardedDefinition(null, WorkflowResult<bool>.Invalid("La definicion esta archivada."));
        }
        if (definition.IsPublished)
        {
            return new GuardedDefinition(null, WorkflowResult<bool>.Invalid(
                "El grafo de una definicion publicada no se edita: usa la version borrador (EnsureDraft)."));
        }
        return new GuardedDefinition(definition, null);
    }

    private async Task<WorkflowResult<bool>> MutateNodeAsync(
        Guid nodeId, Func<WorkflowNode, string?> mutate, CancellationToken cancellationToken)
    {
        var node = await _db.WorkflowNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<bool>.NotFound("Nodo de flujo no encontrado.");
        }
        var guarded = await LoadDraftAsync(node.DefinitionId, cancellationToken);
        if (guarded.Error is not null)
        {
            return guarded.Error;
        }
        var error = mutate(node);
        if (error is not null)
        {
            return WorkflowResult<bool>.Invalid(error);
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);
        await RegenerateXmlAsync(guarded.Definition!, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return WorkflowResult<bool>.Ok(true);
    }

    /// <summary>
    /// Regenera el BpmnXml de la definicion desde el grafo materializado EN EL CHANGE
    /// TRACKER (incluye altas/bajas pendientes del llamador): process + bpmndi con las
    /// coordenadas actuales, para conservar la portabilidad bpmn.io (ADR-0014/0022).
    /// </summary>
    private async Task RegenerateXmlAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        // Carga tracked + pendientes locales (Local incluye Added; los Removed ya no estan).
        await _db.WorkflowNodes.Where(n => n.DefinitionId == definition.Id).LoadAsync(cancellationToken);
        await _db.WorkflowEdges.Where(e => e.DefinitionId == definition.Id).LoadAsync(cancellationToken);
        var nodes = _db.WorkflowNodes.Local
            .Where(n => n.DefinitionId == definition.Id)
            .OrderBy(n => n.StepNumber).ThenBy(n => n.BpmnElementId, StringComparer.Ordinal)
            .ToList();
        var edges = _db.WorkflowEdges.Local
            .Where(e => e.DefinitionId == definition.Id)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.BpmnElementId, StringComparer.Ordinal)
            .ToList();
        // MERGE, no reescritura: se sincroniza SOLO el grafo del motor dentro del XML existente. Antes esto
        // regeneraba el documento entero y borraba todo lo que el motor no modela (objeto de datos, almacen,
        // grupo, subproceso, pool, anotaciones...), que el graficador ahora ofrece para DOCUMENTAR.
        definition.BpmnXml = MergeXml(definition.BpmnXml, definition.ProcessCode, nodes, edges);
    }

    /// <summary>
    /// Serializa el grafo materializado a XML BPMN con DI (asigna BpmnElementId a las
    /// aristas que no tengan, mutandolas). Publico para reuso del seeder (backfill de
    /// definiciones anteriores al layout).
    /// </summary>
    public static string WriteXml(string processCode, IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges)
    {
        var (writerNodes, writerEdges) = ToWriterGraph(nodes, edges);
        return BpmnXmlWriter.Write(processCode, writerNodes, writerEdges);
    }

    /// <summary>
    /// Sincroniza el grafo del motor DENTRO del XML existente en vez de regenerarlo: preserva las figuras
    /// que el motor no modela (objeto de datos, almacen, grupo, subproceso, pool, anotaciones), que el
    /// graficador ofrece para DOCUMENTAR el proceso. Cae a <see cref="WriteXml"/> si no hay XML utilizable.
    /// </summary>
    public static string MergeXml(
        string? existingXml, string processCode,
        IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges)
    {
        var (writerNodes, writerEdges) = ToWriterGraph(nodes, edges);
        return BpmnXmlMerger.Merge(existingXml, processCode, writerNodes, writerEdges);
    }

    /// <summary>Convierte el grafo materializado (entidades) al modelo del serializador BPMN.</summary>
    private static (List<BpmnWriterNode> Nodes, List<BpmnWriterEdge> Edges) ToWriterGraph(
        IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<WorkflowEdge> edges)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var writerNodes = nodes.Select(n =>
        {
            var (dw, dh) = BpmnXmlWriter.DefaultSize(n.NodeType);
            return new BpmnWriterNode(n.BpmnElementId, n.Name, n.NodeType, n.X, n.Y, n.W ?? dw, n.H ?? dh);
        }).ToList();
        foreach (var edge in edges.Where(e => e.BpmnElementId is not null))
        {
            used.Add(edge.BpmnElementId!);
        }
        var writerEdges = new List<BpmnWriterEdge>();
        foreach (var edge in edges)
        {
            // Aristas heredadas sin id: se les asigna uno estable ANTES de serializar.
            edge.BpmnElementId ??= NewFlowId(used);
            if (!byId.ContainsKey(edge.SourceNodeId) || !byId.ContainsKey(edge.TargetNodeId))
            {
                continue;
            }
            writerEdges.Add(new BpmnWriterEdge(
                edge.BpmnElementId, byId[edge.SourceNodeId].BpmnElementId,
                byId[edge.TargetNodeId].BpmnElementId, edge.Name, edge.ConditionExpression));
        }
        return (writerNodes, writerEdges);
    }

    private async Task<FlowCanvasDto?> BuildCanvasAsync(WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var nodes = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == definition.Id).OrderBy(n => n.StepNumber)
            .ToListAsync(cancellationToken);
        var edges = await _db.WorkflowEdges.AsNoTracking()
            .Where(e => e.DefinitionId == definition.Id).OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
        var nodeIds = nodes.Select(n => n.Id).ToList();

        var forms = await _db.WorkflowNodeForms.AsNoTracking()
            .Where(f => nodeIds.Contains(f.NodeId))
            .Join(_db.FormDefinitions.AsNoTracking(), f => f.DefinitionId, d => d.Id,
                (f, d) => new { f.NodeId, d.Id, d.Code, d.Title })
            .ToListAsync(cancellationToken);
        var formsByNode = forms.ToDictionary(f => f.NodeId);

        var rules = await _db.WorkflowNodeRules.AsNoTracking()
            .Where(l => nodeIds.Contains(l.WorkflowNodeId))
            .Join(_db.Rules.AsNoTracking(), l => l.RuleId, r => r.Id,
                (l, r) => new { l.WorkflowNodeId, Link = l, Rule = r })
            .ToListAsync(cancellationToken);
        var rulesByNode = rules
            .GroupBy(x => x.WorkflowNodeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FlowNodeRuleDto>)g
                .OrderBy(x => x.Link.SortOrder)
                .Select(x => new FlowNodeRuleDto(
                    x.Link.Id, x.Rule.Id, x.Rule.Name, x.Rule.VerbName, x.Rule.Status, x.Link.IsAutonomous))
                .ToList());

        var nodeDtos = nodes.Select(n =>
        {
            var (dw, dh) = BpmnXmlWriter.DefaultSize(n.NodeType);
            var form = formsByNode.GetValueOrDefault(n.Id);
            return new FlowCanvasNodeDto(
                n.Id, n.BpmnElementId, n.Name, n.NodeType, n.X, n.Y, n.W ?? dw, n.H ?? dh,
                n.AllowsAssignment, n.RestartNodeId,
                form?.Id, form?.Code, form?.Title,
                rulesByNode.GetValueOrDefault(n.Id) ?? [],
                n.Color, n.Note);
        }).ToList();
        var edgeDtos = edges.Select(e => new FlowCanvasEdgeDto(
            e.Id, e.SourceNodeId, e.TargetNodeId, e.BpmnElementId, e.Name, e.ConditionExpression)).ToList();

        return new FlowCanvasDto(
            definition.Id, definition.ProcessCode, definition.Version, definition.Name,
            definition.Category, definition.Description, definition.IsPublished,
            definition.IsPaused, definition.IsArchived, EstadoOf(definition),
            IsEditable: !definition.IsPublished && !definition.IsArchived,
            nodeDtos, edgeDtos);
    }

    /// <summary>Se une a la transaccion del llamador si ya hay una abierta (null = unida).</summary>
    private async Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(CancellationToken cancellationToken)
        => _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
}
