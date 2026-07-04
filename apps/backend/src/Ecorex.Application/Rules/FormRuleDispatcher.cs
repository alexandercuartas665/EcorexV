using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Rules;

/// <summary>
/// Fachada MINIMA entre el DynamicFormRenderer y el RulesEngine (encapsula la integracion
/// para no ensuciar el renderer, ADR-0016): resuelve que campos de una definicion tienen
/// reglas activas vinculadas (para no consultar en cada onchange) y despacha la ejecucion
/// al cambiar un campo, devolviendo las acciones de UI y el FormData resultante.
/// </summary>
public interface IFormRuleDispatcher
{
    /// <summary>FieldCodes de la definicion con reglas ACTIVAS vinculadas (disparadores).</summary>
    Task<IReadOnlySet<string>> GetTriggerFieldCodesAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta las reglas del campo cambiado (en SortOrder) y devuelve acciones + FormData.
    /// Si el campo no tiene reglas, devuelve un resultado vacio sin tocar la base.
    /// </summary>
    Task<FormFieldRulesOutcome> OnFieldChangedAsync(
        Guid definitionId, string fieldCode, IReadOnlyDictionary<string, string?> values,
        Guid? formResponseId = null, Guid? executedByTenantUserId = null,
        Guid? actorUserId = null, string actorName = "Sistema",
        CancellationToken cancellationToken = default);
}

public sealed class FormRuleDispatcher : IFormRuleDispatcher
{
    private static readonly FormFieldRulesOutcome Empty = new([], [],
        new Dictionary<string, string?>(StringComparer.Ordinal));

    private readonly IApplicationDbContext _db;
    private readonly IRulesEngine _engine;

    public FormRuleDispatcher(IApplicationDbContext db, IRulesEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<IReadOnlySet<string>> GetTriggerFieldCodesAsync(
        Guid definitionId, CancellationToken cancellationToken = default)
    {
        var codes = await _db.FormFieldRules
            .Join(_db.FormQuestions, l => l.FormQuestionId, q => q.Id, (l, r) => new { Link = l, Question = r })
            .Where(x => x.Question.DefinitionId == definitionId)
            .Join(_db.Rules, x => x.Link.RuleId, r => r.Id, (x, r) => new { x.Question.FieldCode, Rule = r })
            .Where(x => x.Rule.Status == RuleStatus.Active)
            .Select(x => x.FieldCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<FormFieldRulesOutcome> OnFieldChangedAsync(
        Guid definitionId, string fieldCode, IReadOnlyDictionary<string, string?> values,
        Guid? formResponseId = null, Guid? executedByTenantUserId = null,
        Guid? actorUserId = null, string actorName = "Sistema",
        CancellationToken cancellationToken = default)
    {
        var questionId = await _db.FormQuestions
            .Where(q => q.DefinitionId == definitionId && q.FieldCode == fieldCode)
            .Select(q => (Guid?)q.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (questionId is not Guid id)
        {
            return Empty;
        }
        return await _engine.ExecuteForFormFieldAsync(
            id, values, formResponseId, executedByTenantUserId, actorUserId, actorName, cancellationToken);
    }
}

/// <summary>
/// Estado de UI derivado de las acciones de regla, compartido entre el renderer y los
/// tests: campos ocultos por regla (que NO se validan como requeridos) y overrides de
/// obligatoriedad. Apply tambien vuelca los SetFieldValue sobre el diccionario de valores.
/// </summary>
public sealed class FormRuleUiState
{
    public HashSet<string> HiddenFields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, bool> RequiredOverrides { get; } = new(StringComparer.Ordinal);

    public void Apply(IEnumerable<RuleAction> actions, IDictionary<string, string?> values)
    {
        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case RuleActionKind.HideField:
                    HiddenFields.Add(action.FieldCode);
                    break;
                case RuleActionKind.ShowField:
                    HiddenFields.Remove(action.FieldCode);
                    break;
                case RuleActionKind.SetFieldValue:
                    values[action.FieldCode] = action.Value;
                    break;
                case RuleActionKind.SetRequired:
                    RequiredOverrides[action.FieldCode] = action.Required;
                    break;
            }
        }
    }

    public bool IsHidden(string fieldCode) => HiddenFields.Contains(fieldCode);

    /// <summary>Obligatoriedad efectiva: override de regla o el Required de la pregunta.</summary>
    public bool IsRequired(string fieldCode, bool questionRequired)
        => RequiredOverrides.TryGetValue(fieldCode, out var overridden) ? overridden : questionRequired;
}
