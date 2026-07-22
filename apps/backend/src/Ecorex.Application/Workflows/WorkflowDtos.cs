using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

// ---- WorkflowEngine (FASE 4, ola 1) ----

/// <summary>Solicitud de importacion de un XML BPMN 2.0 estandar (se guarda tal cual).</summary>
public sealed record ImportBpmnRequest(
    string ProcessCode,
    string Name,
    string BpmnXml,
    string? Description = null);

public sealed record WorkflowNodeDto(
    Guid Id, string BpmnElementId, string? Name, WorkflowNodeType NodeType,
    int? StepNumber, bool AllowsAssignment, Guid? RestartNodeId);

public sealed record WorkflowEdgeDto(
    Guid Id, Guid SourceNodeId, Guid TargetNodeId, string? BpmnElementId,
    string? Name, string? ConditionExpression);

public sealed record WorkflowDefinitionDto(
    Guid Id, string ProcessCode, string Name, string? Description, int Version,
    bool IsPublished, bool IsArchived,
    IReadOnlyList<WorkflowNodeDto> Nodes, IReadOnlyList<WorkflowEdgeDto> Edges);

/// <summary>Paso del historial con los datos de su nodo (para bandejas y tests).</summary>
public sealed record WorkflowStepDto(
    Guid Id, Guid InstanceId, Guid NodeId, string BpmnElementId, string? NodeName,
    WorkflowNodeType NodeType, int CycleIndex, bool IsCurrent, bool IsCycleStart,
    WorkflowStepStatus Status, Guid? AssignedToTenantUserId, Guid? ExecutedByTenantUserId,
    string? ApprovalResult, string? ApprovalComment, DateTimeOffset? CompletedAt,
    // Agentes de IA en nodos (ola 2). Aditivos y al final para no romper llamadores posicionales.
    // El AUTOR maquina viaja junto al humano (nunca en su lugar) y la propuesta viaja aparte para
    // que una bandeja pueda mostrarla mientras el paso sigue esperando a una persona.
    Guid? ExecutedByAiAgentId = null,
    string? AgentProposalResult = null, string? AgentProposalComment = null,
    string? AgentFailureReason = null, DateTimeOffset? AgentAttemptedAt = null);

public sealed record WorkflowInstanceDto(
    Guid Id, Guid DefinitionId, Guid? TaskItemId, WorkflowInstanceStatus Status,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int CurrentCycle,
    IReadOnlyList<WorkflowStepDto> CurrentSteps);
