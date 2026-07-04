namespace Ecorex.Application.Rules;

/// <summary>
/// CRUD del modulo de reglas (documento de configuracion + reglas + vinculos + historial,
/// port del modulo legacy 000802). Los documentos NUNCA se borran fisicamente (se
/// archivan); una regla solo se borra si no tiene historial (append-only, ADR-0016).
/// Todo tenant-scoped por el filtro global.
/// </summary>
public interface IRuleDocumentService
{
    // ---- Documentos ----

    Task<IReadOnlyList<RuleDocumentDto>> ListDocumentsAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<RuleDocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleDocumentDto>> CreateDocumentAsync(SaveRuleDocumentRequest request, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleDocumentDto>> UpdateDocumentAsync(Guid documentId, SaveRuleDocumentRequest request, CancellationToken cancellationToken = default);
    Task<RuleResult<bool>> SetDocumentArchivedAsync(Guid documentId, bool archived, CancellationToken cancellationToken = default);

    // ---- Reglas ----

    Task<IReadOnlyList<RuleDto>> ListRulesAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleDto>> CreateRuleAsync(Guid documentId, SaveRuleRequest request, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleDto>> UpdateRuleAsync(Guid ruleId, SaveRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Borra la regla y sus vinculos. Invalid si tiene historial (append-only).</summary>
    Task<RuleResult<bool>> DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    // ---- Vinculos (pregunta de formulario / nodo de flujo) ----

    Task<IReadOnlyList<RuleFormLinkDto>> ListFormLinksAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Reglas vinculadas a UNA pregunta (tab Reglas del constructor, ADR-0021).</summary>
    Task<IReadOnlyList<QuestionRuleLinkDto>> ListQuestionLinksAsync(Guid formQuestionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuleNodeLinkDto>> ListNodeLinksAsync(Guid ruleId, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleFormLinkDto>> LinkToQuestionAsync(Guid ruleId, Guid formQuestionId, int sortOrder = 0, CancellationToken cancellationToken = default);
    Task<RuleResult<bool>> UnlinkQuestionAsync(Guid formFieldRuleId, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleNodeLinkDto>> LinkToNodeAsync(Guid ruleId, Guid workflowNodeId, int sortOrder = 0, bool isAutonomous = true, CancellationToken cancellationToken = default);
    Task<RuleResult<bool>> UnlinkNodeAsync(Guid workflowNodeRuleId, CancellationToken cancellationToken = default);

    // ---- Historial ----

    /// <summary>Ultimas ejecuciones del tenant, filtrables por documento y/o regla.</summary>
    Task<IReadOnlyList<RuleExecutionLogDto>> ListExecutionLogsAsync(
        Guid? documentId = null, Guid? ruleId = null, int take = 100, CancellationToken cancellationToken = default);

    // ---- Opciones para los combos de vinculacion de la UI ----

    Task<IReadOnlyList<RuleOption>> ListFormDefinitionOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleOption>> ListFormQuestionOptionsAsync(Guid definitionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuleOption>> ListWorkflowDefinitionOptionsAsync(CancellationToken cancellationToken = default);
    /// <summary>Solo nodos Task de la definicion (los unicos que ejecutan reglas).</summary>
    Task<IReadOnlyList<RuleOption>> ListWorkflowNodeOptionsAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default);
}
