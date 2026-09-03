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
    private readonly Lookups.IFormLookupService _lookup;
    private readonly Rules.IRulesEngine _rules;

    public FormResponseService(
        IApplicationDbContext db, IWorkflowEngine workflowEngine, Tenancy.ISequenceService sequences,
        Common.ITenantContext tenant, Tenancy.IFormRecordBroadcaster recordBroadcaster,
        Lookups.IFormLookupService lookup, Rules.IRulesEngine rules)
    {
        _db = db;
        _workflowEngine = workflowEngine;
        _sequences = sequences;
        _tenant = tenant;
        _recordBroadcaster = recordBroadcaster;
        _lookup = lookup;
        _rules = rules;
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
        // Valores del ENCABEZADO visibles para las formulas de columna via {#campo} (C3): p.ej. el
        // % de IVA de la cotizacion, que es editable por documento y no se repite en cada fila.
        var headerValues = document.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);
        // ADR-0081 fase 2: las grillas se recalculan en ORDEN POR DEPENDENCIA (cross-grid): una grilla que
        // referencia a otra (columna crossGrid) se computa DESPUES de esa. Asi el APU se calcula antes que
        // Oferta/Margen, que traen sus totales por VLOOKUP/SUMIF. computedRows guarda las filas ya computadas
        // para que el resolver cross-grid las lea.
        var gridMeta = questions.Where(q => q.ControlType == FormControlType.GridDetail)
            .Select(q => (q, cols: Calc.FormGridCalculator.ParseColumns(q.OptionsJson),
                             extras: Lookups.FormGridColumnLookupParser.Parse(q.OptionsJson)))
            .Where(x => x.cols.Count > 0)
            .ToList();
        var metaByCode = gridMeta.ToDictionary(m => m.q.FieldCode, m => m, StringComparer.OrdinalIgnoreCase);
        var depItems = gridMeta
            .Select(m => (m.q.FieldCode, (IReadOnlyList<string>)m.extras.Values
                .Where(e => e.CrossRef is not null).Select(e => e.CrossRef!.Grid).ToList()))
            .ToList();
        var computedRowsByField = new Dictionary<string, List<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in Calc.FormGridDependency.Order(depItems))
        {
            var (question, cols, extras) = metaByCode[code];
            document.TryGetValue(question.FieldCode, out var gridField);
            var gridRows = FormFieldValidator.ParseGridRows(gridField?.Value)
                .Select(r => new Dictionary<string, string?>(r, StringComparer.Ordinal)).ToList();
            // Auto-resolucion multi-clave (VLOOKUP) AUTORITATIVA en servidor ANTES del calculo: el cliente
            // no es fuente de verdad para los precios resueltos (igual que el calc por formula).
            await ResolveGridColumnsAsync(question, gridRows, cancellationToken);
            // CAP 2: resolver columnas crossGrid contra las grillas YA computadas (antes del calc, que las usa).
            ResolveCrossGridColumns(extras, gridRows, computedRowsByField, headerValues);
            var (computed, rollups) = Calc.FormGridCalculator.Recompute(gridRows, cols, headerValues);
            computedRowsByField[question.FieldCode] = computed;
            document[question.FieldCode] = new FormFieldValue(
                computed.Count == 0 ? null : JsonSerializer.Serialize(computed, JsonOptions),
                question.ControlType.ToString());
            foreach (var (field, total) in rollups)
            {
                var type = questionsByCode.TryGetValue(field, out var tq) ? tq.ControlType.ToString() : FormControlType.Text.ToString();
                document[field] = new FormFieldValue(total, type);
                // Un roll-up ya es encabezado: queda visible para las tablas que se calculen despues.
                headerValues[field] = total;
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

        // Escalon de ESTADOS (P1#5): al enviar, calcula el estado del registro (badge) desde sus datos y lo
        // escribe en el campo destino del documento (avance-only). Config en la definicion (StatusLadderJson);
        // generico, no hardcode. El valor queda como un campo mas -> visible en la bandeja/impresion y de
        // badge en el encabezado del renderer.
        if (submit)
        {
            var ladderJson = await _db.FormDefinitions.AsNoTracking()
                .Where(d => d.Id == response.DefinitionId).Select(d => d.StatusLadderJson)
                .FirstOrDefaultAsync(cancellationToken);

            // Conteo de HIJOS por campo Subform (FormRecordLink) del registro actual, para los operadores
            // hasChildren/childCount del escalon (p.ej. Prospectado = tiene >=1 oportunidad). Se precarga UNA
            // vez y solo si hay escalon configurado. En un registro NUEVO aun sin Id no hay hijos -> cuenta 0.
            Dictionary<string, int> childCounts = new(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(ladderJson) && response.Id != Guid.Empty)
            {
                childCounts = await _db.FormRecordLinks.AsNoTracking()
                    .Where(l => l.ParentResponseId == response.Id)
                    .GroupBy(l => l.ParentFieldCode)
                    .Select(gp => new { Field = gp.Key, N = gp.Count() })
                    .ToDictionaryAsync(x => x.Field, x => x.N, StringComparer.Ordinal, cancellationToken);
            }

            var ladder = FormStatusLadder.Resolve(
                ladderJson, code => document.TryGetValue(code, out var fv) ? fv.Value : null,
                code => childCounts.TryGetValue(code, out var n) ? n : 0);
            if (ladder is not null)
            {
                var type = questionsByCode.TryGetValue(ladder.TargetField, out var lq)
                    ? lq.ControlType.ToString() : FormControlType.Text.ToString();
                document[ladder.TargetField] = new FormFieldValue(ladder.Label, type);
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
                // Este formulario ya se envio: su link queda Completed.
                link.Status = FormFlowLinkStatus.Completed;

                // Un nodo puede exigir VARIOS formularios: el paso se completa SOLO cuando no queda
                // ningun otro formulario Pending de ese nodo/instancia (todos enviados). Si aun faltan,
                // este envio no cierra el paso.
                var otherPending = await _db.FormFlowLinks
                    .AnyAsync(l => l.WorkflowInstanceId == link.WorkflowInstanceId
                        && l.WorkflowNodeId == link.WorkflowNodeId
                        && l.Status == FormFlowLinkStatus.Pending
                        && l.Id != link.Id, cancellationToken);
                if (otherPending)
                {
                    continue;
                }

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
                // Si el paso ya no esta vigente (reinicio/rechazo posterior), el link ya quedo cerrado.
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

        // Reglas ON-SUBMIT (FormSubmitRule): corren DESPUES de que el envio quedo confirmado y commiteado,
        // asi un fallo al crear (p.ej.) la actividad NO revierte el envio; el motor lo registra en su
        // historial. Cubre tambien el envio publico anonimo (esta ruta la usan renderer y /f/{token}).
        if (submit)
        {
            try
            {
                var submitData = document.ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);
                await _rules.ExecuteForFormSubmitAsync(
                    response.DefinitionId, submitData,
                    formResponseId: response.Id,
                    executedByTenantUserId: submittedByTenantUserId,
                    actorUserId: _tenant.UserId,
                    actorName: "Formulario",
                    cancellationToken: cancellationToken);
            }
            catch (Exception)
            {
                // El envio ya esta confirmado: un fallo de las reglas on-submit no debe tumbarlo.
            }
        }

        return FormResult<FormResponseDto>.Ok(ToDto(response));
    }

    /// <summary>
    /// Resuelve la identidad de un registro transaccional al confirmar (ola F3, doc 01 D3):
    /// consecutivo (una TenantSequence por formulario, prefijo = codigo del form) o clave natural
    /// (valor de un campo, unicidad garantizada por indice). None = sin numero.
    /// </summary>
    // ADR-0081 CAP 2: para cada columna 'crossGrid' de la tabla, resuelve su valor (VLOOKUP/SUMIF) contra
    // las filas YA COMPUTADAS de la grilla origen (mismo registro). Se llama ANTES del Recompute para que
    // los 'calc' que usan el valor lo vean. Si la grilla origen aun no se computo (deberia por el orden de
    // dependencia), la celda se deja como estaba.
    private static void ResolveCrossGridColumns(
        IReadOnlyDictionary<string, Lookups.FormGridColumnExtras> extras,
        List<Dictionary<string, string?>> rows,
        IReadOnlyDictionary<string, List<Dictionary<string, string?>>> computedByField,
        IReadOnlyDictionary<string, string?> headerValues)
    {
        foreach (var ex in extras.Values.Where(e => e.CrossRef is not null))
        {
            var cross = ex.CrossRef!;
            if (!computedByField.TryGetValue(cross.Grid, out var sourceRows)) { continue; }
            foreach (var row in rows)
            {
                row[ex.Id] = Calc.FormGridCrossRefResolver.Resolve(cross, sourceRows, row, headerValues);
            }
        }
    }

    // Auto-resolucion multi-clave (VLOOKUP/INDEX-MATCH) en SERVIDOR: para cada columna 'resolve' de la
    // tabla, valida 'when', sustituye {campo} del match con celdas de la MISMA fila y pide el valor a la
    // capa de lookup (tenant-safe). Mismo criterio que el renderer; el servidor manda.
    private async Task ResolveGridColumnsAsync(Domain.Entities.FormQuestion question, List<Dictionary<string, string?>> rows, CancellationToken ct)
    {
        var resolvers = Lookups.FormGridColumnLookupParser.Parse(question.OptionsJson)
            .Values.Where(e => e.Resolve is not null).ToList();
        if (resolvers.Count == 0) { return; }
        foreach (var row in rows)
        {
            foreach (var ex in resolvers)
            {
                var rc = ex.Resolve!;
                if (!rc.When.All(kv => CellsEqualExact(SubstituteRowRefs(kv.Key, row), kv.Value)))
                {
                    row[ex.Id] = string.Empty;
                    continue;
                }
                var match = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var complete = true;
                foreach (var (col, refExpr) in rc.Match)
                {
                    var v = SubstituteRowRefs(refExpr, row);
                    if (string.IsNullOrWhiteSpace(v)) { complete = false; break; }
                    match[col] = v;
                }
                if (!complete || string.IsNullOrWhiteSpace(rc.SourceRef))
                {
                    row[ex.Id] = string.Empty;
                    continue;
                }
                var result = await _lookup.MatchAsync(rc.SourceKind, rc.SourceRef!, match, rc.ReturnField, ct);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    // La matriz es autoritativa donde hay match.
                    row[ex.Id] = result!;
                }
                else if (!rc.AllowManual)
                {
                    // Clasico: sin match -> vacio. Con allowManual se CONSERVA lo tecleado (no se sobrescribe).
                    row[ex.Id] = string.Empty;
                }
            }
        }
    }

    private static string SubstituteRowRefs(string expr, IReadOnlyDictionary<string, string?> row)
        => string.IsNullOrEmpty(expr)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(expr, "\\{([^}]+)\\}",
                m => row.TryGetValue(m.Groups[1].Value.Trim(), out var v) ? (v ?? string.Empty) : string.Empty);

    private static bool CellsEqualExact(string? a, string? b)
    {
        var x = (a ?? string.Empty).Trim();
        var y = (b ?? string.Empty).Trim();
        if (decimal.TryParse(x, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var nx)
            && decimal.TryParse(y, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ny))
        {
            return nx == ny;
        }
        return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }

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
                // Prefijo y padding CONFIGURABLES por formulario (disenador). IdentityPrefix null =>
                // hereda el Code + "-" (ej. "COT-"); cadena vacia => sin prefijo. Padding fuera de 1..12 => 6.
                var prefix = definition.IdentityPrefix is null
                    ? (string.IsNullOrEmpty(definition.Code) ? "" : definition.Code + "-")
                    : definition.IdentityPrefix;
                var padding = definition.IdentityPadding is >= 1 and <= 12 ? definition.IdentityPadding : 6;
                var number = await _sequences.NextAsync(code, prefix, padding, cancellationToken);
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

    // ---- Gestion por FILA del GridDetail (ADR-0085) ----

    public async Task<FormResult<Guid>> AddRowChildAsync(
        Guid parentResponseId, string parentFieldCode, string parentRowId, Guid childDefinitionId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return FormResult<Guid>.Invalid("No hay tenant activo."); }
        if (string.IsNullOrWhiteSpace(parentRowId)) { return FormResult<Guid>.Invalid("Falta el identificador de fila."); }
        var parentExists = await _db.FormResponses.AsNoTracking().AnyAsync(r => r.Id == parentResponseId, cancellationToken);
        if (!parentExists) { return FormResult<Guid>.NotFound("Registro padre no encontrado."); }
        var childDefExists = await _db.FormDefinitions.AsNoTracking().AnyAsync(d => d.Id == childDefinitionId, cancellationToken);
        if (!childDefExists) { return FormResult<Guid>.NotFound("Definicion hija no encontrada."); }

        var child = new FormResponse { TenantId = tenantId, DefinitionId = childDefinitionId, Data = "{}" };
        _db.FormResponses.Add(child);
        var order = await _db.FormRecordLinks
            .Where(l => l.ParentResponseId == parentResponseId && l.ParentFieldCode == parentFieldCode && l.ParentRowId == parentRowId)
            .CountAsync(cancellationToken);
        _db.FormRecordLinks.Add(new FormRecordLink
        {
            TenantId = tenantId,
            ParentResponseId = parentResponseId,
            ParentFieldCode = parentFieldCode,
            ParentRowId = parentRowId,
            ChildResponseId = child.Id,
            SortOrder = order,
        });
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<Guid>.Ok(child.Id);
    }

    public async Task<IReadOnlyList<FormRecordListItemDto>> ListRowChildrenAsync(
        Guid parentResponseId, string parentFieldCode, string parentRowId, CancellationToken cancellationToken = default)
    {
        var children = await _db.FormRecordLinks.AsNoTracking()
            .Where(l => l.ParentResponseId == parentResponseId && l.ParentFieldCode == parentFieldCode && l.ParentRowId == parentRowId)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.CreatedAt)
            .Join(_db.FormResponses.AsNoTracking(), l => l.ChildResponseId, r => r.Id, (l, r) => r)
            .ToListAsync(cancellationToken);

        return children.Select(r =>
        {
            var fields = ParseDocument(r.Data).ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.Ordinal);
            return new FormRecordListItemDto(r.Id, r.RecordNumber, r.RecordStatus, r.TransactionDate, r.SubmittedAt, r.Reference, fields);
        }).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<Guid, int>>> CountRowChildrenAsync(
        Guid parentResponseId, string parentFieldCode, CancellationToken cancellationToken = default)
    {
        var rows = await _db.FormRecordLinks.AsNoTracking()
            .Where(l => l.ParentResponseId == parentResponseId && l.ParentFieldCode == parentFieldCode && l.ParentRowId != null)
            .Join(_db.FormResponses.AsNoTracking(), l => l.ChildResponseId, r => r.Id, (l, r) => new { l.ParentRowId, r.DefinitionId })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, IReadOnlyDictionary<Guid, int>>(StringComparer.Ordinal);
        foreach (var g in rows.GroupBy(x => x.ParentRowId!))
        {
            map[g.Key] = g.GroupBy(x => x.DefinitionId).ToDictionary(gg => gg.Key, gg => gg.Count());
        }
        return map;
    }

    public async Task<Guid?> ResolveDefinitionIdByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) { return null; }
        var c = code.Trim();
        return await _db.FormDefinitions.AsNoTracking()
            .Where(d => d.Code == c && d.Status == FormStatus.Active && !d.IsArchived)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
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

    public async Task<FormResult<bool>> DeleteRecordAsync(Guid responseId, CancellationToken cancellationToken = default)
    {
        var response = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null)
        {
            return FormResult<bool>.NotFound("Registro no encontrado.");
        }

        var tx = _db.HasActiveTransaction ? null : await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            // Enlaces maestro-detalle (FormRecordLink) apuntan al registro con FK Restrict: la BD no
            // deja borrar el registro mientras existan, asi que se retiran primero. El registro puede
            // ser padre o hijo de otro.
            var enlaces = await _db.FormRecordLinks
                .Where(l => l.ParentResponseId == responseId || l.ChildResponseId == responseId)
                .ToListAsync(cancellationToken);
            if (enlaces.Count > 0) { _db.FormRecordLinks.RemoveRange(enlaces); }

            // Notas de tercero que citan este registro (FK Restrict, nullable): se desligan sin
            // borrar la nota (es historia del tercero, no del formulario).
            var notas = await _db.TerceroNotas
                .Where(n => n.FormResponseId == responseId)
                .ToListAsync(cancellationToken);
            foreach (var n in notas) { n.FormResponseId = null; }

            // FormFlowLink cae por cascada de BD. El registro se borra de verdad y su numero se libera.
            _db.FormResponses.Remove(response);
            await _db.SaveChangesAsync(cancellationToken);

            if (tx is not null) { await tx.CommitAsync(cancellationToken); }
            return FormResult<bool>.Ok(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx is not null) { await tx.RollbackAsync(cancellationToken); }
            return FormResult<bool>.Conflict(ConflictMessage);
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(cancellationToken); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
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
            // Un nodo puede tener VARIOS formularios: se ofrecen TODOS (en orden). El paso no se
            // completa hasta enviarlos todos (el gating vive en SubmitAsync).
            var stepForms = nodeForms.Where(f => f.NodeId == step.NodeId)
                .OrderBy(f => f.SortOrder).ToList();
            if (stepForms.Count == 0)
            {
                continue;
            }
            var (isGatewayAhead, approvalOptions) =
                WorkflowInboxProjection.ResolveGatewayAhead(step.NodeId, edges, gatewayNodeIds);

            foreach (var nodeForm in stepForms)
            {
                var definition = await _db.FormDefinitions.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == nodeForm.DefinitionId, cancellationToken);
                if (definition is null || definition.Status != FormStatus.Active || definition.IsArchived)
                {
                    continue;
                }

                // Continuidad de datos (mismo formulario en varios nodos): la respuesta se ANCLA a
                // (definicion, numero de la tarea). Si ese formulario YA se ENVIO en un paso anterior, se
                // REUSA esa respuesta (mismos datos) en los pasos siguientes en vez de crear uno en blanco
                // -que era el bug: GetOrCreateDraft solo reutiliza borradores, y al enviarse pasaba a
                // Submitted-. Si no hay enviada, se usa/crea el borrador (idempotente) para diligenciarlo.
                Guid responseId;
                string? responseRef;
                FormResponseStatus responseStatus;
                var submitted = await _db.FormResponses.AsNoTracking()
                    .Where(r => r.DefinitionId == definition.Id
                        && r.Reference == task.Number
                        && r.Status == FormResponseStatus.Submitted)
                    .OrderByDescending(r => r.SubmittedAt)
                    .Select(r => new { r.Id, r.Reference })
                    .FirstOrDefaultAsync(cancellationToken);
                if (submitted is not null)
                {
                    responseId = submitted.Id;
                    responseRef = submitted.Reference;
                    responseStatus = FormResponseStatus.Submitted;
                }
                else if (!nodeForm.AutoCreateOnArrival)
                {
                    // Carga automatica DESACTIVADA: no se crea el borrador al llegar al paso. Solo se muestra
                    // si YA existe uno (p.ej. agregado a mano con "+ Agregar formulario"); si no, se omite.
                    var existingDraft = await _db.FormResponses.AsNoTracking()
                        .Where(r => r.DefinitionId == definition.Id
                            && r.Reference == task.Number
                            && r.Status == FormResponseStatus.Draft)
                        .OrderByDescending(r => r.CreatedAt)
                        .Select(r => new { r.Id, r.Reference, r.Status })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (existingDraft is null)
                    {
                        continue;
                    }
                    responseId = existingDraft.Id;
                    responseRef = existingDraft.Reference;
                    responseStatus = existingDraft.Status;
                }
                else
                {
                    var draft = await GetOrCreateDraftAsync(definition.Id, task.Number, cancellationToken);
                    if (!draft.IsOk || draft.Value is null)
                    {
                        continue;
                    }
                    responseId = draft.Value.Id;
                    responseRef = draft.Value.Reference;
                    responseStatus = draft.Value.Status;
                }

                // Link del nodo a esa respuesta. Si la respuesta ya esta enviada, el link nace Completed
                // (los datos se cargan, en solo-lectura, y el paso NO queda bloqueado esperando el
                // formulario). Si es borrador, Pending: hay que diligenciarlo/enviarlo en este paso.
                var linkStatus = responseStatus == FormResponseStatus.Submitted
                    ? FormFlowLinkStatus.Completed
                    : FormFlowLinkStatus.Pending;
                var link = await _db.FormFlowLinks
                    .FirstOrDefaultAsync(l => l.WorkflowInstanceId == instanceId
                        && l.WorkflowNodeId == step.NodeId
                        && l.FormResponseId == responseId, cancellationToken);
                if (link is null)
                {
                    link = new FormFlowLink
                    {
                        TenantId = task.TenantId,
                        FormResponseId = responseId,
                        WorkflowInstanceId = instanceId,
                        WorkflowNodeId = step.NodeId,
                        Status = linkStatus
                    };
                    _db.FormFlowLinks.Add(link);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                result.Add(new TaskStepFormDto(
                    responseId, definition.Id, definition.Code, definition.Title,
                    instanceId, step.NodeId, step.NodeName,
                    link.Status, responseStatus, responseRef,
                    isGatewayAhead, approvalOptions, definition.CardLayout));
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<CreationFlowFormDto>> GetSubcategoriaCreationFlowFormsAsync(
        Guid subcategoriaId, CancellationToken cancellationToken = default)
    {
        var defId = await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => s.Id == subcategoriaId)
            .Select(s => s.WorkflowDefinitionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (defId is not Guid wfDefId)
        {
            return [];
        }
        // Debe estar publicado (mismo criterio que el arranque del flujo).
        var published = await _db.WorkflowDefinitions.AsNoTracking()
            .AnyAsync(d => d.Id == wfDefId && d.IsPublished && !d.IsArchived, cancellationToken);
        if (!published)
        {
            return [];
        }
        var startNodeId = await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == wfDefId && n.NodeType == WorkflowNodeType.StartEvent)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (startNodeId is not Guid snid)
        {
            return [];
        }
        // Formularios del evento de inicio (Active), en orden. Son los que el wizard ofrece AL CREAR.
        return await _db.WorkflowNodeForms.AsNoTracking()
            .Where(f => f.NodeId == snid)
            .OrderBy(f => f.SortOrder)
            .Join(_db.FormDefinitions.AsNoTracking(), f => f.DefinitionId, d => d.Id, (f, d) => d)
            .Where(d => d.Status == FormStatus.Active && !d.IsArchived)
            .Select(d => new CreationFlowFormDto(d.Id, d.Code, d.Title, d.CardLayout))
            .ToListAsync(cancellationToken);
    }

    // Resuelve el formulario del concepto (subcategoria) de una tarea: (tarea, definicion Active) o null.
    private async Task<(TaskItem Task, FormDefinition Def)?> ResolveConceptFormAsync(Guid taskItemId, CancellationToken ct)
    {
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskItemId, ct);
        if (task?.SubcategoriaId is not Guid subId) { return null; }
        var formDefId = await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => s.Id == subId).Select(s => s.FormDefinitionId).FirstOrDefaultAsync(ct);
        if (formDefId is not Guid defId) { return null; }
        var def = await _db.FormDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == defId, ct);
        if (def is null || def.Status != FormStatus.Active || def.IsArchived) { return null; }
        return (task, def);
    }

    public async Task<TaskConceptFormsDto?> GetTaskConceptFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveConceptFormAsync(taskItemId, cancellationToken);
        if (resolved is null) { return null; }
        var (task, def) = resolved.Value;
        return await BuildGeneroAsync(task, def, new HashSet<Guid>(), cancellationToken);
    }

    /// <summary>
    /// Arma la tarjeta de un GENERO (una definicion de formulario) para una tarea: sus respuestas ancladas
    /// ("{numero}" / "{numero}-{n}"), el formulario ACTIVO efectivo y los items. Excluye las respuestas
    /// indicadas (p.ej. la del paso ACTUAL del flujo, que se muestra en "Formularios del proceso"). Es la base
    /// compartida por el genero del concepto y los generos del flujo: toda la logica es agnostica del origen.
    /// </summary>
    private async Task<TaskConceptFormsDto> BuildGeneroAsync(
        TaskItem task, FormDefinition def, ISet<Guid> excludeResponseIds, CancellationToken ct, bool inherited = false)
    {
        var prefix = task.Number + "-";
        var responses = (await _db.FormResponses.AsNoTracking()
            .Where(r => r.DefinitionId == def.Id
                && (r.Reference == task.Number || (r.Reference != null && r.Reference.StartsWith(prefix))))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct))
            .Where(r => !excludeResponseIds.Contains(r.Id))
            .ToList();

        // Cliente para el subtitulo de la tarjeta (generico, por Label). El titulo = numero (Reference).
        var cliCode = await ResolveFieldCodeAsync(def.Id, ct, "cliente", "tercero", "razon", "razón", "empresa");

        // Formulario ACTIVO efectivo: el marcado (IsActive); si ninguno, el original ("{numero}" sin
        // sufijo); si tampoco, el mas antiguo. Asi siempre hay exactamente uno activo aunque nadie
        // lo haya elegido, y con una sola respuesta esa queda activa.
        var activeId = responses.FirstOrDefault(r => r.IsActive)?.Id
            ?? responses.FirstOrDefault(r => r.Reference == task.Number)?.Id
            ?? responses.FirstOrDefault()?.Id;

        var items = responses.Select(r => new TaskConceptFormItemDto(
            r.Id, r.Reference, r.Status,
            r.Reference, ExtractDataField(r.Data, cliCode), r.CreatedAt,
            r.Id == activeId)).ToList();

        return new TaskConceptFormsDto(def.Id, def.Code, def.Title, items, def.CardLayout, inherited);
    }

    public async Task<IReadOnlyList<TaskConceptFormsDto>> GetTaskFormGenerosAsync(Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskItemId, cancellationToken);
        if (task is null || string.IsNullOrEmpty(task.Number)) { return Array.Empty<TaskConceptFormsDto>(); }

        // 1) Genero del CONCEPTO (0 o 1): siempre se muestra si existe (aunque este vacio).
        Guid? conceptDefId = null;
        if (task.SubcategoriaId is Guid subId)
        {
            var cd = await _db.ActividadSubcategorias.AsNoTracking()
                .Where(s => s.Id == subId && s.FormDefinitionId != null)
                .Select(s => s.FormDefinitionId!.Value).FirstOrDefaultAsync(cancellationToken);
            if (cd != Guid.Empty) { conceptDefId = cd; }
        }

        // 2) Generos del FLUJO: definiciones de WorkflowNodeForm de TODOS los nodos (opciones de "+ Agregar" y,
        //    ya diligenciados, tarjetas). El formulario del paso ACTUAL YA NO se excluye: tambien se pinta como
        //    tarjeta; su boton "Diligenciar" (y ruta/cierre de compuerta) lo resuelve la UI en modo paso.
        var flowDefIds = new List<Guid>();
        if (task.WorkflowInstanceId is Guid instId)
        {
            var wfDefId = await _db.WorkflowInstances.AsNoTracking()
                .Where(i => i.Id == instId).Select(i => i.DefinitionId).FirstOrDefaultAsync(cancellationToken);
            var nodeIds = await _db.WorkflowNodes.AsNoTracking()
                .Where(n => n.DefinitionId == wfDefId).Select(n => n.Id).ToListAsync(cancellationToken);
            var nodeForms = await _db.WorkflowNodeForms.AsNoTracking()
                .Where(f => nodeIds.Contains(f.NodeId))
                .Select(f => new { f.DefinitionId, f.SortOrder })
                .OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
            foreach (var nf in nodeForms)
            {
                if (!flowDefIds.Contains(nf.DefinitionId)) { flowDefIds.Add(nf.DefinitionId); }
            }
        }

        // 3) Catch-all: cualquier definicion con respuestas ancladas a la tarea (p.ej. una Orden de Trabajo
        //    derivada, ADR-0078). Asi nada anclado queda sin tarjeta (subsume la vieja "Formularios de la actividad").
        var prefix = task.Number + "-";
        var anchoredDefIds = await _db.FormResponses.AsNoTracking()
            .Where(r => r.Reference == task.Number || (r.Reference != null && r.Reference.StartsWith(prefix)))
            .Select(r => r.DefinitionId).Distinct().ToListAsync(cancellationToken);

        // Orden de generos: concepto, luego flujo (SortOrder), luego catch-all.
        var ordered = new List<Guid>();
        void AddDef(Guid id) { if (!ordered.Contains(id)) { ordered.Add(id); } }
        if (conceptDefId is Guid c) { AddDef(c); }
        foreach (var d in flowDefIds) { AddDef(d); }
        foreach (var d in anchoredDefIds) { AddDef(d); }
        if (ordered.Count == 0) { return Array.Empty<TaskConceptFormsDto>(); }

        var defs = await _db.FormDefinitions.AsNoTracking()
            .Where(d => ordered.Contains(d.Id) && d.Status == FormStatus.Active && !d.IsArchived)
            .ToListAsync(cancellationToken);
        var defById = defs.ToDictionary(d => d.Id);

        // Se devuelven TODOS los generos configurados (concepto + flujo + catch-all), INCLUIDOS los vacios:
        // la UI pinta una lista UNIFORME de formularios (los generos vacios no aportan items) y usa el conjunto
        // completo para el selector del boton "+ Agregar formulario" (que pregunta el tipo cuando hay varios).
        var result = new List<TaskConceptFormsDto>();
        var noExclude = new HashSet<Guid>();
        foreach (var defId in ordered)
        {
            if (!defById.TryGetValue(defId, out var def)) { continue; } // archivada/inactiva -> no se ofrece
            result.Add(await BuildGeneroAsync(task, def, noExclude, cancellationToken));
        }

        // Formularios HEREDADOS de la tarea PADRE (salto de flujo, ADR-0076): cuando esta tarea nacio de un
        // salto (ParentId), se anexan los formularios que el PADRE contiene (respuestas ancladas a su numero)
        // como generos de SOLO consulta, para poder visualizarlos sin salir a la tarea padre. Solo generos con
        // items (no se pintan vacios), y NO entran al selector "+ Agregar" (Inherited=true los excluye).
        if (task.ParentId is Guid parentId)
        {
            var parent = await _db.TaskItems.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == parentId, cancellationToken);
            if (parent is not null && !string.IsNullOrEmpty(parent.Number))
            {
                var pprefix = parent.Number + "-";
                var parentDefIds = await _db.FormResponses.AsNoTracking()
                    .Where(r => r.Reference == parent.Number || (r.Reference != null && r.Reference.StartsWith(pprefix)))
                    .Select(r => r.DefinitionId).Distinct().ToListAsync(cancellationToken);
                if (parentDefIds.Count > 0)
                {
                    var parentDefs = await _db.FormDefinitions.AsNoTracking()
                        .Where(d => parentDefIds.Contains(d.Id) && d.Status == FormStatus.Active && !d.IsArchived)
                        .ToListAsync(cancellationToken);
                    foreach (var pdef in parentDefs)
                    {
                        var g = await BuildGeneroAsync(parent, pdef, noExclude, cancellationToken, inherited: true);
                        if (g.Items.Count > 0) { result.Add(g); }
                    }
                }
            }
        }
        return result;
    }

    public async Task<FormResult<TaskConceptFormItemDto>> CreateTaskConceptFormAsync(Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveConceptFormAsync(taskItemId, cancellationToken);
        if (resolved is null) { return FormResult<TaskConceptFormItemDto>.NotFound("La tarea no tiene formulario de concepto."); }
        return await CreateTaskFormAsync(taskItemId, resolved.Value.Def.Id, cancellationToken);
    }

    public async Task<FormResult<TaskConceptFormItemDto>> CreateTaskFormAsync(Guid taskItemId, Guid definitionId, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskItemId, cancellationToken);
        if (task is null || string.IsNullOrEmpty(task.Number))
        {
            return FormResult<TaskConceptFormItemDto>.NotFound("Tarea no encontrada.");
        }
        if (task.Status == TaskItemStatus.Closed)
        {
            return FormResult<TaskConceptFormItemDto>.Invalid("La tarea esta cerrada: no se pueden agregar formularios.");
        }
        var def = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId && d.Status == FormStatus.Active && !d.IsArchived, cancellationToken);
        if (def is null)
        {
            return FormResult<TaskConceptFormItemDto>.NotFound("Formulario no encontrado o inactivo.");
        }

        var next = await NextFormOrdinalAsync(def.Id, task.Number, cancellationToken);
        var reference = $"{task.Number}-{next}";
        var data = await BuildInheritedNumberDataAsync(def.Id, reference, sourceData: null, cancellationToken);

        var response = new FormResponse
        {
            TenantId = task.TenantId,
            DefinitionId = def.Id,
            Reference = reference,
            Status = FormResponseStatus.Draft,
            Data = data
        };
        _db.FormResponses.Add(response);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<TaskConceptFormItemDto>.Ok(new TaskConceptFormItemDto(
            response.Id, response.Reference, response.Status, response.Reference, null, response.CreatedAt));
    }

    public async Task<FormResult<TaskConceptFormItemDto>> DuplicateResponseAsync(Guid responseId, CancellationToken cancellationToken = default)
    {
        var src = await _db.FormResponses.AsNoTracking().FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (src is null) { return FormResult<TaskConceptFormItemDto>.NotFound("Formulario no encontrado."); }
        var taskNumber = StripOrdinal(src.Reference);
        if (string.IsNullOrEmpty(taskNumber)) { return FormResult<TaskConceptFormItemDto>.Invalid("El formulario no esta anclado a una tarea."); }
        var closed = await _db.TaskItems.AsNoTracking()
            .AnyAsync(t => t.Number == taskNumber && t.Status == TaskItemStatus.Closed, cancellationToken);
        if (closed) { return FormResult<TaskConceptFormItemDto>.Invalid("La tarea esta cerrada: no se pueden agregar formularios."); }

        // Copia todo el Data del origen y hereda el numero nuevo en el campo "numero".
        var next = await NextFormOrdinalAsync(src.DefinitionId, taskNumber, cancellationToken);
        var reference = $"{taskNumber}-{next}";
        var data = await BuildInheritedNumberDataAsync(src.DefinitionId, reference, src.Data, cancellationToken);

        var response = new FormResponse
        {
            TenantId = src.TenantId,
            DefinitionId = src.DefinitionId,
            Reference = reference,
            Status = FormResponseStatus.Draft,
            Data = data
        };
        _db.FormResponses.Add(response);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<TaskConceptFormItemDto>.Ok(new TaskConceptFormItemDto(
            response.Id, response.Reference, response.Status, response.Reference, null, response.CreatedAt));
    }

    public async Task<FormResult<Guid>> CreateDerivedFormAsync(
        Guid sourceResponseId, Guid targetDefinitionId,
        IReadOnlyDictionary<string, string>? fieldMapping,
        IReadOnlyDictionary<string, string>? contextDefaults = null,
        Guid? actorTenantUserId = null,
        CancellationToken cancellationToken = default)
    {
        var src = await _db.FormResponses.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == sourceResponseId, cancellationToken);
        if (src is null) { return FormResult<Guid>.NotFound("Registro origen no encontrado."); }

        var targetDef = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == targetDefinitionId, cancellationToken);
        if (targetDef is null) { return FormResult<Guid>.NotFound("Formulario destino no encontrado."); }
        if (targetDef.Status != FormStatus.Active || targetDef.IsArchived)
        {
            return FormResult<Guid>.Invalid("El formulario destino no esta activo.");
        }

        // Campos a copiar del origen: auto por codigo + mapeo explicito {origen: destino} para los que cambian
        // de nombre (ej. croquis -> croquis_pieza). La grilla 'items' se copia como un campo mas si el destino
        // la tiene. Se calcula ANTES de la idempotencia para poder REFRESCAR la derivada si sigue en borrador.
        var targetCodes = (await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == targetDefinitionId)
            .Select(q => q.FieldCode)
            .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var sourceDoc = ParseDocument(src.Data);
        var mapped = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        foreach (var (srcCode, val) in sourceDoc)
        {
            var targetCode = fieldMapping is not null && fieldMapping.TryGetValue(srcCode, out var mc) && !string.IsNullOrWhiteSpace(mc)
                ? mc.Trim()
                : srcCode;
            if (targetCodes.Contains(targetCode))
            {
                mapped[targetCode] = val;
            }
        }

        // Valores por defecto / TRANSFORMACION configurable (contextDefaults): rellenan campos del destino SIN
        // origen (p.ej. Vendedor = nombre del usuario actual) resolviendo tokens '@...' desde el contexto. Son
        // "fill-if-empty": nunca pisan un dato ya mapeado del origen ni lo que el usuario haya escrito luego.
        var resolvedDefaults = await ResolveContextDefaultsAsync(
            contextDefaults, targetCodes, actorTenantUserId, cancellationToken);

        // Idempotencia (ADR-0078): si ESTE origen ya fue convertido a ESTE destino, no se crea otro. Se REABRE
        // el existente y, mientras siga en BORRADOR, se REFRESCAN sus campos heredados con los datos ACTUALES
        // del origen (cliente, items, croquis, ...); los campos PROPIOS de la OT (vendedor, fechas de entrega,
        // procesos...) se conservan. Ademas se re-ancla si nacio suelto. Si la OT ya fue ENVIADA, no se toca.
        var existing = await _db.FormResponses
            .Where(r => r.DerivedFromResponseId == sourceResponseId && r.DefinitionId == targetDefinitionId)
            .OrderBy(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            var changed = false;
            if (string.IsNullOrEmpty(existing.Reference))
            {
                var tn = StripOrdinal(src.Reference);
                if (!string.IsNullOrEmpty(tn))
                {
                    var nx = await NextFormOrdinalAsync(targetDefinitionId, tn!, cancellationToken);
                    existing.Reference = $"{tn}-{nx}";
                    changed = true;
                }
            }
            if (existing.Status == FormResponseStatus.Draft && (mapped.Count > 0 || resolvedDefaults.Count > 0))
            {
                var doc = new Dictionary<string, FormFieldValue>(ParseDocument(existing.Data), StringComparer.Ordinal);
                foreach (var (code, val) in mapped) { doc[code] = val; }
                ApplyDefaultsFillEmpty(doc, resolvedDefaults);
                existing.Data = JsonSerializer.Serialize(doc, JsonOptions);
                changed = true;
            }
            if (changed) { await _db.SaveChangesAsync(cancellationToken); }
            return FormResult<Guid>.Ok(existing.Id);
        }

        // Anclaje a tarea: si el origen esta anclado (Reference "{tarea}-{n}" o "{tarea}"), la derivada cae en
        // la MISMA tarea con un ordinal nuevo -> aparece en su pestana Formularios. Si el origen es suelto
        // (sin Reference), la derivada nace sin anclaje (flujo standalone).
        // Rellenar los campos por defecto (transformacion) en el borrador nuevo, sin pisar lo ya copiado.
        ApplyDefaultsFillEmpty(mapped, resolvedDefaults);

        var taskNumber = StripOrdinal(src.Reference);
        string? reference = null;
        if (!string.IsNullOrEmpty(taskNumber))
        {
            var next = await NextFormOrdinalAsync(targetDefinitionId, taskNumber, cancellationToken);
            reference = $"{taskNumber}-{next}";
        }

        var dataJson = reference is not null
            ? await BuildInheritedNumberDataAsync(targetDefinitionId, reference,
                JsonSerializer.Serialize(mapped, JsonOptions), cancellationToken)
            : JsonSerializer.Serialize(mapped, JsonOptions);

        var response = new FormResponse
        {
            TenantId = targetDef.TenantId,
            DefinitionId = targetDefinitionId,
            Reference = reference,
            Status = FormResponseStatus.Draft,
            Data = dataJson,
            // Marca de origen: idempotencia + "convertida" en la cotizacion origen (ADR-0078).
            DerivedFromResponseId = sourceResponseId
        };

        // Numeracion AL CREAR (ADR-0078, decision A): un formulario transaccional por Secuencia (ej. Orden de
        // Trabajo FT-C-008, OT-000001) nace NUMERADO y Confirmed -porque como borrador dentro de la tarea nunca
        // pasa por el submit que asigna el numero-. La edicion posterior NO se bloquea (el registro sigue
        // Draft en el ciclo de envio; RecordStatus=Confirmed solo evita RE-numerar). Otros modos: sin cambio.
        if (targetDef.IsTransactional && targetDef.IdentityMode == FormIdentityMode.Sequence)
        {
            var identity = await ResolveIdentityAsync(targetDef, mapped, cancellationToken);
            if (identity.Ok && !string.IsNullOrWhiteSpace(identity.Number))
            {
                response.RecordNumber = identity.Number;
                response.RecordStatus = FormRecordStatus.Confirmed;
                response.TransactionDate = DateTimeOffset.UtcNow;
            }
        }

        _db.FormResponses.Add(response);
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<Guid>.Ok(response.Id);
    }

    /// <summary>Copia los valores por defecto (transformacion) al documento solo donde el campo destino esta
    /// vacio o ausente: nunca pisa un dato heredado del origen ni lo que el usuario haya escrito.</summary>
    private static void ApplyDefaultsFillEmpty(
        IDictionary<string, FormFieldValue> doc, IReadOnlyDictionary<string, FormFieldValue> defaults)
    {
        foreach (var (code, val) in defaults)
        {
            if (!doc.TryGetValue(code, out var cur) || string.IsNullOrWhiteSpace(cur.Value))
            {
                doc[code] = val;
            }
        }
    }

    /// <summary>Resuelve el mapa configurable { campoDestino: token } a valores concretos. Un token que empieza
    /// por '@' se resuelve desde el contexto de ejecucion (usuario actual, fecha); cualquier otro texto se toma
    /// como constante literal. Solo se resuelven campos que EXISTEN en el destino. El nombre/correo del usuario
    /// requiere <paramref name="actorTenantUserId"/> (el TenantUser que dispara la conversion).</summary>
    private async Task<Dictionary<string, FormFieldValue>> ResolveContextDefaultsAsync(
        IReadOnlyDictionary<string, string>? defaults, ISet<string> targetCodes,
        Guid? actorTenantUserId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        if (defaults is null || defaults.Count == 0) { return result; }

        // Datos del usuario actual: se resuelven UNA vez y solo si algun token los pide (evita el join si no aplica).
        var wantsUser = defaults.Values.Any(v => v.TrimStart().StartsWith("@usuario", StringComparison.OrdinalIgnoreCase)
            || v.TrimStart().StartsWith("@user", StringComparison.OrdinalIgnoreCase));
        string? userName = null;
        string? userEmail = null;
        if (wantsUser && actorTenantUserId is Guid tuId && tuId != Guid.Empty)
        {
            var u = await _db.TenantUsers.AsNoTracking()
                .Where(tu => tu.Id == tuId)
                .Join(_db.PlatformUsers.AsNoTracking().IgnoreQueryFilters(),
                    tu => tu.PlatformUserId, pu => pu.Id,
                    (tu, pu) => new { pu.DisplayName, tu.Email })
                .FirstOrDefaultAsync(cancellationToken);
            userName = u?.DisplayName;
            userEmail = u?.Email;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        foreach (var (destCode, rawToken) in defaults)
        {
            if (!targetCodes.Contains(destCode)) { continue; }
            var token = rawToken?.Trim();
            if (string.IsNullOrEmpty(token)) { continue; }

            string? value;
            string type = "Text";
            if (token.StartsWith('@'))
            {
                switch (token.ToLowerInvariant())
                {
                    case "@usuario.nombre" or "@usuario" or "@user.nombre" or "@user.name":
                        value = userName; break;
                    case "@usuario.email" or "@usuario.correo" or "@user.email":
                        value = userEmail; break;
                    case "@fecha.hoy" or "@hoy" or "@today":
                        value = nowUtc.ToString("yyyy-MM-dd"); type = "Date"; break;
                    case "@fecha.hora" or "@hora" or "@now":
                        value = nowUtc.ToString("HH:mm"); type = "Time"; break;
                    default:
                        value = null; break; // token desconocido -> no rellena
                }
            }
            else
            {
                value = token; // constante literal configurada
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                result[destCode] = new FormFieldValue(value, type);
            }
        }
        return result;
    }

    public async Task<IReadOnlyList<TaskRelatedFormDto>> GetTaskRelatedFormsAsync(Guid taskItemId, CancellationToken cancellationToken = default)
    {
        var task = await _db.TaskItems.AsNoTracking()
            .Where(t => t.Id == taskItemId)
            .Select(t => new { t.Number, t.SubcategoriaId, t.WorkflowInstanceId })
            .FirstOrDefaultAsync(cancellationToken);
        if (task is null || string.IsNullOrEmpty(task.Number)) { return Array.Empty<TaskRelatedFormDto>(); }

        // Definiciones ya listadas por otras vias (concepto + pasos del flujo): se excluyen para no duplicar.
        var excluded = new HashSet<Guid>();
        if (task.SubcategoriaId is Guid subId)
        {
            var conceptDef = await _db.ActividadSubcategorias.AsNoTracking()
                .Where(s => s.Id == subId && s.FormDefinitionId != null)
                .Select(s => s.FormDefinitionId!.Value).FirstOrDefaultAsync(cancellationToken);
            if (conceptDef != Guid.Empty) { excluded.Add(conceptDef); }
        }
        // Formularios de PASO del flujo: NO se excluye la definicion entera ni "toda respuesta anclada al numero
        // base". Solo se excluyen las respuestas que "Formularios del proceso" YA muestra AHORA (las del paso
        // ACTUAL, via FormFlowLink al nodo current). Asi un formulario diligenciado EN LA CREACION (form del
        // evento de inicio, ADR-0069) o en un paso ya recorrido -anclado al numero base- SIGUE visible aunque su
        // nodo no sea el paso actual (bug: el form de creacion desaparecia de la tarea). Las derivadas (ordinal,
        // ADR-0078) no tienen link a un paso -> tambien pasan. Requiere que _stepForms se cargue ANTES (crea los
        // links del paso actual): TaskDetailModal lo hace en ese orden.
        var shownInStep = new HashSet<Guid>();
        if (task.WorkflowInstanceId is Guid instId)
        {
            var current = await _workflowEngine.GetCurrentStepsAsync(instId, cancellationToken);
            var currentNodeIds = current
                .Where(s => s.Status == WorkflowStepStatus.Pending)
                .Select(s => s.NodeId)
                .ToList();
            if (currentNodeIds.Count > 0)
            {
                shownInStep = (await _db.FormFlowLinks.AsNoTracking()
                    .Where(l => l.WorkflowInstanceId == instId && currentNodeIds.Contains(l.WorkflowNodeId))
                    .Select(l => l.FormResponseId)
                    .ToListAsync(cancellationToken)).ToHashSet();
            }
        }
        var excludedList = excluded.ToList(); // solo el def del concepto (su seccion cubre base + ordinales)

        var prefix = task.Number + "-";
        var rows = await _db.FormResponses.AsNoTracking()
            .Where(r => (r.Reference == task.Number || (r.Reference != null && r.Reference.StartsWith(prefix)))
                && !excludedList.Contains(r.DefinitionId))
            .Join(_db.FormDefinitions.AsNoTracking(), r => r.DefinitionId, d => d.Id, (r, d) => new
            {
                r.Id, r.DefinitionId, d.Code, d.Title, r.Reference, r.RecordNumber, r.Status, r.CreatedAt,
                d.CardLayout, d.IsArchived, r.Data
            })
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows
            .Where(x => !x.IsArchived)
            .Where(x => !shownInStep.Contains(x.Id))                          // ya visible en "Formularios del proceso"
            .Where(x => x.Status != FormResponseStatus.Draft || HasData(x.Data)) // sin borradores de paso creados en blanco
            .Select(x => new TaskRelatedFormDto(
                x.Id, x.DefinitionId, x.Code, x.Title, x.Reference, x.RecordNumber, x.Status, x.CreatedAt, x.CardLayout))
            .ToList();
    }

    /// <summary>El documento Data trae algo mas que un objeto/arreglo vacio (para no listar borradores en blanco).</summary>
    private static bool HasData(string? data)
    {
        var s = data?.Trim();
        return !string.IsNullOrEmpty(s) && s is not "{}" and not "[]";
    }

    public async Task<DerivedFormRefDto?> GetDerivedRecordAsync(Guid sourceResponseId, CancellationToken cancellationToken = default)
    {
        // El registro derivado de esta respuesta (ADR-0078): idempotencia garantiza a lo sumo uno por destino;
        // se toma el mas antiguo. Filtro global de tenant aplica.
        var row = await _db.FormResponses.AsNoTracking()
            .Where(r => r.DerivedFromResponseId == sourceResponseId)
            .OrderBy(r => r.CreatedAt)
            .Join(_db.FormDefinitions.AsNoTracking(), r => r.DefinitionId, d => d.Id, (r, d) => new
            {
                r.Id, d.Code, d.Title, r.RecordNumber, r.Status
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new DerivedFormRefDto(row.Id, row.Code, row.Title, row.RecordNumber, row.Status);
    }

    public async Task<FormResult<bool>> SetActiveTaskFormAsync(Guid responseId, CancellationToken cancellationToken = default)
    {
        var target = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (target is null) { return FormResult<bool>.NotFound("Formulario no encontrado."); }
        var taskNumber = StripOrdinal(target.Reference);
        if (string.IsNullOrEmpty(taskNumber)) { return FormResult<bool>.Invalid("El formulario no esta anclado a una tarea."); }

        // Mismo conjunto de la tarea que GetTaskConceptFormsAsync: exclusividad acotada a esta tarea.
        var prefix = taskNumber + "-";
        var set = await _db.FormResponses
            .Where(r => r.DefinitionId == target.DefinitionId
                && (r.Reference == taskNumber || (r.Reference != null && r.Reference.StartsWith(prefix))))
            .ToListAsync(cancellationToken);
        foreach (var r in set) { r.IsActive = r.Id == responseId; }
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<BoardFormDto>> GetBoardFormsAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var tasks = await _db.TaskItems.AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .Select(t => new { t.SubcategoriaId, t.WorkflowInstanceId })
            .ToListAsync(cancellationToken);
        if (tasks.Count == 0) { return Array.Empty<BoardFormDto>(); }

        // Formularios del CONCEPTO: subcategoria de la tarea -> FormDefinitionId.
        var subIds = tasks.Where(t => t.SubcategoriaId is not null).Select(t => t.SubcategoriaId!.Value).Distinct().ToList();
        var conceptDefIds = subIds.Count == 0 ? new List<Guid>() : await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => subIds.Contains(s.Id) && s.FormDefinitionId != null)
            .Select(s => s.FormDefinitionId!.Value).Distinct().ToListAsync(cancellationToken);

        // Formularios de PASO: instancia -> definicion de flujo -> nodos -> formulario del nodo.
        var instIds = tasks.Where(t => t.WorkflowInstanceId is not null).Select(t => t.WorkflowInstanceId!.Value).Distinct().ToList();
        var stepDefIds = new List<Guid>();
        if (instIds.Count > 0)
        {
            var wfDefIds = await _db.WorkflowInstances.AsNoTracking()
                .Where(i => instIds.Contains(i.Id)).Select(i => i.DefinitionId).Distinct().ToListAsync(cancellationToken);
            var nodeIds = await _db.WorkflowNodes.AsNoTracking()
                .Where(n => wfDefIds.Contains(n.DefinitionId)).Select(n => n.Id).ToListAsync(cancellationToken);
            stepDefIds = await _db.WorkflowNodeForms.AsNoTracking()
                .Where(f => nodeIds.Contains(f.NodeId)).Select(f => f.DefinitionId).Distinct().ToListAsync(cancellationToken);
        }

        var conceptSet = conceptDefIds.ToHashSet();
        var allDefIds = conceptDefIds.Concat(stepDefIds).Distinct().ToList();
        if (allDefIds.Count == 0) { return Array.Empty<BoardFormDto>(); }

        var defs = await _db.FormDefinitions.AsNoTracking()
            .Where(d => allDefIds.Contains(d.Id) && d.Status == FormStatus.Active && !d.IsArchived)
            .Select(d => new { d.Id, d.Code, d.Title })
            .ToListAsync(cancellationToken);

        return defs
            .Select(d => new BoardFormDto(d.Id, d.Code, d.Title, conceptSet.Contains(d.Id)))
            .OrderByDescending(x => x.IsConcept).ThenBy(x => x.Title)
            .ToList();
    }

    public async Task<IReadOnlyList<TaskFormDataDto>> GetBoardTaskFormValuesAsync(Guid boardId, IReadOnlyList<Guid> definitionIds, CancellationToken cancellationToken = default)
    {
        if (definitionIds.Count == 0) { return Array.Empty<TaskFormDataDto>(); }
        var defIds = definitionIds.Distinct().ToList();

        var tasks = await _db.TaskItems.AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .Select(t => new { t.Id, t.Number })
            .ToListAsync(cancellationToken);
        if (tasks.Count == 0) { return Array.Empty<TaskFormDataDto>(); }
        var byNumber = tasks.GroupBy(t => t.Number).ToDictionary(g => g.Key, g => g.First().Id);

        // Respuestas de esas definiciones ancladas a alguna tarea; el sufijo "-n" se resuelve en memoria.
        var responses = await _db.FormResponses.AsNoTracking()
            .Where(r => defIds.Contains(r.DefinitionId) && r.Reference != null)
            .Select(r => new { r.DefinitionId, r.Reference, r.Data, r.IsActive, r.Status, r.CreatedAt })
            .ToListAsync(cancellationToken);

        var result = new List<TaskFormDataDto>();
        var grouped = responses
            .Select(r => new { r.DefinitionId, r.Data, r.IsActive, r.Status, r.CreatedAt, TaskNumber = StripOrdinal(r.Reference) })
            .Where(r => r.TaskNumber != null && byNumber.ContainsKey(r.TaskNumber))
            .GroupBy(r => (TaskNumber: r.TaskNumber!, r.DefinitionId));
        foreach (var g in grouped)
        {
            // Efectivo: el activo (concepto); si no, la respuesta Submitted mas reciente (paso); si no, la mas reciente.
            var chosen = g.FirstOrDefault(x => x.IsActive)
                ?? g.Where(x => x.Status == FormResponseStatus.Submitted).OrderByDescending(x => x.CreatedAt).FirstOrDefault()
                ?? g.OrderByDescending(x => x.CreatedAt).First();
            result.Add(new TaskFormDataDto(byNumber[g.Key.TaskNumber], g.Key.DefinitionId, chosen.Data));
        }
        return result;
    }

    public async Task<IReadOnlyList<BoardGridSourceDto>> GetBoardGridSourcesAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var forms = await GetBoardFormsAsync(boardId, cancellationToken);
        if (forms.Count == 0) { return Array.Empty<BoardGridSourceDto>(); }
        var defIds = forms.Select(f => f.DefinitionId).ToList();
        var grids = await _db.FormQuestions.AsNoTracking()
            .Where(q => defIds.Contains(q.DefinitionId) && q.ControlType == FormControlType.GridDetail)
            .Select(q => new { q.DefinitionId, q.FieldCode, q.Label, q.OptionsJson })
            .ToListAsync(cancellationToken);
        var titleByDef = forms.GroupBy(f => f.DefinitionId).ToDictionary(g => g.Key, g => g.First().Title);
        var result = new List<BoardGridSourceDto>();
        foreach (var g in grids)
        {
            var cols = Calc.FormGridCalculator.ParseColumns(g.OptionsJson);
            if (cols.Count == 0) { continue; }
            result.Add(new BoardGridSourceDto(
                g.DefinitionId,
                titleByDef.TryGetValue(g.DefinitionId, out var t) ? t : "(formulario)",
                g.FieldCode,
                string.IsNullOrWhiteSpace(g.Label) ? g.FieldCode : g.Label,
                cols.Select(c => new BoardGridColumnDto(c.Id, c.Label, c.Format)).ToList()));
        }
        return result;
    }

    public async Task<IReadOnlyList<TaskGridRowsDto>> GetBoardTaskGridRowsAsync(Guid boardId, Guid formDefId, string gridFieldCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gridFieldCode)) { return Array.Empty<TaskGridRowsDto>(); }
        // Reusa el Data del formulario EFECTIVO por tarea; de ahi se extrae el valor del GridDetail.
        var values = await GetBoardTaskFormValuesAsync(boardId, new[] { formDefId }, cancellationToken);
        var result = new List<TaskGridRowsDto>();
        foreach (var v in values)
        {
            var gridValue = ExtractFieldValue(v.Data, gridFieldCode);
            var rows = FormFieldValidator.ParseGridRows(gridValue)
                .Select(r => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(r, StringComparer.Ordinal))
                .ToList();
            if (rows.Count > 0) { result.Add(new TaskGridRowsDto(v.TaskItemId, rows)); }
        }
        return result;
    }

    /// <summary>Extrae el string de valor de un campo del documento Data ({ code: {value,type} }).</summary>
    private static string? ExtractFieldValue(string? dataJson, string fieldCode)
    {
        if (string.IsNullOrWhiteSpace(dataJson)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return null; }
            if (!doc.RootElement.TryGetProperty(fieldCode, out var fld)) { return null; }
            if (fld.ValueKind == JsonValueKind.Object && fld.TryGetProperty("value", out var val))
            {
                return val.ValueKind == JsonValueKind.String ? val.GetString() : val.GetRawText();
            }
            return fld.ValueKind == JsonValueKind.String ? fld.GetString() : fld.GetRawText();
        }
        catch (JsonException) { return null; }
    }

    // ---- Numeracion heredada de formularios de la tarea: Reference = "{numero tarea}-{n}" ----

    /// <summary>Primer FieldCode de la definicion cuyo Label contiene alguna de las pistas. Null si ninguno.</summary>
    private async Task<string?> ResolveFieldCodeAsync(Guid defId, CancellationToken ct, params string[] needles)
    {
        var q = await _db.FormQuestions.AsNoTracking()
            .Where(x => x.DefinitionId == defId).Select(x => new { x.FieldCode, x.Label }).ToListAsync(ct);
        return q.FirstOrDefault(x => needles.Any(n => (x.Label ?? "").Contains(n, StringComparison.OrdinalIgnoreCase)))?.FieldCode;
    }

    /// <summary>Campo "numero" del formulario (code + tipo) para heredar el numero de la tarea; null si no hay.</summary>
    private async Task<(string Code, string Type)?> ResolveNumberFieldAsync(Guid defId, CancellationToken ct)
    {
        var q = await _db.FormQuestions.AsNoTracking()
            .Where(x => x.DefinitionId == defId).Select(x => new { x.FieldCode, x.Label, x.ControlType }).ToListAsync(ct);
        var f = q.FirstOrDefault(x => new[] { "cotiz", "numero", "número", "folio", "consecutivo" }
            .Any(n => (x.Label ?? "").Contains(n, StringComparison.OrdinalIgnoreCase)));
        return f is null ? null : (f.FieldCode, f.ControlType.ToString());
    }

    /// <summary>Siguiente ordinal para la tarea: max sufijo existente + 1 (estable ante borrados).</summary>
    private async Task<int> NextFormOrdinalAsync(Guid defId, string taskNumber, CancellationToken ct)
    {
        var prefix = taskNumber + "-";
        var refs = await _db.FormResponses.AsNoTracking()
            .Where(r => r.DefinitionId == defId
                && (r.Reference == taskNumber || (r.Reference != null && r.Reference.StartsWith(prefix))))
            .Select(r => r.Reference).ToListAsync(ct);
        var maxN = 0;
        foreach (var rf in refs)
        {
            if (string.IsNullOrEmpty(rf)) { continue; }
            var dash = rf.LastIndexOf('-');
            if (dash > 0 && int.TryParse(rf[(dash + 1)..], out var n)) { maxN = Math.Max(maxN, n); }
            else if (rf == taskNumber) { maxN = Math.Max(maxN, 1); } // legacy sin sufijo cuenta como 1
        }
        return maxN + 1;
    }

    /// <summary>Numero de la tarea a partir de un Reference "{tarea}-{n}" (o el mismo si no trae sufijo).</summary>
    private static string StripOrdinal(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) { return ""; }
        var dash = reference.LastIndexOf('-');
        return dash > 0 && int.TryParse(reference[(dash + 1)..], out _) ? reference[..dash] : reference;
    }

    /// <summary>Data para una respuesta nueva: copia sourceData (si viene, para "copiar") y escribe el numero
    /// heredado en el campo "numero" del formulario, si existe.</summary>
    private async Task<string> BuildInheritedNumberDataAsync(Guid defId, string reference, string? sourceData, CancellationToken ct)
    {
        var doc = string.IsNullOrWhiteSpace(sourceData)
            ? new Dictionary<string, FormFieldValue>(StringComparer.Ordinal)
            : (JsonSerializer.Deserialize<Dictionary<string, FormFieldValue>>(sourceData!, JsonOptions) ?? new(StringComparer.Ordinal));
        var numField = await ResolveNumberFieldAsync(defId, ct);
        if (numField is not null) { doc[numField.Value.Code] = new FormFieldValue(reference, numField.Value.Type); }
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    public async Task<FormResult<FormResponseDto>> ReopenResponseAsync(Guid responseId, CancellationToken cancellationToken = default)
    {
        var response = await _db.FormResponses.FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);
        if (response is null) { return FormResult<FormResponseDto>.NotFound("Respuesta no encontrada."); }
        if (response.Status == FormResponseStatus.Draft) { return FormResult<FormResponseDto>.Ok(ToDto(response)); }

        // Guard: no reabrir si la tarea (por su numero, tenant-scoped) esta Cerrada.
        if (!string.IsNullOrEmpty(response.Reference))
        {
            var closed = await _db.TaskItems.AsNoTracking()
                .AnyAsync(t => t.Number == response.Reference && t.Status == TaskItemStatus.Closed, cancellationToken);
            if (closed) { return FormResult<FormResponseDto>.Invalid("La tarea esta cerrada: no se puede reabrir la cotizacion."); }
        }

        response.Status = FormResponseStatus.Draft;
        response.SubmittedAt = null;
        await _db.SaveChangesAsync(cancellationToken);
        return FormResult<FormResponseDto>.Ok(ToDto(response));
    }

    /// <summary>Extrae el valor string de un campo del Data (<c>{ code: { value, type } }</c>). Null si falta.</summary>
    private static string? ExtractDataField(string? dataJson, string? fieldCode)
    {
        if (string.IsNullOrEmpty(dataJson) || string.IsNullOrEmpty(fieldCode)) { return null; }
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(fieldCode, out var f)
                && f.ValueKind == JsonValueKind.Object
                && f.TryGetProperty("value", out var v))
            {
                var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
        }
        catch (JsonException) { /* Data corrupto: sin resumen */ }
        return null;
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
