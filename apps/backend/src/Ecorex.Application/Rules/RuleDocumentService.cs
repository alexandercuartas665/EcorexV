using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Application.Rules.Verbs;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules;

/// <summary>
/// Implementacion de IRuleDocumentService (ADR-0016). Valida que el VerbName exista en el
/// registro tipado (catalogo del motor) y que ParamsJson sea un objeto JSON con los
/// parametros obligatorios del descriptor. Los vinculos validan que la pregunta/el nodo
/// pertenezcan al tenant (el filtro global ya lo garantiza; NotFound si no).
/// </summary>
public sealed class RuleDocumentService : IRuleDocumentService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IRulesEngine _engine;

    public RuleDocumentService(IApplicationDbContext db, ITenantContext tenantContext, IRulesEngine engine)
    {
        _db = db;
        _tenantContext = tenantContext;
        _engine = engine;
    }

    // ---- Documentos ----

    public async Task<IReadOnlyList<RuleDocumentDto>> ListDocumentsAsync(
        bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _db.RuleDocuments.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(d => !d.IsArchived);
        }
        var rows = await query
            .OrderBy(d => d.DocumentCode)
            .Select(d => new
            {
                Document = d,
                RuleCount = _db.Rules.Count(r => r.DocumentId == d.Id)
            })
            .ToListAsync(cancellationToken);
        return rows.Select(x => ToDto(x.Document, x.RuleCount)).ToList();
    }

    public async Task<RuleDocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _db.RuleDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }
        var count = await _db.Rules.CountAsync(r => r.DocumentId == documentId, cancellationToken);
        return ToDto(document, count);
    }

    public async Task<RuleResult<RuleDocumentDto>> CreateDocumentAsync(
        SaveRuleDocumentRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return RuleResult<RuleDocumentDto>.Invalid("No hay tenant activo.");
        }
        var error = ValidateDocument(request);
        if (error is not null)
        {
            return RuleResult<RuleDocumentDto>.Invalid(error);
        }
        var code = request.DocumentCode.Trim();
        if (await _db.RuleDocuments.AnyAsync(d => d.DocumentCode == code, cancellationToken))
        {
            return RuleResult<RuleDocumentDto>.Conflict($"Ya existe un documento con el codigo {code}.");
        }

        var document = new RuleDocument
        {
            TenantId = tenantId,
            DocumentCode = code,
            Name = request.Name.Trim(),
            Category = request.Category.Trim(),
            Description = Normalize(request.Description),
            Status = request.Status
        };
        _db.RuleDocuments.Add(document);
        await _db.SaveChangesAsync(cancellationToken);
        return RuleResult<RuleDocumentDto>.Ok(ToDto(document, 0));
    }

    public async Task<RuleResult<RuleDocumentDto>> UpdateDocumentAsync(
        Guid documentId, SaveRuleDocumentRequest request, CancellationToken cancellationToken = default)
    {
        var document = await _db.RuleDocuments.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return RuleResult<RuleDocumentDto>.NotFound("Documento de reglas no encontrado.");
        }
        var error = ValidateDocument(request);
        if (error is not null)
        {
            return RuleResult<RuleDocumentDto>.Invalid(error);
        }
        var code = request.DocumentCode.Trim();
        if (await _db.RuleDocuments.AnyAsync(d => d.DocumentCode == code && d.Id != documentId, cancellationToken))
        {
            return RuleResult<RuleDocumentDto>.Conflict($"Ya existe un documento con el codigo {code}.");
        }

        document.DocumentCode = code;
        document.Name = request.Name.Trim();
        document.Category = request.Category.Trim();
        document.Description = Normalize(request.Description);
        document.Status = request.Status;
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.Rules.CountAsync(r => r.DocumentId == documentId, cancellationToken);
        return RuleResult<RuleDocumentDto>.Ok(ToDto(document, count));
    }

    public async Task<RuleResult<bool>> SetDocumentArchivedAsync(
        Guid documentId, bool archived, CancellationToken cancellationToken = default)
    {
        var document = await _db.RuleDocuments.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return RuleResult<bool>.NotFound("Documento de reglas no encontrado.");
        }
        document.IsArchived = archived;
        await _db.SaveChangesAsync(cancellationToken);
        return RuleResult<bool>.Ok(true);
    }

    // ---- Reglas ----

    public async Task<IReadOnlyList<RuleDto>> ListRulesAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _db.Rules.AsNoTracking()
            .Where(r => r.DocumentId == documentId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.CreatedAt)
            .Select(r => new RuleDto(r.Id, r.DocumentId, r.Name, r.Description, r.VerbName,
                r.SortOrder, r.ParamsJson, r.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleListItemDto>> ListAllRulesAsync(
        bool includeArchivedDocuments = false, CancellationToken cancellationToken = default)
    {
        var query = _db.Rules.AsNoTracking()
            .Join(_db.RuleDocuments, r => r.DocumentId, d => d.Id, (r, d) => new { r, d });
        if (!includeArchivedDocuments)
        {
            query = query.Where(x => !x.d.IsArchived);
        }
        return await query
            .OrderBy(x => x.d.DocumentCode).ThenBy(x => x.r.SortOrder).ThenBy(x => x.r.CreatedAt)
            .Select(x => new RuleListItemDto(x.r.Id, x.r.DocumentId, x.d.DocumentCode, x.d.Name,
                x.d.Category, x.d.IsArchived, x.r.Name, x.r.Description, x.r.VerbName,
                x.r.SortOrder, x.r.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<RuleDto?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        return await _db.Rules.AsNoTracking()
            .Where(r => r.Id == ruleId)
            .Select(r => new RuleDto(r.Id, r.DocumentId, r.Name, r.Description, r.VerbName,
                r.SortOrder, r.ParamsJson, r.Status))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RuleResult<RuleDto>> CreateRuleAsync(
        Guid documentId, SaveRuleRequest request, CancellationToken cancellationToken = default)
    {
        var document = await _db.RuleDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
        {
            return RuleResult<RuleDto>.NotFound("Documento de reglas no encontrado.");
        }
        var error = ValidateRule(request);
        if (error is not null)
        {
            return RuleResult<RuleDto>.Invalid(error);
        }

        var rule = new Rule
        {
            TenantId = document.TenantId,
            DocumentId = documentId,
            Name = request.Name.Trim(),
            Description = Normalize(request.Description),
            VerbName = request.VerbName.Trim().ToUpperInvariant(),
            SortOrder = request.SortOrder,
            ParamsJson = Normalize(request.ParamsJson),
            Status = request.Status
        };
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return RuleResult<RuleDto>.Ok(ToDto(rule));
    }

    public async Task<RuleResult<RuleDto>> UpdateRuleAsync(
        Guid ruleId, SaveRuleRequest request, CancellationToken cancellationToken = default)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return RuleResult<RuleDto>.NotFound("Regla no encontrada.");
        }
        var error = ValidateRule(request);
        if (error is not null)
        {
            return RuleResult<RuleDto>.Invalid(error);
        }

        // Mover de documento (select Documento del editor, ADR-0023): valida que el
        // destino exista en el tenant (el filtro global oculta los ajenos).
        if (request.DocumentId is Guid targetDocumentId && targetDocumentId != rule.DocumentId)
        {
            var targetExists = await _db.RuleDocuments
                .AnyAsync(d => d.Id == targetDocumentId, cancellationToken);
            if (!targetExists)
            {
                return RuleResult<RuleDto>.NotFound("El documento destino no existe.");
            }
            rule.DocumentId = targetDocumentId;
        }

        rule.Name = request.Name.Trim();
        rule.Description = Normalize(request.Description);
        rule.VerbName = request.VerbName.Trim().ToUpperInvariant();
        rule.SortOrder = request.SortOrder;
        rule.ParamsJson = Normalize(request.ParamsJson);
        rule.Status = request.Status;
        await _db.SaveChangesAsync(cancellationToken);
        return RuleResult<RuleDto>.Ok(ToDto(rule));
    }

    public async Task<RuleResult<RuleDto>> DuplicateRuleAsync(
        Guid ruleId, CancellationToken cancellationToken = default)
    {
        var source = await _db.Rules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (source is null)
        {
            return RuleResult<RuleDto>.NotFound("Regla no encontrada.");
        }

        var maxSort = await _db.Rules
            .Where(r => r.DocumentId == source.DocumentId)
            .MaxAsync(r => (int?)r.SortOrder, cancellationToken) ?? 0;
        var name = source.Name + " (copia)";
        if (name.Length > 100)
        {
            name = source.Name[..(100 - " (copia)".Length)] + " (copia)";
        }

        // El clon nace en Development y SIN vinculos: activarlo/vincularlo es decision
        // explicita del usuario (evita ejecuciones dobles accidentales).
        var copy = new Rule
        {
            TenantId = source.TenantId,
            DocumentId = source.DocumentId,
            Name = name,
            Description = source.Description,
            VerbName = source.VerbName,
            SortOrder = maxSort + 1,
            ParamsJson = source.ParamsJson,
            Status = RuleStatus.Development
        };
        _db.Rules.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);
        return RuleResult<RuleDto>.Ok(ToDto(copy));
    }

    public async Task<RuleResult<bool>> DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await _db.Rules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return RuleResult<bool>.NotFound("Regla no encontrada.");
        }
        // Historial append-only: una regla con ejecuciones NO se borra (inactivala o deja
        // vencer el TTL de 90 dias del historial).
        if (await _db.RuleExecutionLogs.AnyAsync(l => l.RuleId == ruleId, cancellationToken))
        {
            return RuleResult<bool>.Invalid(
                "La regla tiene historial de ejecuciones: cambiala a Inactiva en vez de borrarla.");
        }

        await using var transaction = _db.HasActiveTransaction
            ? null
            : await _db.BeginTransactionAsync(cancellationToken);
        var formLinks = await _db.FormFieldRules.Where(l => l.RuleId == ruleId).ToListAsync(cancellationToken);
        var nodeLinks = await _db.WorkflowNodeRules.Where(l => l.RuleId == ruleId).ToListAsync(cancellationToken);
        _db.FormFieldRules.RemoveRange(formLinks);
        _db.WorkflowNodeRules.RemoveRange(nodeLinks);
        _db.Rules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return RuleResult<bool>.Ok(true);
    }

    // ---- Vinculos ----

    public async Task<IReadOnlyList<RuleFormLinkDto>> ListFormLinksAsync(
        Guid ruleId, CancellationToken cancellationToken = default)
    {
        return await _db.FormFieldRules.AsNoTracking()
            .Where(l => l.RuleId == ruleId)
            .Join(_db.FormQuestions, l => l.FormQuestionId, q => q.Id, (l, q) => new { l, q })
            .Join(_db.FormDefinitions, x => x.q.DefinitionId, d => d.Id, (x, d) => new { x.l, x.q, d })
            .OrderBy(x => x.d.Code).ThenBy(x => x.l.SortOrder)
            .Select(x => new RuleFormLinkDto(x.l.Id, x.l.RuleId, x.q.Id, x.q.FieldCode,
                x.q.Label, x.d.Code, x.d.Title, x.l.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QuestionRuleLinkDto>> ListQuestionLinksAsync(
        Guid formQuestionId, CancellationToken cancellationToken = default)
    {
        return await _db.FormFieldRules.AsNoTracking()
            .Where(l => l.FormQuestionId == formQuestionId)
            .Join(_db.Rules, l => l.RuleId, r => r.Id, (l, r) => new { l, r })
            .Join(_db.RuleDocuments, x => x.r.DocumentId, d => d.Id, (x, d) => new { x.l, x.r, d })
            .OrderBy(x => x.l.SortOrder).ThenBy(x => x.r.Name)
            .Select(x => new QuestionRuleLinkDto(x.l.Id, x.r.Id, x.r.Name, x.r.VerbName,
                x.d.DocumentCode, x.d.Name, x.l.SortOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleNodeLinkDto>> ListNodeLinksAsync(
        Guid ruleId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowNodeRules.AsNoTracking()
            .Where(l => l.RuleId == ruleId)
            .Join(_db.WorkflowNodes, l => l.WorkflowNodeId, n => n.Id, (l, n) => new { l, n })
            .Join(_db.WorkflowDefinitions, x => x.n.DefinitionId, d => d.Id, (x, d) => new { x.l, x.n, d })
            .OrderBy(x => x.d.ProcessCode).ThenBy(x => x.l.SortOrder)
            .Select(x => new RuleNodeLinkDto(x.l.Id, x.l.RuleId, x.n.Id, x.n.BpmnElementId,
                x.n.Name, x.d.ProcessCode, x.d.Name, x.l.SortOrder, x.l.IsAutonomous))
            .ToListAsync(cancellationToken);
    }

    public async Task<RuleResult<RuleFormLinkDto>> LinkToQuestionAsync(
        Guid ruleId, Guid formQuestionId, int sortOrder = 0, CancellationToken cancellationToken = default)
    {
        var rule = await _db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return RuleResult<RuleFormLinkDto>.NotFound("Regla no encontrada.");
        }
        var question = await _db.FormQuestions.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == formQuestionId, cancellationToken);
        if (question is null)
        {
            return RuleResult<RuleFormLinkDto>.NotFound("Pregunta de formulario no encontrada.");
        }
        if (await _db.FormFieldRules.AnyAsync(
                l => l.FormQuestionId == formQuestionId && l.RuleId == ruleId, cancellationToken))
        {
            return RuleResult<RuleFormLinkDto>.Conflict("La regla ya esta vinculada a esa pregunta.");
        }

        var link = new FormFieldRule
        {
            TenantId = rule.TenantId,
            FormQuestionId = formQuestionId,
            RuleId = ruleId,
            SortOrder = sortOrder
        };
        _db.FormFieldRules.Add(link);
        await _db.SaveChangesAsync(cancellationToken);

        var definition = await _db.FormDefinitions.AsNoTracking()
            .FirstAsync(d => d.Id == question.DefinitionId, cancellationToken);
        return RuleResult<RuleFormLinkDto>.Ok(new RuleFormLinkDto(
            link.Id, ruleId, question.Id, question.FieldCode, question.Label,
            definition.Code, definition.Title, sortOrder));
    }

    public async Task<RuleResult<bool>> UnlinkQuestionAsync(
        Guid formFieldRuleId, CancellationToken cancellationToken = default)
    {
        var link = await _db.FormFieldRules.FirstOrDefaultAsync(l => l.Id == formFieldRuleId, cancellationToken);
        if (link is null)
        {
            return RuleResult<bool>.NotFound("Vinculo no encontrado.");
        }
        _db.FormFieldRules.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        return RuleResult<bool>.Ok(true);
    }

    public async Task<RuleResult<QuestionRuleLinkDto>> CreateFieldConditionRuleAsync(
        CreateFieldConditionRequest request, CancellationToken cancellationToken = default)
    {
        // La pregunta disparadora y el campo fuente deben ser del mismo formulario: la regla se ejecuta
        // en el renderer de ESE formulario y solo ve sus campos.
        var trigger = await _db.FormQuestions.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == request.TriggerQuestionId && q.DefinitionId == request.DefinitionId, cancellationToken);
        if (trigger is null)
        {
            return RuleResult<QuestionRuleLinkDto>.NotFound("La pregunta disparadora no existe en este formulario.");
        }

        var op = (request.Operator ?? "").Trim();
        if (op is not ("equals" or "notEquals" or "empty" or "notEmpty"))
        {
            return RuleResult<QuestionRuleLinkDto>.Invalid("Operador invalido.");
        }
        var effect = (request.Effect ?? "").Trim();
        if (effect is not ("hide" or "show" or "require" or "optional"))
        {
            return RuleResult<QuestionRuleLinkDto>.Invalid("Efecto invalido.");
        }
        if (string.IsNullOrWhiteSpace(request.SourceFieldCode) || string.IsNullOrWhiteSpace(request.TargetFieldCode))
        {
            return RuleResult<QuestionRuleLinkDto>.Invalid("Faltan el campo evaluado o el campo objetivo.");
        }

        var definition = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DefinitionId, cancellationToken);
        if (definition is null)
        {
            return RuleResult<QuestionRuleLinkDto>.NotFound("Formulario no encontrado.");
        }

        // Un documento por formulario reune sus reglas de campo, para que el disenador no salga al
        // modulo de Reglas. Se crea la primera vez y se reutiliza despues.
        var docCode = $"FRMRULES-{definition.Code}";
        var docId = await _db.RuleDocuments.AsNoTracking()
            .Where(d => d.DocumentCode == docCode)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (docId is null)
        {
            var created = await CreateDocumentAsync(new SaveRuleDocumentRequest(
                docCode, $"Reglas de campo - {definition.Title}", "Formularios",
                "Reglas condicionales creadas desde el constructor de formularios.",
                RuleStatus.Active), cancellationToken);
            if (!created.IsOk || created.Value is null) { return RuleResult<QuestionRuleLinkDto>.Invalid(created.Error ?? "No se pudo crear el documento de reglas."); }
            docId = created.Value.Id;
        }

        var paramsJson = JsonSerializer.Serialize(new
        {
            sourceField = request.SourceFieldCode,
            @operator = op,
            value = request.Value,
            targetField = request.TargetFieldCode,
            effect
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var opLabel = op switch { "equals" => "=", "notEquals" => "!=", "empty" => "vacio", _ => "con valor" };
        var ruleName = $"Si {request.SourceFieldCode} {opLabel} {request.Value} -> {effect} {request.TargetFieldCode}";

        var order = await _db.FormFieldRules.CountAsync(l => l.FormQuestionId == request.TriggerQuestionId, cancellationToken);
        var ruleRes = await CreateRuleAsync(docId.Value, new SaveRuleRequest(
            ruleName, BloquearCampoPorCondicionVerb.VerbName, ParamsJson: paramsJson,
            Status: RuleStatus.Active), cancellationToken);
        if (!ruleRes.IsOk || ruleRes.Value is null) { return RuleResult<QuestionRuleLinkDto>.Invalid(ruleRes.Error ?? "No se pudo crear la regla."); }

        var linkRes = await LinkToQuestionAsync(ruleRes.Value.Id, request.TriggerQuestionId, order, cancellationToken);
        if (!linkRes.IsOk || linkRes.Value is null) { return RuleResult<QuestionRuleLinkDto>.Invalid(linkRes.Error ?? "No se pudo vincular la regla."); }

        return RuleResult<QuestionRuleLinkDto>.Ok(new QuestionRuleLinkDto(
            linkRes.Value.Id, ruleRes.Value.Id, ruleName, BloquearCampoPorCondicionVerb.VerbName,
            docCode, $"Reglas de campo - {definition.Title}", order));
    }

    public async Task<RuleResult<RuleNodeLinkDto>> LinkToNodeAsync(
        Guid ruleId, Guid workflowNodeId, int sortOrder = 0, bool isAutonomous = true,
        CancellationToken cancellationToken = default)
    {
        var rule = await _db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return RuleResult<RuleNodeLinkDto>.NotFound("Regla no encontrada.");
        }
        var node = await _db.WorkflowNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == workflowNodeId, cancellationToken);
        if (node is null)
        {
            return RuleResult<RuleNodeLinkDto>.NotFound("Nodo de flujo no encontrado.");
        }
        if (node.NodeType != WorkflowNodeType.Task)
        {
            return RuleResult<RuleNodeLinkDto>.Invalid("Solo los nodos Task ejecutan reglas.");
        }
        if (await _db.WorkflowNodeRules.AnyAsync(
                l => l.WorkflowNodeId == workflowNodeId && l.RuleId == ruleId, cancellationToken))
        {
            return RuleResult<RuleNodeLinkDto>.Conflict("La regla ya esta vinculada a ese nodo.");
        }

        var link = new WorkflowNodeRule
        {
            TenantId = rule.TenantId,
            WorkflowNodeId = workflowNodeId,
            RuleId = ruleId,
            SortOrder = sortOrder,
            IsAutonomous = isAutonomous
        };
        _db.WorkflowNodeRules.Add(link);
        await _db.SaveChangesAsync(cancellationToken);

        var definition = await _db.WorkflowDefinitions.AsNoTracking()
            .FirstAsync(d => d.Id == node.DefinitionId, cancellationToken);
        return RuleResult<RuleNodeLinkDto>.Ok(new RuleNodeLinkDto(
            link.Id, ruleId, node.Id, node.BpmnElementId, node.Name,
            definition.ProcessCode, definition.Name, sortOrder, isAutonomous));
    }

    public async Task<RuleResult<bool>> UnlinkNodeAsync(
        Guid workflowNodeRuleId, CancellationToken cancellationToken = default)
    {
        var link = await _db.WorkflowNodeRules.FirstOrDefaultAsync(l => l.Id == workflowNodeRuleId, cancellationToken);
        if (link is null)
        {
            return RuleResult<bool>.NotFound("Vinculo no encontrado.");
        }
        _db.WorkflowNodeRules.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        return RuleResult<bool>.Ok(true);
    }

    // ---- Historial ----

    public async Task<IReadOnlyList<RuleExecutionLogDto>> ListExecutionLogsAsync(
        Guid? documentId = null, Guid? ruleId = null, int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _db.RuleExecutionLogs.AsNoTracking();
        if (ruleId is Guid rid)
        {
            query = query.Where(l => l.RuleId == rid);
        }
        else if (documentId is Guid did)
        {
            query = query.Where(l => _db.Rules.Any(r => r.Id == l.RuleId && r.DocumentId == did));
        }
        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(l => new RuleExecutionLogDto(l.Id, l.RuleId, l.RuleNameSnapshot,
                l.TriggerKind, l.Status, l.RecordsAffected, l.DurationMs, l.ErrorMessage,
                l.ExecutedByTenantUserId, l.CreatedAt, l.ExpiresAt,
                l.ExecutedByTenantUserId == null
                    ? null
                    : _db.TenantUsers
                        .Where(u => u.Id == l.ExecutedByTenantUserId)
                        .Select(u => _db.PlatformUsers
                            .Where(p => p.Id == u.PlatformUserId)
                            .Select(p => p.DisplayName)
                            .FirstOrDefault() ?? u.Email)
                        .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    // ---- Metricas (ADR-0023: KPIs del topbar + panel Propiedades) ----

    public async Task<RuleTenantStatsDto> GetTenantStatsAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var documents = await _db.RuleDocuments.CountAsync(d => !d.IsArchived, cancellationToken);
        var rules = await _db.Rules.CountAsync(cancellationToken);
        var window = await AggregateWindowAsync(_db.RuleExecutionLogs.Where(l => l.CreatedAt >= since), cancellationToken);
        return new RuleTenantStatsDto(documents, rules, window.Total, window.SuccessRate, window.AvgMs);
    }

    public async Task<RuleMetricsDto> GetRuleMetricsAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var window = await AggregateWindowAsync(
            _db.RuleExecutionLogs.Where(l => l.RuleId == ruleId && l.CreatedAt >= since), cancellationToken);
        return new RuleMetricsDto(window.Total, window.Success, window.Failed, window.SuccessRate, window.AvgMs);
    }

    public async Task<RuleAuditDto?> GetRuleAuditAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await _db.Rules.AsNoTracking()
            .Where(r => r.Id == ruleId)
            .Select(r => new { r.CreatedAt, r.CreatedBy, r.UpdatedAt, r.UpdatedBy })
            .FirstOrDefaultAsync(cancellationToken);
        if (rule is null)
        {
            return null;
        }
        return new RuleAuditDto(
            rule.CreatedAt, await ResolvePlatformUserNameAsync(rule.CreatedBy, cancellationToken),
            rule.UpdatedAt, await ResolvePlatformUserNameAsync(rule.UpdatedBy, cancellationToken));
    }

    public async Task<Guid?> GetCurrentTenantUserIdAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.UserId is not Guid platformUserId)
        {
            return null;
        }
        return await _db.TenantUsers.AsNoTracking()
            .Where(u => u.PlatformUserId == platformUserId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Agrega la ventana de ejecuciones: total, exitos, fallos, tasa Success/(Success+Failed)
    /// (las Skipped no cuentan en la tasa) y promedio de duracion en ms.
    /// </summary>
    private static async Task<(int Total, int Success, int Failed, double? SuccessRate, int? AvgMs)>
        AggregateWindowAsync(IQueryable<RuleExecutionLog> logs, CancellationToken cancellationToken)
    {
        var agg = await logs
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Success = g.Count(l => l.Status == RuleExecutionStatus.Success),
                Failed = g.Count(l => l.Status == RuleExecutionStatus.Failed),
                AvgMs = g.Average(l => (double?)l.DurationMs)
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (agg is null || agg.Total == 0)
        {
            return (0, 0, 0, null, null);
        }
        var denominator = agg.Success + agg.Failed;
        double? rate = denominator == 0 ? null : (double)agg.Success / denominator;
        return (agg.Total, agg.Success, agg.Failed, rate, (int?)agg.AvgMs);
    }

    private async Task<string?> ResolvePlatformUserNameAsync(Guid? platformUserId, CancellationToken cancellationToken)
    {
        if (platformUserId is not Guid id)
        {
            return null;
        }
        return await _db.PlatformUsers.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => p.DisplayName ?? p.Email)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ---- Opciones para combos ----

    public async Task<IReadOnlyList<RuleOption>> ListFormDefinitionOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.FormDefinitions.AsNoTracking()
            .Where(d => !d.IsArchived)
            .OrderBy(d => d.Code)
            .Select(d => new RuleOption(d.Id, d.Code + " - " + d.Title))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleOption>> ListFormQuestionOptionsAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
    {
        return await _db.FormQuestions.AsNoTracking()
            .Where(q => q.DefinitionId == definitionId)
            .OrderBy(q => q.SortOrder)
            .Select(q => new RuleOption(q.Id, q.FieldCode + " - " + q.Label))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleOption>> ListWorkflowDefinitionOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => !d.IsArchived)
            .OrderBy(d => d.ProcessCode).ThenByDescending(d => d.Version)
            .Select(d => new RuleOption(d.Id,
                d.ProcessCode + " v" + d.Version + " - " + d.Name + (d.IsPublished ? " (publicado)" : "")))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RuleOption>> ListWorkflowNodeOptionsAsync(
        Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        return await _db.WorkflowNodes.AsNoTracking()
            .Where(n => n.DefinitionId == workflowDefinitionId && n.NodeType == WorkflowNodeType.Task)
            .OrderBy(n => n.StepNumber)
            .Select(n => new RuleOption(n.Id, (n.Name ?? n.BpmnElementId) + " [" + n.BpmnElementId + "]"))
            .ToListAsync(cancellationToken);
    }

    // ---- Helpers ----

    private static RuleDocumentDto ToDto(RuleDocument d, int ruleCount)
        => new(d.Id, d.DocumentCode, d.Name, d.Category, d.Description, d.Status, d.IsArchived, ruleCount);

    private static RuleDto ToDto(Rule r)
        => new(r.Id, r.DocumentId, r.Name, r.Description, r.VerbName, r.SortOrder, r.ParamsJson, r.Status);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidateDocument(SaveRuleDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentCode) || request.DocumentCode.Trim().Length > 25)
        {
            return "El codigo del documento es obligatorio (maximo 25 caracteres).";
        }
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            return "El nombre es obligatorio (maximo 100 caracteres).";
        }
        if (string.IsNullOrWhiteSpace(request.Category) || request.Category.Trim().Length > 100)
        {
            return "La categoria es obligatoria (maximo 100 caracteres).";
        }
        return null;
    }

    /// <summary>Valida nombre, verbo registrado y ParamsJson (objeto + obligatorios del descriptor).</summary>
    private string? ValidateRule(SaveRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            return "El nombre de la regla es obligatorio (maximo 100 caracteres).";
        }
        if (string.IsNullOrWhiteSpace(request.VerbName))
        {
            return "El verbo es obligatorio.";
        }
        var descriptor = _engine.FindVerb(request.VerbName);
        if (descriptor is null)
        {
            return $"Verbo no registrado en el catalogo: {request.VerbName.Trim()}.";
        }

        Dictionary<string, JsonElement> parameters = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.ParamsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(request.ParamsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return "ParamsJson debe ser un objeto JSON ({\"param\":valor,...}).";
                }
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    parameters[property.Name] = property.Value.Clone();
                }
            }
            catch (JsonException ex)
            {
                return $"ParamsJson invalido: {ex.Message}";
            }
        }
        var missing = descriptor.Params
            .Where(p => p.Required && !parameters.ContainsKey(p.Name))
            .Select(p => p.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return $"Faltan parametros obligatorios del verbo {descriptor.VerbName}: {string.Join(", ", missing)}.";
        }
        return null;
    }
}
