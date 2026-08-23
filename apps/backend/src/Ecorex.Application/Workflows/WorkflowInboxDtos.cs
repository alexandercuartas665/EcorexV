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

// ---- Diagrama del flujo en el detalle de una tarea (ADR-0051): grafo (geometria de la
//      definicion) + estado por nodo (runtime) para pintar el proceso dentro de la tarjeta. ----

/// <summary>Estado de un nodo en el diagrama de una tarea de proceso.</summary>
public enum TaskFlowNodeState { Pending, Current, Completed, Rejected, Skipped }

/// <summary>Un nodo del diagrama con su geometria (X/Y/W/H de la definicion) y su estado runtime.</summary>
public sealed record TaskFlowNodeDto(
    Guid NodeId,
    string? Name,
    WorkflowNodeType NodeType,
    int X, int Y, int W, int H,
    TaskFlowNodeState State,
    bool IsCurrent,
    // Atendibilidad por el VIEWER (para el menu de cerrar/anotar): solo pasos current+mios/reclamables.
    bool IsMine,
    bool IsClaimable,
    Guid? StepId,
    bool HasForm,
    IReadOnlyList<string> ApprovalOptions,
    // Nodo atendido por un AGENTE (automatico): se pinta AUTOMATICO; su cierre es de otra ola.
    bool IsAuto,
    string? AgentName,
    string? ByLabel,
    string? Note,
    bool HasNote,
    // Etiquetas para pintar dentro de la tarjeta del nodo (ADR-0051): encargado, ultima actividad
    // reportada (cierre o inicio del paso) y desde cuando lleva en espera (si es el paso vigente).
    string? AssigneeLabel = null,
    DateTimeOffset? LastActivityAt = null,
    DateTimeOffset? WaitingSince = null,
    // Rutas de la compuerta que sigue a este paso (aprobado/rechazado con su destino), para el menu.
    IReadOnlyList<TaskFlowRouteDto>? Routes = null,
    // Cargo(s) del organigrama que atienden el nodo (WorkflowNodePolicy -> OrgUnit), para el footer.
    string? CargoLabel = null,
    // Apariencia CONFIGURADA del nodo en el editor de flujos (metadatos): color de paleta
    // (violet/blue/green/amber/rose/slate) y nota post-it. Para pintar el nodo con su color y mostrar
    // la nota dejada en la configuracion dentro de la tarjeta del proceso (ADR-0022).
    string? Color = null,
    string? ConfigNote = null);

/// <summary>Clasificacion de una rama de compuerta para colorear/rotular: aprobado (verde),
/// rechazado (rojo) o neutral (una salida sin semantica de aprobacion).</summary>
public enum TaskFlowRouteKind { Neutral, Approve, Reject }

/// <summary>Arista del diagrama (SourceNodeId -> TargetNodeId), con etiqueta si es condicional y su
/// clasificacion (para pintar en verde/rojo las ramas de la compuerta).</summary>
public sealed record TaskFlowEdgeDto(
    Guid SourceNodeId, Guid TargetNodeId, string? Label, bool IsCondition,
    TaskFlowRouteKind Kind = TaskFlowRouteKind.Neutral);

/// <summary>Una ruta que el usuario puede tomar desde un paso con compuerta adelante: la opcion
/// (approvalResult que se manda al motor), el nombre del PASO DESTINO y su clasificacion.</summary>
public sealed record TaskFlowRouteDto(string Option, string? TargetName, TaskFlowRouteKind Kind);

/// <summary>
/// Diagrama del flujo de una tarea de proceso: nombre, bounds para el viewBox y los nodos/aristas
/// con su estado. Une la geometria de la definicion (GetCanvasAsync) con el estado del runtime
/// (WorkflowStepHistory) y la marca de agente por nodo. Null si la tarea no viene de un flujo.
/// </summary>
public sealed record TaskFlowDiagramDto(
    string FlowName,
    int MinX, int MinY, int Width, int Height,
    IReadOnlyList<TaskFlowNodeDto> Nodes,
    IReadOnlyList<TaskFlowEdgeDto> Edges);
