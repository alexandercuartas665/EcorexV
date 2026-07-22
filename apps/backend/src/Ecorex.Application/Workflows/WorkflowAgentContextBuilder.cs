using Ecorex.Application.Common;
using Ecorex.Application.Forms;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Implementacion de IWorkflowAgentContextBuilder (ola 1). Consulta en bloques y recorta segun
/// WorkflowAgentContextLimits. El aislamiento por tenant es del filtro global: aqui NO se filtra
/// a mano por TenantId (regla 1 de CLAUDE.md), asi que un paso ajeno cae como NotFound.
/// </summary>
public sealed class WorkflowAgentContextBuilder : IWorkflowAgentContextBuilder
{
    private readonly IApplicationDbContext _db;

    public WorkflowAgentContextBuilder(IApplicationDbContext db) => _db = db;

    public async Task<WorkflowResult<WorkflowAgentContextDto>> BuildAsync(
        Guid stepId, CancellationToken cancellationToken = default)
    {
        var step = await _db.WorkflowStepHistories.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null)
        {
            return WorkflowResult<WorkflowAgentContextDto>.NotFound("Paso de flujo no encontrado.");
        }
        if (!step.IsCurrent)
        {
            return WorkflowResult<WorkflowAgentContextDto>.Invalid(
                "El paso no esta vigente: solo se arma contexto para el paso en curso.");
        }

        var node = await _db.WorkflowNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == step.NodeId, cancellationToken);
        if (node is null)
        {
            return WorkflowResult<WorkflowAgentContextDto>.NotFound("Nodo del paso no encontrado.");
        }

        var nodeDto = await BuildNodeAsync(node, cancellationToken);
        var assignment = await BuildAssignmentAsync(node.Id, cancellationToken);
        var (historyDto, stepsById) = await BuildHistoryAsync(step, cancellationToken);
        var priorData = await BuildPriorDataAsync(step, stepsById, cancellationToken);
        var taskDto = await BuildTaskAsync(step.InstanceId, cancellationToken);

        return WorkflowResult<WorkflowAgentContextDto>.Ok(new WorkflowAgentContextDto(
            step.InstanceId, step.Id, nodeDto, priorData, taskDto, historyDto, assignment));
    }

    // ---- (a) Nodo actual + formulario asociado con la definicion de sus campos ----

    private async Task<WorkflowAgentNodeDto> BuildNodeAsync(
        Domain.Entities.WorkflowNode node, CancellationToken cancellationToken)
    {
        var link = await _db.WorkflowNodeForms.AsNoTracking()
            .Where(f => f.NodeId == node.Id)
            .Join(_db.FormDefinitions.AsNoTracking(), f => f.DefinitionId, d => d.Id,
                (f, d) => new { d.Id, d.Code, d.Title, d.Description })
            .FirstOrDefaultAsync(cancellationToken);

        WorkflowAgentFormDto? form = null;
        if (link is not null)
        {
            // Se piden MaxFieldsPerForm+1 para saber si habia mas sin traer el formulario entero.
            var fields = await _db.FormQuestions.AsNoTracking()
                .Where(q => q.DefinitionId == link.Id)
                .OrderBy(q => q.SortOrder).ThenBy(q => q.Label)
                .Take(WorkflowAgentContextLimits.MaxFieldsPerForm + 1)
                .Select(q => new WorkflowAgentFieldDto(
                    q.FieldCode, q.Label, q.ControlType, q.Required, q.HelpText, q.OptionsJson))
                .ToListAsync(cancellationToken);

            var truncated = fields.Count > WorkflowAgentContextLimits.MaxFieldsPerForm;
            if (truncated)
            {
                fields.RemoveAt(fields.Count - 1);
            }
            form = new WorkflowAgentFormDto(
                link.Id, link.Code, link.Title, Clip(link.Description, WorkflowAgentContextLimits.MaxTextChars),
                fields, truncated);
        }

        // El nodo no tiene columna Description: Note es su unico texto libre (post-it del lienzo).
        return new WorkflowAgentNodeDto(
            node.Id, node.BpmnElementId, node.Name,
            Clip(node.Note, WorkflowAgentContextLimits.MaxTextChars),
            node.NodeType, node.StepNumber, form);
    }

    private async Task<WorkflowAgentAssignmentDto?> BuildAssignmentAsync(
        Guid nodeId, CancellationToken cancellationToken)
        => await _db.WorkflowNodeAgents.AsNoTracking()
            .Where(x => x.NodeId == nodeId)
            .Join(_db.AiAgents.AsNoTracking(), x => x.AiAgentId, a => a.Id,
                (x, a) => new WorkflowAgentAssignmentDto(a.Id, a.Name, a.Role, a.IsActive, x.Autonomy))
            .FirstOrDefaultAsync(cancellationToken);

    // ---- (d) Historial de pasos (se arma antes que (b): sus nodos nombran los envios previos) ----

    private async Task<(WorkflowAgentHistoryDto History, Dictionary<Guid, string?> NodeNames)> BuildHistoryAsync(
        Domain.Entities.WorkflowStepHistory step, CancellationToken cancellationToken)
    {
        var total = await _db.WorkflowStepHistories.AsNoTracking()
            .CountAsync(s => s.InstanceId == step.InstanceId, cancellationToken);

        // Ventana MAS RECIENTE: en un caso con loops, los ciclos viejos aportan menos que el actual.
        var rows = await _db.WorkflowStepHistories.AsNoTracking()
            .Where(s => s.InstanceId == step.InstanceId)
            .OrderByDescending(s => s.CycleIndex).ThenByDescending(s => s.CreatedAt)
            .Take(WorkflowAgentContextLimits.MaxHistorySteps)
            .Select(s => new
            {
                s.Id,
                s.NodeId,
                s.CycleIndex,
                s.Status,
                s.IsCurrent,
                s.ApprovalResult,
                s.ApprovalComment,
                s.ExecutedByTenantUserId,
                s.CompletedAt,
                s.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var nodeIds = rows.Select(r => r.NodeId).Distinct().ToList();
        var nodeNames = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => nodeIds.Contains(n.Id))
            .Select(n => new { n.Id, n.Name })
            .ToDictionaryAsync(n => n.Id, n => n.Name, cancellationToken);

        // Correo del ejecutor: para el agente "quien lo hizo" es mas util que un Guid.
        var userIds = rows.Where(r => r.ExecutedByTenantUserId is not null)
            .Select(r => r.ExecutedByTenantUserId!.Value).Distinct().ToList();
        var emails = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.TenantUsers.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        // Se devuelve en orden cronologico ascendente: se lee como un relato del caso.
        var steps = rows
            .OrderBy(r => r.CycleIndex).ThenBy(r => r.CreatedAt)
            .Select(r => new WorkflowAgentHistoryStepDto(
                r.Id, r.NodeId, nodeNames.GetValueOrDefault(r.NodeId), r.CycleIndex, r.Status, r.IsCurrent,
                r.ApprovalResult, Clip(r.ApprovalComment, WorkflowAgentContextLimits.MaxTextChars),
                r.ExecutedByTenantUserId is Guid uid ? emails.GetValueOrDefault(uid) : null,
                r.CompletedAt))
            .ToList();

        var history = new WorkflowAgentHistoryDto(
            steps, total, total > WorkflowAgentContextLimits.MaxHistorySteps);
        return (history, nodeNames);
    }

    // ---- (b) Datos ya capturados en pasos ANTERIORES de la misma instancia ----

    private async Task<WorkflowAgentPriorDataDto> BuildPriorDataAsync(
        Domain.Entities.WorkflowStepHistory step,
        Dictionary<Guid, string?> nodeNames,
        CancellationToken cancellationToken)
    {
        // Los envios de la instancia se conocen por FormFlowLink (respuesta <-> instancia/nodo).
        // Solo Completed: un link Pending es el formulario que AUN no se ha llenado, no un dato.
        // Se excluye el nodo del paso actual: eso es lo que el agente debe producir, no leer.
        var linkQuery = _db.FormFlowLinks.AsNoTracking()
            .Where(l => l.WorkflowInstanceId == step.InstanceId
                && l.WorkflowNodeId != step.NodeId
                && l.Status == FormFlowLinkStatus.Completed);

        var total = await linkQuery.CountAsync(cancellationToken);

        var rows = await linkQuery
            .Join(_db.FormResponses.AsNoTracking(), l => l.FormResponseId, r => r.Id,
                (l, r) => new { l.WorkflowNodeId, Response = r })
            .Join(_db.FormDefinitions.AsNoTracking(), x => x.Response.DefinitionId, d => d.Id,
                (x, d) => new { x.WorkflowNodeId, x.Response, DefinitionId = d.Id, d.Code, d.Title })
            // Los envios MAS RECIENTES son los que pesan; si sobran, se cortan los viejos.
            .OrderByDescending(x => x.Response.SubmittedAt ?? x.Response.CreatedAt)
            .Take(WorkflowAgentContextLimits.MaxPriorForms)
            .ToListAsync(cancellationToken);

        // Etiquetas de los campos: sin ellas el agente ve pares codigo/valor sin significado.
        var definitionIds = rows.Select(x => x.DefinitionId).Distinct().ToList();
        var labels = definitionIds.Count == 0
            ? []
            : await _db.FormQuestions.AsNoTracking()
                .Where(q => definitionIds.Contains(q.DefinitionId))
                .Select(q => new { q.DefinitionId, q.FieldCode, q.Label })
                .ToListAsync(cancellationToken);
        var labelsByDefinition = labels
            .GroupBy(l => l.DefinitionId)
            .ToDictionary(g => g.Key, g => g
                .GroupBy(l => l.FieldCode, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First().Label, StringComparer.Ordinal));

        var forms = new List<WorkflowAgentPriorFormDto>(rows.Count);
        foreach (var row in rows.OrderBy(x => x.Response.SubmittedAt ?? x.Response.CreatedAt))
        {
            var byField = labelsByDefinition.GetValueOrDefault(row.DefinitionId);
            var document = FormResponseService.ParseDocument(row.Response.Data);
            var answers = document
                .Take(WorkflowAgentContextLimits.MaxFieldsPerForm)
                .Select(kv => new WorkflowAgentAnswerDto(
                    kv.Key,
                    byField?.GetValueOrDefault(kv.Key),
                    Clip(kv.Value.Value, WorkflowAgentContextLimits.MaxValueChars)))
                .ToList();

            forms.Add(new WorkflowAgentPriorFormDto(
                row.Response.Id, row.WorkflowNodeId, nodeNames.GetValueOrDefault(row.WorkflowNodeId),
                row.Code, row.Title, row.Response.SubmittedAt, answers,
                document.Count > WorkflowAgentContextLimits.MaxFieldsPerForm));
        }

        return new WorkflowAgentPriorDataDto(forms, total > WorkflowAgentContextLimits.MaxPriorForms);
    }

    // ---- (c) Tarea que disparo el flujo + tercero/cliente ----

    private async Task<WorkflowAgentTaskDto?> BuildTaskAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var taskId = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => i.TaskItemId)
            .FirstOrDefaultAsync(cancellationToken);
        if (taskId is not Guid id)
        {
            return null;
        }
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        // El tercero NO cuelga de TaskItem: se deriva del concepto (subcategoria 000270) que
        // clasifica la tarea, via ActividadSubcategoriaTercero. Solo se resuelve cuando la
        // subcategoria apunta a UN unico tercero; si son varios, el "cliente del caso" es
        // ambiguo y se prefiere no darle al agente un dato que podria ser falso.
        WorkflowAgentTerceroDto? tercero = null;
        if (task.SubcategoriaId is Guid subcategoriaId)
        {
            var terceroIds = await _db.ActividadSubcategoriaTerceros.AsNoTracking()
                .Where(x => x.SubcategoriaId == subcategoriaId)
                .Select(x => x.TerceroId)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (terceroIds.Count == 1)
            {
                tercero = await _db.Terceros.AsNoTracking()
                    .Where(t => t.Id == terceroIds[0])
                    .Select(t => new WorkflowAgentTerceroDto(
                        t.Id, t.Nombre, t.Tipo, t.IdValor, t.Email, t.Telefono, t.Ciudad))
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        return new WorkflowAgentTaskDto(
            task.Id, task.Number, task.Title,
            Clip(task.Description, WorkflowAgentContextLimits.MaxTextChars),
            task.Status, task.Priority, task.DueDate,
            task.RequesterName, task.RequesterEmail, tercero);
    }

    /// <summary>Recorta un texto al tope y marca el corte para que el modelo lo note.</summary>
    private static string? Clip(string? text, int maxChars)
        => text is null || text.Length <= maxChars ? text : string.Concat(text.AsSpan(0, maxChars), "...");
}
