using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Servicio de DISENO de flujos para el editor canvas propio del prototipo (ADR-0022).
/// Complementa a IWorkflowEngine (que importa/publica/ejecuta): aqui viven el indice con
/// metricas reales y las mutaciones del grafo nodo a nodo. REGLAS CLAVE:
/// - El grafo solo se edita en definiciones NO publicadas (borradores). Para editar una
///   publicada, EnsureDraftAsync crea (o reutiliza) la version borrador siguiente por el
///   camino de versionado del motor (ImportBpmnAsync = max+1 no publicada) y copia lo que
///   no viaja en el XML (Category, RestartNodeId, AllowsAssignment, vinculos de
///   formularios y reglas por BpmnElementId).
/// - Cada mutacion del grafo REGENERA el BpmnXml completo (process + bpmndi con las
///   coordenadas del canvas, via BpmnXmlWriter) para conservar la portabilidad bpmn.io
///   del ADR-0014.
/// - Los vinculos (formulario/reglas por nodo) NO forman parte del XML y se permiten
///   tambien sobre definiciones publicadas (igual que hace el modulo de reglas).
/// </summary>
public interface IWorkflowDesignService
{
    // ---- Indice ----

    /// <summary>KPIs y tarjetas del indice /flujos (metricas reales, ver FlowCardDto).</summary>
    Task<FlowIndexDto> ListForIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>Canvas completo (nodos con coordenadas + aristas + vinculos) o null.</summary>
    Task<FlowCanvasDto?> GetCanvasAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>"Nuevo flujo": crea un borrador minimo startEvent -> endEvent.</summary>
    Task<WorkflowResult<FlowCanvasDto>> CreateDraftAsync(string name, string? category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la definicion tal cual si es borrador; si esta publicada, devuelve (o
    /// crea, reusando el versionado del motor) la version borrador siguiente.
    /// </summary>
    Task<WorkflowResult<FlowCanvasDto>> EnsureDraftAsync(Guid definitionId, CancellationToken cancellationToken = default);

    // ---- Mutaciones del grafo (solo borradores; regeneran el BpmnXml) ----

    /// <summary>Agrega un nodo en (x, y). Genera BpmnElementId unico. Un solo startEvent por definicion.</summary>
    Task<WorkflowResult<FlowCanvasNodeDto>> AddNodeAsync(Guid definitionId, WorkflowNodeType nodeType, int x, int y, CancellationToken cancellationToken = default);

    Task<WorkflowResult<bool>> MoveNodeAsync(Guid nodeId, int x, int y, CancellationToken cancellationToken = default);

    Task<WorkflowResult<bool>> RenameNodeAsync(Guid nodeId, string? name, CancellationToken cancellationToken = default);

    /// <summary>Conecta dos nodos de la misma definicion (rechaza duplicados y self-loops).</summary>
    Task<WorkflowResult<FlowCanvasEdgeDto>> ConnectAsync(Guid sourceNodeId, Guid targetNodeId, CancellationToken cancellationToken = default);

    /// <summary>Borra el nodo con sus aristas y vinculos. NUNCA el startEvent (es unico).</summary>
    Task<WorkflowResult<bool>> DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken = default);

    Task<WorkflowResult<bool>> DeleteEdgeAsync(Guid edgeId, CancellationToken cancellationToken = default);

    /// <summary>Condicion de la arista (formato del motor: "approval == 'Approved'"; vacio = rama default).</summary>
    Task<WorkflowResult<bool>> SetEdgeConditionAsync(Guid edgeId, string? conditionExpression, CancellationToken cancellationToken = default);

    /// <summary>Configuracion basica del nodo: AllowsAssignment + reinicio (reusa SetRestartTargetAsync del motor).</summary>
    Task<WorkflowResult<bool>> SetNodeConfigAsync(Guid nodeId, bool allowsAssignment, Guid? restartNodeId, CancellationToken cancellationToken = default);

    // ---- Propiedades y ciclo de vida de la definicion ----

    Task<WorkflowResult<bool>> UpdateDefinitionPropsAsync(Guid definitionId, string name, string? category, string? description, CancellationToken cancellationToken = default);

    /// <summary>Pausa una publicada: StartInstanceAsync del motor rechaza instancias nuevas.</summary>
    Task<WorkflowResult<bool>> PauseAsync(Guid definitionId, CancellationToken cancellationToken = default);

    Task<WorkflowResult<bool>> ResumeAsync(Guid definitionId, CancellationToken cancellationToken = default);

    // ---- Exportar / importar JSON (formato del prototipo) ----

    /// <summary>JSON del prototipo: props + nodos (con layout) + conexiones. Null si no existe.</summary>
    Task<string?> ExportJsonAsync(Guid definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Importa el JSON del prototipo: genera el XML BPMN (writer) y crea una definicion
    /// nueva en Borrador (version max+1 si el codigo ya existe, via motor).
    /// </summary>
    Task<WorkflowResult<FlowCanvasDto>> ImportJsonAsync(string json, CancellationToken cancellationToken = default);

    // ---- Vinculos por nodo (formulario y reglas; permitidos tambien en publicadas) ----

    /// <summary>Catalogo de reglas del tenant (documentos no archivados) para el acordeon Reglas.</summary>
    Task<IReadOnlyList<FlowRuleCatalogItemDto>> ListRuleCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Vincula un formulario ACTIVO al nodo (WorkflowNodeForm: a lo sumo uno por nodo).</summary>
    Task<WorkflowResult<bool>> SetNodeFormAsync(Guid nodeId, Guid formDefinitionId, CancellationToken cancellationToken = default);

    Task<WorkflowResult<bool>> RemoveNodeFormAsync(Guid nodeId, CancellationToken cancellationToken = default);

    Task<WorkflowResult<FlowNodeRuleDto>> AddNodeRuleAsync(Guid nodeId, Guid ruleId, CancellationToken cancellationToken = default);

    Task<WorkflowResult<bool>> RemoveNodeRuleAsync(Guid linkId, CancellationToken cancellationToken = default);

    Task<WorkflowResult<bool>> SetNodeRuleAutonomousAsync(Guid linkId, bool isAutonomous, CancellationToken cancellationToken = default);
}
