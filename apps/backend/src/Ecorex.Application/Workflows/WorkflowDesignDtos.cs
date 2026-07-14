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

/// <summary>Regla vinculada a un nodo (fila del acordeon Reglas).</summary>
public sealed record FlowNodeRuleDto(
    Guid LinkId, Guid RuleId, string RuleName, string VerbName, RuleStatus Status, bool IsAutonomous);

/// <summary>Nodo del canvas con layout y vinculos (formulario y reglas).</summary>
public sealed record FlowCanvasNodeDto(
    Guid Id, string BpmnElementId, string? Name, WorkflowNodeType NodeType,
    int X, int Y, int W, int H, bool AllowsAssignment, Guid? RestartNodeId,
    Guid? FormDefinitionId, string? FormCode, string? FormTitle,
    IReadOnlyList<FlowNodeRuleDto> Rules,
    // Apariencia del nodo en el graficador (color de paleta + nota post-it). Metadatos, no viajan en el XML.
    string? Color = null, string? Note = null);

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
