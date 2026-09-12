using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

// ---- DTOs del editor de flujos del prototipo (pantalla 'flujos', ADR-0022) ----

/// <summary>KPIs del indice (fila de 4 tarjetas del prototipo).</summary>
public sealed record FlowIndexKpisDto(
    int Flows, int RunningFlows, int ActiveInstances, int MonthExecutions);

/// <summary>
/// Tarjeta del indice: UNA por ProcessCode (la version publicada o, si no hay, la mas
/// reciente). Metricas REALES agregadas sobre TODAS las versiones del proceso:
/// RunningInstances = instancias Running; MonthExecutions = instancias iniciadas en el
/// mes calendario UTC en curso; SuccessRate = Completed / (Completed + Stuck + Cancelled)
/// en % redondeado (las Running no cuentan; 0 si no hay instancias terminadas).
/// </summary>
public sealed record FlowCardDto(
    Guid DefinitionId, string ProcessCode, int Version, string Name, string? Category,
    string Estado, int NodeCount, int RunningInstances, int MonthExecutions, int SuccessRate);

public sealed record FlowIndexDto(FlowIndexKpisDto Kpis, IReadOnlyList<FlowCardDto> Cards);

/// <summary>Instancia EN MARCHA (Running) de un flujo que impide eliminarlo. Se lista en el modal de
/// borrado para poder cancelarla una por una (junto con la tarea que la ejecuta, si la hay).</summary>
public sealed record FlowRunningInstanceDto(
    Guid InstanceId, int FlowVersion, Guid? TaskId, string? TaskNumber, string? TaskTitle, DateTimeOffset StartedAt);

/// <summary>Regla vinculada a un nodo (fila del acordeon Reglas).</summary>
public sealed record FlowNodeRuleDto(
    Guid LinkId, Guid RuleId, string RuleName, string VerbName, RuleStatus Status, bool IsAutonomous);

/// <summary>Formulario vinculado a un nodo (fila del acordeon Recursos). Un nodo admite VARIOS.
/// IsRequired (ADR-0077): si es true, hay que ENVIARLO para poder cerrar/decidir el paso.</summary>
public sealed record FlowNodeFormDto(Guid DefinitionId, string Code, string Title, bool IsRequired = false, bool AutoCreateOnArrival = true);

/// <summary>Nodo del canvas con layout y vinculos (formularios y reglas).</summary>
public sealed record FlowCanvasNodeDto(
    Guid Id, string BpmnElementId, string? Name, WorkflowNodeType NodeType,
    int X, int Y, int W, int H, bool AllowsAssignment, Guid? RestartNodeId,
    // Compat: primer formulario del nodo (o null). La lista completa va en <see cref="Forms"/>.
    Guid? FormDefinitionId, string? FormCode, string? FormTitle,
    IReadOnlyList<FlowNodeRuleDto> Rules,
    // Apariencia del nodo en el graficador (color de paleta + nota post-it). Metadatos, no viajan en el XML.
    string? Color = null, string? Note = null,
    // Posicion del post-it de la nota RELATIVA al nodo (px de diagrama). Null = por defecto (debajo del nodo).
    int? NoteOffsetX = null, int? NoteOffsetY = null,
    // Destino en tablero: al activarse este paso, la actividad salta a este tablero/columna (enlace flujo<->tableros).
    Guid? TargetBoardId = null, Guid? TargetColumnId = null,
    // TODOS los formularios del nodo (1:N), en orden. Vacio si ninguno.
    IReadOnlyList<FlowNodeFormDto>? Forms = null,
    // Salto a otro flujo (handoff): definicion destino + su nombre (para mostrar en el panel del nodo).
    Guid? JumpToDefinitionId = null, string? JumpToName = null,
    // Origen del asignado (ADR-0056): modo + (solo FormField) codigo del campo de formulario.
    WorkflowAssigneeSource AssigneeSource = WorkflowAssigneeSource.Policy, string? AssigneeFormFieldCode = null);

public sealed record FlowCanvasEdgeDto(
    Guid Id, Guid SourceNodeId, Guid TargetNodeId, string? BpmnElementId,
    string? Name, string? ConditionExpression);

/// <summary>
/// Canvas completo de una definicion. IsEditable = !IsPublished (el grafo solo se edita
/// en borradores; editar una publicada pasa por EnsureDraftAsync, que reusa el
/// versionado del motor).
/// </summary>
public sealed record FlowCanvasDto(
    Guid DefinitionId, string ProcessCode, int Version, string Name, string? Category,
    string? Description, bool IsPublished, bool IsPaused, bool IsArchived,
    string Estado, bool IsEditable,
    IReadOnlyList<FlowCanvasNodeDto> Nodes, IReadOnlyList<FlowCanvasEdgeDto> Edges);

/// <summary>Regla del catalogo del tenant para el acordeon Reglas del editor.</summary>
public sealed record FlowRuleCatalogItemDto(
    Guid RuleId, string Name, string VerbName, RuleStatus Status, string DocumentName);

/// <summary>
/// Agente de IA asignado a un nodo (ola 1). IsActive es del AiAgent: un agente apagado sigue
/// asignado pero no atendera cuando llegue la ola 2, y el editor debe poder advertirlo.
/// </summary>
public sealed record FlowNodeAgentDto(
    Guid LinkId, Guid AiAgentId, string AgentName, string? AgentRole,
    bool IsActive, WorkflowAgentAutonomy Autonomy,
    // ADR-0091: recursos para conseguir datos al llenar el formulario (opcionales, permiso por nodo).
    Guid? ColmenaClientId = null, string? ColmenaSessionKey = null, Guid? VoiceAiAgentId = null,
    // ADR-0092: linea WhatsApp + plantilla para 'preguntar_whatsapp' (opcionales, permiso por nodo).
    Guid? WhatsAppLineId = null, string? WhatsAppTemplateName = null, string? WhatsAppTemplateLang = null,
    // ADR-0093: instrucciones por paso (prompt extra) + permiso de correo.
    string? ExtraPrompt = null, bool CanSendEmail = false,
    // Politica de FALLO: que hacer si el agente no resuelve el paso.
    WorkflowAgentFailureAction OnFailure = WorkflowAgentFailureAction.ReturnToHuman,
    int FailureRetries = 0, string? FailureRoute = null);

/// <summary>ADR-0093: estado COMPLETO de los recursos del agente del nodo, para guardarlo de una en el modal
/// (evita una firma con muchos parametros). Cada campo null/false = recurso deshabilitado.</summary>
public sealed record FlowNodeAgentResourcesInput(
    Guid? ColmenaClientId, string? ColmenaSessionKey, Guid? VoiceAiAgentId,
    Guid? WhatsAppLineId, string? WhatsAppTemplateName, string? WhatsAppTemplateLang,
    string? ExtraPrompt, bool CanSendEmail,
    WorkflowAgentFailureAction OnFailure = WorkflowAgentFailureAction.ReturnToHuman,
    int FailureRetries = 0, string? FailureRoute = null);

/// <summary>Agente del catalogo del tenant para el selector de agente del editor.</summary>
public sealed record FlowAgentCatalogItemDto(
    Guid AiAgentId, string Name, string? Role, bool IsActive);

/// <summary>Cliente COLMENA del tenant para el selector de "buscar_web" del editor (ADR-0091).</summary>
public sealed record FlowColmenaClientDto(Guid Id, string Name, bool IsActive);

/// <summary>Linea WhatsApp del tenant para el selector de "preguntar_whatsapp" del editor (ADR-0092).</summary>
public sealed record FlowWhatsAppLineDto(Guid Id, string Name, bool Connected);
