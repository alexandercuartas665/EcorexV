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

    /// <summary>Lista PLANA de todas las reglas del tenant con su documento como categoria (ADR-0023).</summary>
    Task<IReadOnlyList<RuleListItemDto>> ListAllRulesAsync(bool includeArchivedDocuments = false, CancellationToken cancellationToken = default);

    Task<RuleDto?> GetRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleDto>> CreateRuleAsync(Guid documentId, SaveRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Actualiza la regla; si request.DocumentId viene y es distinto, la mueve de documento.</summary>
    Task<RuleResult<RuleDto>> UpdateRuleAsync(Guid ruleId, SaveRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Clona la regla (nombre + " (copia)", ultimo SortOrder) en el MISMO documento, sin vinculos.</summary>
    Task<RuleResult<RuleDto>> DuplicateRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Borra la regla y sus vinculos. Invalid si tiene historial (append-only).</summary>
    Task<RuleResult<bool>> DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken = default);

    // ---- Metricas (KPIs + panel Propiedades, ADR-0023) ----

    /// <summary>KPIs del tenant: documentos, reglas y ejecuciones/exito/promedio de los ultimos 30 dias.</summary>
    Task<RuleTenantStatsDto> GetTenantStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>Metricas de UNA regla en la ventana de 30 dias.</summary>
    Task<RuleMetricsDto> GetRuleMetricsAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Creada/modificada con nombres legibles (status strip + Propiedades).</summary>
    Task<RuleAuditDto?> GetRuleAuditAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>TenantUser del usuario autenticado (para registrar quien ejecuta la prueba manual).</summary>
    Task<Guid?> GetCurrentTenantUserIdAsync(CancellationToken cancellationToken = default);

    // ---- Vinculos (pregunta de formulario / nodo de flujo) ----

    Task<IReadOnlyList<RuleFormLinkDto>> ListFormLinksAsync(Guid ruleId, CancellationToken cancellationToken = default);

    /// <summary>Reglas vinculadas a UNA pregunta (tab Reglas del constructor, ADR-0021).</summary>
    Task<IReadOnlyList<QuestionRuleLinkDto>> ListQuestionLinksAsync(Guid formQuestionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuleNodeLinkDto>> ListNodeLinksAsync(Guid ruleId, CancellationToken cancellationToken = default);
    Task<RuleResult<RuleFormLinkDto>> LinkToQuestionAsync(Guid ruleId, Guid formQuestionId, int sortOrder = 0, CancellationToken cancellationToken = default);
    Task<RuleResult<bool>> UnlinkQuestionAsync(Guid formFieldRuleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una regla condicional de campo "si {sourceField} {op} {value} -&gt; {effect} {targetField}"
    /// (verbo BLOQUEAR_CAMPO_XCONDICION) y la vincula a la pregunta disparadora, en un solo paso.
    /// La regla se guarda en un documento propio del formulario (se crea si no existe), para que el
    /// disenador no tenga que salir a crear reglas a mano. El runtime (FormRuleDispatcher) ya la aplica.
    /// </summary>
    Task<RuleResult<QuestionRuleLinkDto>> CreateFieldConditionRuleAsync(
        CreateFieldConditionRequest request, CancellationToken cancellationToken = default);
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
