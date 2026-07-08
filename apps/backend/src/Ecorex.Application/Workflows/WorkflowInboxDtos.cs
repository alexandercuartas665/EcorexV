using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

// ---- Bandeja de pasos pendientes (runtime operativo de flujos, ola F2, ADR-0036) ----

/// <summary>
/// Un paso Pending y current de una instancia Running que el usuario puede atender (es el
/// asignado o un candidato de la policy del nodo). Reune datos de la tarea, el proceso, el
/// nodo y (si adelante hay una compuerta exclusiva) las opciones de decision de aprobacion.
/// </summary>
public sealed record PendingStepDto(
    Guid StepId,
    Guid InstanceId,
    Guid? TaskItemId,
    string? TaskNumber,
    string? TaskTitle,
    string ProcessName,
    string ProcessCode,
    string? NodeName,
    WorkflowNodeType NodeType,
    Guid? AssignedToTenantUserId,
    string AssignedToLabel,
    bool IsMine,
    bool IsClaimable,
    bool AllowsReassignment,
    bool HasForm,
    bool IsGatewayAhead,
    IReadOnlyList<string> ApprovalOptions,
    int CycleIndex,
    DateTimeOffset CreatedAt);
