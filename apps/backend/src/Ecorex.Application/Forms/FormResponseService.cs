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
    private readonly Tenancy.ISequenceService _sequences;
    private readonly Common.ITenantContext _tenant;
    private readonly Tenancy.IFormRecordBroadcaster _recordBroadcaster;

    public FormResponseService(
        IApplicationDbContext db, IWorkflowEngine workflowEngine, Tenancy.ISequenceService sequences,
        Common.ITenantContext tenant, Tenancy.IFormRecordBroadcaster recordBroadcaster)
    {
        _db = db;
        _workflowEngine = workflowEngine;
        _sequences = sequences;
        _tenant = tenant;
        _recordBroadcaster = recordBroadcaster;
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

    public async Task<FormResult<FormResponseDto>> SetReferenceAsync(
        Guid responseId, string reference, CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (normalized is null)
        {
            return FormResult<FormResponseDto>.Invalid("La referencia no puede estar vacia.");
        }

        var response = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null)
        {
            return FormResult<FormResponseDto>.NotFound("Respuesta no encontrada.");
        }

        // No destructivo: si ya quedo anclada (p.ej. la respuesta del paso del flujo), se respeta.
        if (string.IsNullOrWhiteSpace(response.Reference))
        {
            response.Reference = normalized;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return FormResult<FormResponseDto>.Ok(ToDto(response));
    }

    public async Task<FormResult<FormResponseDto>> SaveAsync(
        Guid responseId, IReadOnlyDictionary<string, FormFieldValue> data, bool submit,
        Guid? submittedByTenantUserId = null, string? approvalResult = null,
        IReadOnlyCollection<string>? hiddenFieldCodes = null,
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
                // Campos ocultos por REGLA en runtime (D4): el renderer evalua las reglas de
                // visibilidad y manda que campos quedaron ocultos; no se exigen (p.ej. "Valor"
                // cuando "Concreto una venta? = No"). Sin esto, un campo condicional oculto pero
                // Required bloqueaba el envio SIN mostrar el error (el campo no se pinta).
                if (hiddenFieldCodes is not null && hiddenFieldCodes.Contains(question.FieldCode))
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
                    FormFieldValidator.ParseRules(question.ValidationJson),
                    question.OptionsJson);
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

        // Registro transaccional (ola F3, doc 01 D2/D3): confirmar = enviar. La identidad se
        // resuelve ANTES de abrir la transaccion (patron de ISequenceService: EnsureSequence +
        // NextAsync fuera de la tx del caso de uso, para no abortar por el INSERT del consecutivo).
        // Idempotente: si el registro ya esta Confirmed no reasigna.
        string? recordNumber = null;
        string? recordFormCode = null;
        var assignRecord = false;
        if (submit)
        {
            var definition = await _db.FormDefinitions
                .FirstOrDefaultAsync(d => d.Id == response.DefinitionId, cancellationToken);
            if (definition?.IsTransactional == true && response.RecordStatus != FormRecordStatus.Confirmed)
            {
                var identity = await ResolveIdentityAsync(definition, document, cancellationToken);
                if (!identity.Ok)
                {
                    return FormResult<FormResponseDto>.ValidationFailed(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [definition.IdentitySourceFieldCode ?? "_identidad"] = identity.Error!
                        });
                }
                recordNumber = identity.Number;
                recordFormCode = definition.Code;
                assignRecord = true;
            }
        }

        await using var transaction = await BeginTransactionIfNoneAsync(cancellationToken);

        response.Data = JsonSerializer.Serialize(document, JsonOptions);
        if (submit)
        {
            response.Status = FormResponseStatus.Submitted;
            response.SubmittedAt = DateTimeOffset.UtcNow;
            response.SubmittedByTenantUserId = submittedByTenantUserId;

            // Registro transaccional (ola F3): identidad ya resuelta antes de la transaccion.
            if (assignRecord)
            {
                response.RecordNumber = recordNumber;
                response.RecordStatus = FormRecordStatus.Confirmed;
                response.TransactionDate = DateTimeOffset.UtcNow;
            }

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
        catch (DbUpdateException) when (submit)
        {
            // Choca el indice unico de record_number (clave natural duplicada por tenant+definicion).
            return FormResult<FormResponseDto>.ValidationFailed(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["_identidad"] = "Ya existe un registro con esa clave (numero duplicado)."
                });
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        // Bandeja en vivo (ola F4): tras confirmar un registro, avisa a la bandeja /m/{code}.
        if (assignRecord && recordFormCode is not null && _tenant.TenantId is Guid tid)
        {
            await _recordBroadcaster.RecordConfirmedAsync(tid, recordFormCode, recordNumber ?? "", cancellationToken);
        }

        return FormResult<FormResponseDto>.Ok(ToDto(response));
    }

    /// <summary>
    /// Resuelve la identidad de un registro transaccional al confirmar (ola F3, doc 01 D3):
    /// consecutivo (una TenantSequence por formulario, prefijo = codigo del form) o clave natural
    /// (valor de un campo, unicidad garantizada por indice). None = sin numero.
    /// </summary>
    private async Task<(bool Ok, string? Number, string? Error)> ResolveIdentityAsync(
        FormDefinition definition, IReadOnlyDictionary<string, FormFieldValue> document, CancellationToken cancellationToken)
    {
        switch (definition.IdentityMode)
        {
            case FormIdentityMode.None:
                return (true, null, null);

            case FormIdentityMode.NaturalKey:
                if (string.IsNullOrWhiteSpace(definition.IdentitySourceFieldCode))
                {
                    return (false, null, "El formulario no tiene campo de identidad configurado.");
                }
                document.TryGetValue(definition.IdentitySourceFieldCode, out var keyField);
                if (string.IsNullOrWhiteSpace(keyField?.Value))
                {
                    return (false, null, "El campo de identidad es obligatorio para confirmar.");
                }
                return (true, keyField!.Value, null);

            case FormIdentityMode.Sequence:
                // Una secuencia por formulario (doc 03 B). El code de TenantSequence es varchar(10):
                // se usa un codigo corto derivado del id ("F"+8 hex, unico por tenant); el prefijo
                // legible del numero es el codigo del formulario (ej. FRM-021-000001).
                var code = "F" + definition.Id.ToString("N")[..8];
                await _sequences.EnsureSequenceAsync(code, cancellationToken);
                var number = await _sequences.NextAsync(code, $"{definition.Code}-", 6, cancellationToken);
                return (true, number, null);

            default:
                return (true, null, null);
        }
    }

    public async Task<IReadOnlyList<FormRecordListItemDto>> ListRecordsAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        // Bandeja del formulario-modulo (ola F4): los registros enviados (no borradores), recientes primero.
        var rows = await _db.FormResponses.AsNoTracking()
            .Where(r => r.DefinitionId == definitionId && r.Status == FormResponseStatus.Submitted)
            .OrderByDescending(r => r.TransactionDate ?? r.SubmittedAt)
            .Select(r => new { r.Id, r.RecordNumber, r.RecordStatus, r.TransactionDate, r.SubmittedAt, r.Reference, r.Data })
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var fields = ParseDocument(r.Data).ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);
            return new FormRecordListItemDto(
                r.Id, r.RecordNumber, r.RecordStatus, r.TransactionDate, r.SubmittedAt, r.Reference,
                fields);
        }).ToList();
    }

    public async Task<byte[]?> ExportRecordsXlsxAsync(Guid definitionId, CancellationToken cancellationToken = default)
    {
        var definition = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId && d.IsModule, cancellationToken);
        if (definition is null) { return null; }

        // Columnas de datos configuradas (field codes) + su etiqueta desde las preguntas.
        var columns = ParseCodeList(definition.ListColumnsJson);
        var labels = await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == definitionId)
            .ToDictionaryAsync(q => q.FieldCode, q => q.Label, StringComparer.Ordinal, cancellationToken);

        var records = await ListRecordsAsync(definitionId, cancellationToken);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add(definition.Code);
        // Encabezado: metadatos fijos + columnas de datos (vista aplanada para BI).
        var headers = new List<string> { "Numero", "Fecha", "Estado", "Referencia" };
        headers.AddRange(columns.Select(c => labels.TryGetValue(c, out var l) ? l : c));
        for (var i = 0; i < headers.Count; i++) { ws.Cell(1, i + 1).Value = headers[i]; }
        ws.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var r in records)
        {
            ws.Cell(row, 1).Value = r.RecordNumber ?? "";
            ws.Cell(row, 2).Value = (r.TransactionDate ?? r.SubmittedAt)?.ToString("yyyy-MM-dd HH:mm") ?? "";
            ws.Cell(row, 3).Value = r.RecordStatus.ToString();
            ws.Cell(row, 4).Value = r.Reference ?? "";
            for (var c = 0; c < columns.Count; c++)
            {
                ws.Cell(row, 5 + c).Value = r.Fields.TryGetValue(columns[c], out var v) ? v ?? "" : "";
            }
            row++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---- Maestro-detalle (ola F5, doc 01 D7) ----

    public async Task<IReadOnlyList<FormRecordListItemDto>> ListChildrenAsync(
        Guid parentResponseId, string parentFieldCode, CancellationToken cancellationToken = default)
    {
        var links = await _db.FormRecordLinks.AsNoTracking()
            .Where(l => l.ParentResponseId == parentResponseId && l.ParentFieldCode == parentFieldCode)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.CreatedAt)
            .Join(_db.FormResponses.AsNoTracking(), l => l.ChildResponseId, r => r.Id, (l, r) => r)
            .ToListAsync(cancellationToken);

        return links.Select(r =>
        {
            var fields = ParseDocument(r.Data).ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);
            return new FormRecordListItemDto(r.Id, r.RecordNumber, r.RecordStatus, r.TransactionDate, r.SubmittedAt, r.Reference, fields);
        }).ToList();
    }

    public async Task<FormResult<Guid>> AddChildAsync(
        Guid parentResponseId, string parentFieldCode, Guid childDefinitionId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
        {
            return FormResult<Guid>.Invalid("No hay tenant activo.");
        }
        var parentExists = await _db.FormResponses.AsNoTracking().AnyAsync(r => r.Id == parentResponseId, cancellationToken);
        if (!parentExists) { return FormResult<Guid>.NotFound("Registro padre no encontrado."); }
        var childDefExists = await _db.FormDefinitions.AsNoTracking().AnyAsync(d => d.Id == childDefinitionId, cancellationToken);
        if (!childDefExists) { return FormResult<Guid>.NotFound("Definicion hija no encontrada."); }

        var child = new FormResponse { TenantId = tenantId, DefinitionId = childDefinitionId, Data = "{}" };
        _db.FormResponses.Add(child);
        var order = await _db.FormRecordLinks
            .Where(l => l.ParentResponseId == parentResponseId && l.ParentFieldCode == parentFieldCode)
            .CountAsync(cancellationToken);
        _db.FormRecordLinks.Add(new FormRecordLink
        {
            TenantId = tenantId,
            ParentResponseId = parentResponseId,
            ParentFieldCode = parentFieldCode,
            ChildResponseId = child.Id,
            SortOrder = order,
        });
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<Guid>.Ok(child.Id);
    }

    public async Task<FormResult<bool>> UnlinkChildAsync(
        Guid parentResponseId, string parentFieldCode, Guid childResponseId, CancellationToken cancellationToken = default)
    {
        var link = await _db.FormRecordLinks
            .FirstOrDefaultAsync(l => l.ParentResponseId == parentResponseId
                && l.ParentFieldCode == parentFieldCode && l.ChildResponseId == childResponseId, cancellationToken);
        if (link is null) { return FormResult<bool>.NotFound("Enlace no encontrado."); }
        _db.FormRecordLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    /// <summary>Deserializa un arreglo JSON de field codes (columnas/filtros de la bandeja). Vacio si invalido.</summary>
    private static IReadOnlyList<string> ParseCodeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Anula un registro transaccional confirmado (ola F3, doc 01 D2): RecordStatus=Voided + motivo
    /// + auditoria. NO borra ni libera el numero (queda el hueco, trazable). Idempotente.
    /// </summary>
    public async Task<FormResult<FormResponseDto>> VoidAsync(
        Guid responseId, string reason, Guid? byTenantUserId = null, CancellationToken cancellationToken = default)
    {
        var response = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null)
        {
            return FormResult<FormResponseDto>.NotFound("Respuesta no encontrada.");
        }
        if (response.RecordStatus != FormRecordStatus.Confirmed)
        {
            return FormResult<FormResponseDto>.Invalid("Solo se puede anular un registro confirmado.");
        }
        response.RecordStatus = FormRecordStatus.Voided;
        response.VoidedAt = DateTimeOffset.UtcNow;
        response.VoidedByTenantUserId = byTenantUserId;
        response.VoidReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FormResult<FormResponseDto>.Conflict(ConflictMessage);
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
            response.Version,
            response.RecordNumber, response.RecordStatus, response.TransactionDate);

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
