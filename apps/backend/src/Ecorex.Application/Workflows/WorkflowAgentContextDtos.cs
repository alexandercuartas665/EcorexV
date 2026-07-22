using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Contexto que recibira un agente de IA para atender un paso de flujo (ola 1: se ARMA, no se
/// ejecuta). Contrato ESTRUCTURADO a proposito: serializar a texto para el prompt es trivial y
/// reversible, pero al reves se pierde la forma. Cuatro partes, en el orden en que un humano
/// las necesitaria: (1) el paso y su formulario, (2) lo ya capturado antes, (3) la tarea y su
/// tercero, (4) por donde paso el caso.
///
/// TODAS las colecciones vienen ACOTADAS (ver WorkflowAgentContextLimits): los tokens se pagan
/// y el tenant tiene cupo por plan. Los flags Truncated permiten al serializador avisar al
/// modelo que esta viendo una ventana y no el universo.
/// </summary>
public sealed record WorkflowAgentContextDto(
    Guid InstanceId,
    Guid StepId,
    WorkflowAgentNodeDto Node,
    WorkflowAgentPriorDataDto PriorData,
    WorkflowAgentTaskDto? Task,
    WorkflowAgentHistoryDto History,
    // Agente asignado al nodo y su autonomia. Null si el nodo no tiene agente.
    WorkflowAgentAssignmentDto? Assignment);

/// <summary>Agente asignado al nodo del paso y con que autonomia debe actuar.</summary>
public sealed record WorkflowAgentAssignmentDto(
    Guid AiAgentId, string AgentName, string? AgentRole, bool IsActive, WorkflowAgentAutonomy Autonomy);

/// <summary>(a) El nodo actual y el formulario que el paso debe llenar, con sus campos.</summary>
public sealed record WorkflowAgentNodeDto(
    Guid NodeId,
    string BpmnElementId,
    string? Name,
    // Descripcion del nodo: se usa la nota del lienzo (Note), el unico texto libre del nodo.
    string? Description,
    WorkflowNodeType NodeType,
    int? StepNumber,
    WorkflowAgentFormDto? Form);

/// <summary>Formulario asociado al nodo (WorkflowNodeForm) y la definicion de sus campos.</summary>
public sealed record WorkflowAgentFormDto(
    Guid DefinitionId,
    string Code,
    string Title,
    string? Description,
    IReadOnlyList<WorkflowAgentFieldDto> Fields,
    // True si el formulario tiene mas campos de los incluidos (tope por formulario).
    bool FieldsTruncated);

/// <summary>Campo del formulario tal como el agente debe entenderlo para poder llenarlo.</summary>
public sealed record WorkflowAgentFieldDto(
    string FieldCode,
    string Label,
    FormControlType ControlType,
    bool Required,
    string? HelpText,
    // Opciones crudas (JSON) para Select/Radio/MultiCheck; null si el control no las usa.
    string? OptionsJson);

/// <summary>(b) Datos ya capturados en pasos ANTERIORES de la misma instancia.</summary>
public sealed record WorkflowAgentPriorDataDto(
    IReadOnlyList<WorkflowAgentPriorFormDto> Forms,
    // True si hubo mas envios previos de los incluidos (tope de formularios previos).
    bool Truncated);

/// <summary>Un formulario ya enviado en un paso anterior, con sus valores.</summary>
public sealed record WorkflowAgentPriorFormDto(
    Guid ResponseId,
    Guid NodeId,
    string? NodeName,
    string FormCode,
    string FormTitle,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<WorkflowAgentAnswerDto> Answers,
    // True si la respuesta tenia mas campos de los incluidos.
    bool AnswersTruncated);

/// <summary>Valor capturado. Label viene de la definicion cuando se pudo resolver.</summary>
public sealed record WorkflowAgentAnswerDto(string FieldCode, string? Label, string? Value);

/// <summary>(c) La tarea que disparo el flujo y el tercero/cliente asociado si lo hay.</summary>
public sealed record WorkflowAgentTaskDto(
    Guid TaskItemId,
    string Number,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTimeOffset? DueDate,
    string? RequesterName,
    string? RequesterEmail,
    WorkflowAgentTerceroDto? Tercero);

/// <summary>Tercero/cliente del caso (Directorio General 000232).</summary>
public sealed record WorkflowAgentTerceroDto(
    Guid TerceroId, string Nombre, TerceroTipo Tipo, string? IdValor, string? Email, string? Telefono, string? Ciudad);

/// <summary>(d) Historial de pasos: por donde paso el caso, con aprobaciones y comentarios.</summary>
public sealed record WorkflowAgentHistoryDto(
    IReadOnlyList<WorkflowAgentHistoryStepDto> Steps,
    // Total real de pasos de la instancia (para que el modelo sepa cuanto no ve).
    int TotalSteps,
    // True si TotalSteps supera el tope y Steps es solo la ventana mas reciente.
    bool Truncated);

public sealed record WorkflowAgentHistoryStepDto(
    Guid StepId,
    Guid NodeId,
    string? NodeName,
    int CycleIndex,
    WorkflowStepStatus Status,
    bool IsCurrent,
    string? ApprovalResult,
    string? ApprovalComment,
    string? ExecutedByEmail,
    DateTimeOffset? CompletedAt);
