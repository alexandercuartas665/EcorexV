using System.Diagnostics;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ecorex.Application.Rules;

/// <summary>
/// Implementacion de IRulesEngine (ADR-0016). Los verbos se resuelven de forma DIFERIDA
/// desde el ServiceProvider (GetServices de IRuleVerb) para evitar el ciclo de construccion
/// WorkflowEngine -> IWorkflowRuleHook -> IRulesEngine -> verbos -> ITaskItemService ->
/// IWorkflowEngine: en el momento de ejecutar, todos los servicios del scope ya existen.
/// El historial se escribe SIEMPRE (exito/fallo/skip) con ExpiresAt = ahora + 90 dias.
/// </summary>
public sealed class RulesEngine : IRulesEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IServiceProvider _services;
    private Dictionary<string, IRuleVerb>? _verbs;

    public RulesEngine(IApplicationDbContext db, ITenantContext tenantContext, IServiceProvider services)
    {
        _db = db;
        _tenantContext = tenantContext;
        _services = services;
    }

    /// <summary>Registro tipado: diccionario VerbName -> instancia, resuelto una vez por scope.</summary>
    private Dictionary<string, IRuleVerb> Verbs
        => _verbs ??= _services.GetServices<IRuleVerb>()
            .ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RuleVerbDescriptor> GetVerbCatalog()
        => Verbs.Values.Select(v => v.Descriptor).OrderBy(d => d.VerbName, StringComparer.Ordinal).ToList();

    public RuleVerbDescriptor? FindVerb(string verbName)
        => Verbs.TryGetValue((verbName ?? "").Trim(), out var verb) ? verb.Descriptor : null;

    public async Task<RuleResult<RuleExecutionOutcome>> ExecuteRuleAsync(
        Guid ruleId, RuleInvocation invocation, CancellationToken cancellationToken = default)
    {
        var row = await _db.Rules
            .Join(_db.RuleDocuments, r => r.DocumentId, d => d.Id, (r, d) => new { Rule = r, Document = d })
            .FirstOrDefaultAsync(x => x.Rule.Id == ruleId, cancellationToken);
        if (row is null)
        {
            return RuleResult<RuleExecutionOutcome>.NotFound("Regla no encontrada.");
        }
        var rule = row.Rule;

        // Reglas Inactive (o de documento archivado) no corren nunca; en Development solo
        // corren con disparador Manual ("Ejecutar prueba"). Queda constancia como Skipped.
        var eligible = invocation.TriggerKind == RuleTriggerKind.Manual
            ? rule.Status != RuleStatus.Inactive && !row.Document.IsArchived
            : rule.Status == RuleStatus.Active && !row.Document.IsArchived
                && row.Document.Status == RuleStatus.Active;
        if (!eligible)
        {
            var skipped = await WriteLogAsync(rule, invocation, RuleExecutionStatus.Skipped,
                recordsAffected: 0, durationMs: 0,
                error: $"Regla no elegible para el disparador {invocation.TriggerKind} (estado {rule.Status}).",
                cancellationToken);
            return RuleResult<RuleExecutionOutcome>.Ok(skipped);
        }

        // REGISTRO TIPADO: verbo desconocido = error tipado registrado en historial (nunca
        // Activator.CreateInstance sobre texto, el RCE del legacy).
        if (!Verbs.TryGetValue(rule.VerbName.Trim(), out var verb))
        {
            var failed = await WriteLogAsync(rule, invocation, RuleExecutionStatus.Failed,
                recordsAffected: 0, durationMs: 0,
                error: $"Verbo no registrado en el catalogo: {rule.VerbName}.", cancellationToken);
            return RuleResult<RuleExecutionOutcome>.InvalidWithValue(failed,
                $"Verbo no registrado en el catalogo: {rule.VerbName}.");
        }

        var (parameters, paramsError) = ParseParams(rule.ParamsJson);
        if (paramsError is not null)
        {
            var failed = await WriteLogAsync(rule, invocation, RuleExecutionStatus.Failed,
                recordsAffected: 0, durationMs: 0, error: paramsError, cancellationToken);
            return RuleResult<RuleExecutionOutcome>.InvalidWithValue(failed, paramsError);
        }

        var context = new RuleContext
        {
            TenantId = rule.TenantId,
            RuleId = rule.Id,
            TriggerKind = invocation.TriggerKind,
            Params = parameters,
            TaskItemId = invocation.TaskItemId,
            WorkflowInstanceId = invocation.WorkflowInstanceId,
            NodeId = invocation.NodeId,
            FormResponseId = invocation.FormResponseId,
            FormData = invocation.FormData is null
                ? new Dictionary<string, string?>(StringComparer.Ordinal)
                : new Dictionary<string, string?>(invocation.FormData, StringComparer.Ordinal),
            ExecutedByTenantUserId = invocation.ExecutedByTenantUserId,
            ActorUserId = invocation.ActorUserId,
            ActorName = invocation.ActorName
        };

        var stopwatch = Stopwatch.StartNew();
        RuleVerbResult verbResult;
        try
        {
            verbResult = await verb.ExecuteAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // El verbo NUNCA tumba al llamador: el fallo queda tipado y en el historial.
            verbResult = RuleVerbResult.Fail($"Excepcion en el verbo {verb.Name}: {ex.Message}");
        }
        stopwatch.Stop();

        var status = verbResult.Success ? RuleExecutionStatus.Success : RuleExecutionStatus.Failed;
        var outcome = await WriteLogAsync(rule, invocation, status,
            verbResult.RecordsAffected, (int)stopwatch.ElapsedMilliseconds,
            verbResult.Success ? null : verbResult.Message, cancellationToken,
            verbResult.Message, verbResult.ActionList, verbResult.AutoCompleteStep, context.FormData);

        return RuleResult<RuleExecutionOutcome>.Ok(outcome);
    }

    public async Task<FormFieldRulesOutcome> ExecuteForFormFieldAsync(
        Guid formQuestionId, IReadOnlyDictionary<string, string?> formData,
        Guid? formResponseId = null, Guid? executedByTenantUserId = null,
        Guid? actorUserId = null, string actorName = "Sistema",
        CancellationToken cancellationToken = default)
    {
        var ruleIds = await _db.FormFieldRules
            .Where(l => l.FormQuestionId == formQuestionId)
            .Join(_db.Rules, l => l.RuleId, r => r.Id, (l, r) => new { l.SortOrder, Rule = r })
            .Where(x => x.Rule.Status == RuleStatus.Active)
            .Join(_db.RuleDocuments, x => x.Rule.DocumentId, d => d.Id,
                (x, d) => new { x.SortOrder, x.Rule.Id, Document = d })
            .Where(x => x.Document.Status == RuleStatus.Active && !x.Document.IsArchived)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var executions = new List<RuleExecutionOutcome>();
        var actions = new List<RuleAction>();
        var currentData = new Dictionary<string, string?>(formData, StringComparer.Ordinal);
        foreach (var ruleId in ruleIds)
        {
            var result = await ExecuteRuleAsync(ruleId, new RuleInvocation(
                RuleTriggerKind.FormField, currentData,
                FormResponseId: formResponseId,
                ExecutedByTenantUserId: executedByTenantUserId,
                ActorUserId: actorUserId, ActorName: actorName), cancellationToken);
            if (result.Value is not RuleExecutionOutcome outcome)
            {
                continue;
            }
            executions.Add(outcome);
            actions.AddRange(outcome.Actions);
            // Propaga los SetFieldValue al FormData de las reglas siguientes.
            foreach (var action in outcome.Actions.Where(a => a.Kind == RuleActionKind.SetFieldValue))
            {
                currentData[action.FieldCode] = action.Value;
            }
        }

        return new FormFieldRulesOutcome(executions, actions, currentData);
    }

    public async Task<WorkflowNodeRulesOutcome> ExecuteForWorkflowNodeAsync(
        Guid workflowNodeId, Guid? workflowInstanceId = null, Guid? taskItemId = null,
        CancellationToken cancellationToken = default)
    {
        var ruleIds = await _db.WorkflowNodeRules
            .Where(l => l.WorkflowNodeId == workflowNodeId && l.IsAutonomous)
            .Join(_db.Rules, l => l.RuleId, r => r.Id, (l, r) => new { l.SortOrder, Rule = r })
            .Where(x => x.Rule.Status == RuleStatus.Active)
            .Join(_db.RuleDocuments, x => x.Rule.DocumentId, d => d.Id,
                (x, d) => new { x.SortOrder, x.Rule.Id, Document = d })
            .Where(x => x.Document.Status == RuleStatus.Active && !x.Document.IsArchived)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var executions = new List<RuleExecutionOutcome>();
        var allSucceeded = true;
        var autoComplete = false;
        foreach (var ruleId in ruleIds)
        {
            var result = await ExecuteRuleAsync(ruleId, new RuleInvocation(
                RuleTriggerKind.WorkflowNode,
                NodeId: workflowNodeId,
                WorkflowInstanceId: workflowInstanceId,
                TaskItemId: taskItemId), cancellationToken);
            if (result.Value is not RuleExecutionOutcome outcome)
            {
                allSucceeded = false;
                continue;
            }
            executions.Add(outcome);
            if (outcome.Status != RuleExecutionStatus.Success)
            {
                allSucceeded = false;
            }
            else if (outcome.AutoCompleteStep)
            {
                autoComplete = true;
            }
        }

        return new WorkflowNodeRulesOutcome(
            executions, allSucceeded && executions.Count > 0, allSucceeded && autoComplete);
    }

    // ---- Helpers ----

    private static (IReadOnlyDictionary<string, JsonElement> Params, string? Error) ParseParams(string? paramsJson)
    {
        var empty = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return (empty, null);
        }
        try
        {
            using var doc = JsonDocument.Parse(paramsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (empty, "ParamsJson debe ser un objeto JSON ({\"param\":valor,...}).");
            }
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.Clone();
            }
            return (result, null);
        }
        catch (JsonException ex)
        {
            return (empty, $"ParamsJson invalido: {ex.Message}");
        }
    }

    /// <summary>
    /// Escribe la fila append-only del historial (SIEMPRE, exito o fallo) con TTL de 90
    /// dias y devuelve el outcome tipado. Participa en la transaccion ambiente si la hay.
    /// </summary>
    private async Task<RuleExecutionOutcome> WriteLogAsync(
        Rule rule, RuleInvocation invocation, RuleExecutionStatus status,
        int recordsAffected, int durationMs, string? error, CancellationToken cancellationToken,
        string? message = null, IReadOnlyList<RuleAction>? actions = null,
        bool autoCompleteStep = false, IReadOnlyDictionary<string, string?>? finalFormData = null)
    {
        var contextJson = JsonSerializer.Serialize(new
        {
            triggerKind = invocation.TriggerKind.ToString(),
            taskItemId = invocation.TaskItemId,
            workflowInstanceId = invocation.WorkflowInstanceId,
            nodeId = invocation.NodeId,
            formResponseId = invocation.FormResponseId,
            formData = finalFormData ?? invocation.FormData
        }, JsonOptions);

        var log = new RuleExecutionLog
        {
            TenantId = rule.TenantId,
            RuleId = rule.Id,
            ExecutedByTenantUserId = invocation.ExecutedByTenantUserId,
            RuleNameSnapshot = rule.Name.Length > 100 ? rule.Name[..100] : rule.Name,
            TriggerKind = invocation.TriggerKind,
            ContextJson = contextJson,
            Status = status,
            RecordsAffected = recordsAffected,
            DurationMs = durationMs,
            ErrorMessage = error is { Length: > 2000 } ? error[..2000] : error,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(IRulesEngine.HistoryTtlDays)
        };
        _db.RuleExecutionLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        return new RuleExecutionOutcome(
            rule.Id, rule.Name, rule.VerbName, status, message ?? error,
            recordsAffected, durationMs, actions ?? [], autoCompleteStep, error);
    }
}
