using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ecorex.Application.Forms;

/// <summary>
/// Implementacion de IFormResponseService (ADR-0015). El documento de datos se serializa
/// como { fieldCode: { value, type } } (claves del documento = FieldCode literal, sin
/// transformar). El submit re-valida TODO en servidor con FormFieldValidator y, si hay
/// FormFlowLink Pending, completa el paso del flujo via IWorkflowEngine dentro de la misma
/// transaccion (el motor se une a la transaccion abierta, patron HasActiveTransaction).
/// </summary>
public sealed class FormResponseService : IFormResponseService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string ConflictMessage = "Otro usuario modifico la respuesta. Recarga e intenta de nuevo.";

    private readonly IApplicationDbContext _db;
    private readonly IWorkflowEngine _workflowEngine;

    public FormResponseService(IApplicationDbContext db, IWorkflowEngine workflowEngine)
    {
        _db = db;
        _workflowEngine = workflowEngine;
    }

    public async Task<FormResult<FormResponseDto>> GetOrCreateDraftAsync(Guid definitionId, string? reference, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<FormResponseDto>.NotFound("Formulario no encontrado.");
        }
        if (definition.Status != FormStatus.Active || definition.IsArchived)
        {
            return FormResult<FormResponseDto>.Invalid("El formulario no esta activo.");
        }

        var normalizedReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (normalizedReference is not null)
        {
            var existing = await _db.FormResponses.AsNoTracking()
                .Where(r => r.DefinitionId == definitionId
                    && r.Reference == normalizedReference
                    && r.Status == FormResponseStatus.Draft)
                .OrderBy(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return FormResult<FormResponseDto>.Ok(ToDto(existing));
            }
        }

        var response = new FormResponse
        {
            TenantId = definition.TenantId,
            DefinitionId = definitionId,
            Reference = normalizedReference,
            Status = FormResponseStatus.Draft,
            Data = "{}"
        };
        _db.FormResponses.Add(response);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormResponseDto>.Ok(ToDto(response));
    }

    public async Task<FormResponseDto?> GetAsync(Guid responseId, CancellationToken cancellationToken = default)
    {
        var response = await _db.FormResponses.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        return response is null ? null : ToDto(response);
    }

    public async Task<FormResult<FormResponseDto>> SaveAsync(
        Guid responseId, IReadOnlyDictionary<string, FormFieldValue> data, bool submit,
        Guid? submittedByTenantUserId = null, string? approvalResult = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null)
        {
            return FormResult<FormResponseDto>.NotFound("Respuesta no encontrada.");
        }
        if (response.Status == FormResponseStatus.Submitted)
        {
            return FormResult<FormResponseDto>.Invalid("La respuesta ya fue enviada y no puede modificarse.");
        }

        var questions = await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == response.DefinitionId)
            .OrderBy(q => q.SortOrder)
            .ToListAsync(cancellationToken);
        var questionsByCode = questions.ToDictionary(q => q.FieldCode, StringComparer.Ordinal);

        // Solo se persisten claves que existen en la definicion (documento canonico).
        var document = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        foreach (var (fieldCode, value) in data)
        {
            if (questionsByCode.TryGetValue(fieldCode, out var question)
                && !FormFieldValidator.IsNonInput(question.ControlType))
            {
                document[fieldCode] = new FormFieldValue(value.Value, question.ControlType.ToString());
            }
        }

        // Tablas en SERVIDOR (ola F2, doc 01 D5): formula por fila + roll-up de columnas al
        // encabezado, con el helper compartido con el renderer. Persiste las filas computadas.
        foreach (var question in questions.Where(q => q.ControlType == FormControlType.GridDetail))
        {
            var cols = Calc.FormGridCalculator.ParseColumns(question.OptionsJson);
            if (cols.Count == 0) { continue; }
            document.TryGetValue(question.FieldCode, out var gridField);
            var gridRows = FormFieldValidator.ParseGridRows(gridField?.Value)
                .Select(r => new Dictionary<string, string?>(r, StringComparer.Ordinal)).ToList();
            var (computed, rollups) = Calc.FormGridCalculator.Recompute(gridRows, cols);
            document[question.FieldCode] = new FormFieldValue(
                computed.Count == 0 ? null : JsonSerializer.Serialize(computed, JsonOptions),
                question.ControlType.ToString());
            foreach (var (field, total) in rollups)
            {
                var type = questionsByCode.TryGetValue(field, out var tq) ? tq.ControlType.ToString() : FormControlType.Text.ToString();
                document[field] = new FormFieldValue(total, type);
            }
        }

        // Calculo en SERVIDOR (ola F2, doc 01 D5): recomputa los campos con CalcExpression con el
        // MISMO evaluador tipado del cliente. El cliente NO es fuente de verdad para montos: su
        // valor se descarta y se persiste el del servidor.
        var calcValues = document.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);
        foreach (var question in questions.Where(q => !string.IsNullOrWhiteSpace(q.CalcExpression)))
        {
            var computed = Calc.FormExpressionEvaluator.Evaluate(question.CalcExpression, calcValues)
                ?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            document[question.FieldCode] = new FormFieldValue(computed, question.ControlType.ToString());
            calcValues[question.FieldCode] = computed;
        }

        if (submit)
        {
            // VALIDACION SERVIDOR completa por tipo, con errores por fieldCode.
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var question in questions)
            {
                // Campos ocultos por el disenador (ADR-0021): no se pintan, no se validan.
                if (question.IsHidden)
                {
                    continue;
                }
                if (FormFieldValidator.IsNonInput(question.ControlType))
                {
                    continue;
                }
                document.TryGetValue(question.FieldCode, out var field);
                var error = FormFieldValidator.Validate(
                    question.ControlType, question.Required, field?.Value,
                    FormFieldValidator.ParseOptions(question.OptionsJson),
                    FormFieldValidator.ParseRules(question.ValidationJson));
                if (error is not null)
                {
                    errors[question.FieldCode] = error;
                }
            }
            if (errors.Count > 0)
            {
                return FormResult<FormResponseDto>.ValidationFailed(errors);
            }
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        response.Data = JsonSerializer.Serialize(document, JsonOptions);
        if (submit)
        {
            response.Status = FormResponseStatus.Submitted;
            response.SubmittedAt = DateTimeOffset.UtcNow;
            response.SubmittedByTenantUserId = submittedByTenantUserId;

            // Integracion con el flujo: cada link Pending completa SU paso current del
            // workflow (misma transaccion logica; si el motor falla, rollback total).
            var pendingLinks = await _db.FormFlowLinks
                .Where(l => l.FormResponseId == response.Id && l.Status == FormFlowLinkStatus.Pending)
                .ToListAsync(cancellationToken);
            foreach (var link in pendingLinks)
            {
                var currentSteps = await _workflowEngine.GetCurrentStepsAsync(link.WorkflowInstanceId, cancellationToken);
                var step = currentSteps.FirstOrDefault(s =>
                    s.NodeId == link.WorkflowNodeId && s.Status == WorkflowStepStatus.Pending);
                if (step is not null)
                {
                    // approvalResult (decision capturada junto al formulario): el paso lleva la
                    // decision y el motor resuelve la compuerta adelante en su cascada (ADR-0037).
                    var completed = await _workflowEngine.CompleteStepAsync(
                        link.WorkflowInstanceId, step.Id, submittedByTenantUserId,
                        approvalResult: approvalResult,
                        cancellationToken: cancellationToken);
                    if (!completed.IsOk && completed.Status != WorkflowEngineStatus.StuckDetected)
                    {
                        return FormResult<FormResponseDto>.Invalid(
                            completed.Error ?? "No se pudo completar el paso del flujo vinculado.");
                    }
                }
                // Si el paso ya no esta vigente (reinicio/rechazo posterior), el link se
                // cierra igualmente: el formulario quedo respondido para ese ciclo.
                link.Status = FormFlowLinkStatus.Completed;
            }
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FormResult<FormResponseDto>.Conflict(ConflictMessage);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return FormResult<FormResponseDto>.Ok(ToDto(response));
    }

    public async Task<IReadOnlyList<TaskStepFormDto>> GetTaskStepFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskItemId, cancellationToken);
        if (task?.WorkflowInstanceId is not Guid instanceId)
        {
            return [];
        }

        var currentSteps = await _workflowEngine.GetCurrentStepsAsync(instanceId, cancellationToken);
        var pendingSteps = currentSteps
            .Where(s => s.Status == WorkflowStepStatus.Pending)
            .ToList();
        if (pendingSteps.Count == 0)
        {
            return [];
        }

        var nodeIds = pendingSteps.Select(s => s.NodeId).ToList();
        var nodeForms = await _db.WorkflowNodeForms.AsNoTracking()
            .Where(f => nodeIds.Contains(f.NodeId))
            .ToListAsync(cancellationToken);
        if (nodeForms.Count == 0)
        {
            return [];
        }

        // Compuerta adelante y opciones de decision del nodo con formulario (misma logica pura
        // que la bandeja, ADR-0036/0037): la UI del formulario pide la decision junto al form.
        var definitionId = await _db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == instanceId).Select(i => i.DefinitionId)
            .FirstAsync(cancellationToken);
        var edges = (await _db.WorkflowEdges.AsNoTracking()
            .Where(e => e.DefinitionId == definitionId)
            .Select(e => new { e.SourceNodeId, e.TargetNodeId, e.Name })
            .ToListAsync(cancellationToken))
            .Select(e => new WorkflowInboxProjection.EdgeRow(e.SourceNodeId, e.TargetNodeId, e.Name))
            .ToList();
        var gatewayNodeIds = (await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == definitionId && n.NodeType == WorkflowNodeType.ExclusiveGateway)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var result = new List<TaskStepFormDto>();
        foreach (var step in pendingSteps)
        {
            var nodeForm = nodeForms.FirstOrDefault(f => f.NodeId == step.NodeId);
            if (nodeForm is null)
            {
                continue;
            }
            var definition = await _db.FormDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == nodeForm.DefinitionId, cancellationToken);
            if (definition is null || definition.Status != FormStatus.Active || definition.IsArchived)
            {
                continue;
            }

            // Borrador (idempotente) anclado al numero de la tarea + link al paso.
            var draft = await GetOrCreateDraftAsync(definition.Id, task.Number, cancellationToken);
            if (!draft.IsOk || draft.Value is null)
            {
                continue;
            }
            var link = await _db.FormFlowLinks
                .FirstOrDefaultAsync(l => l.WorkflowInstanceId == instanceId
                    && l.WorkflowNodeId == step.NodeId
                    && l.FormResponseId == draft.Value.Id, cancellationToken);
            if (link is null)
            {
                link = new FormFlowLink
                {
                    TenantId = task.TenantId,
                    FormResponseId = draft.Value.Id,
                    WorkflowInstanceId = instanceId,
                    WorkflowNodeId = step.NodeId,
                    Status = FormFlowLinkStatus.Pending
                };
                _db.FormFlowLinks.Add(link);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var (isGatewayAhead, approvalOptions) =
                WorkflowInboxProjection.ResolveGatewayAhead(step.NodeId, edges, gatewayNodeIds);

            result.Add(new TaskStepFormDto(
                draft.Value.Id, definition.Id, definition.Code, definition.Title,
                instanceId, step.NodeId, step.NodeName,
                link.Status, draft.Value.Status, draft.Value.Reference,
                isGatewayAhead, approvalOptions));
        }
        return result;
    }

    // ---- Helpers ----

    private static FormResponseDto ToDto(FormResponse response)
        => new(response.Id, response.DefinitionId, response.Reference, response.Status,
            ParseDocument(response.Data), response.SubmittedAt, response.SubmittedByTenantUserId,
            response.Version);

    /// <summary>Deserializa el documento { fieldCode: { value, type } }; vacio si es invalido.</summary>
    public static IReadOnlyDictionary<string, FormFieldValue> ParseDocument(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FormFieldValue>>(data, JsonOptions)
                ?? new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        }
    }

    /// <summary>Se une a la transaccion del llamador si ya hay una abierta (null = unida).</summary>
    private async Task<IDbContextTransaction?> BeginTransactionIfNoneAsync(CancellationToken cancellationToken)
        => _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
}
