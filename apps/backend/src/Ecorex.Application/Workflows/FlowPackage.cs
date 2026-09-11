namespace Ecorex.Application.Workflows;

/// <summary>
/// Paquete PORTABLE de un flujo (ADR-0097 Ola B1): captura el grafo + la config de cada nodo SIN ids
/// de tenant, para publicarlo al marketplace y "traerlo" a otro tenant. Formularios embebidos (export
/// JSON del formulario); cargos y agentes referenciados POR NOMBRE (se remapean en el tenant destino,
/// lo que no coincide queda sin asignar). Serializable a JSON (enums como texto).
/// </summary>
public sealed record FlowPackage(
    int FormatVersion,
    string Name,
    string? Description,
    string? Category,
    IReadOnlyList<FlowPackageNode> Nodes,
    IReadOnlyList<FlowPackageEdge> Edges);

/// <summary>Un nodo del paquete. <see cref="Tipo"/> es el tipo compatible con el import del grafo
/// ("startEvent" / "task" / "exclusiveGateway" / "endEvent").</summary>
public sealed record FlowPackageNode(
    string BpmnElementId,
    string Tipo,
    string? Label,
    int X,
    int Y,
    int? W,
    int? H,
    string AssigneeSource,
    string? AssigneeFormFieldCode,
    string? Color,
    string? Note,
    IReadOnlyList<FlowPackageForm> Forms,
    IReadOnlyList<string> CargoNames,
    FlowPackageAgent? Agent,
    IReadOnlyList<FlowPackageRule> Rules);

/// <summary>Formulario vinculado a un nodo: el export JSON portable del formulario + sus flags de nodo.</summary>
public sealed record FlowPackageForm(
    string ExportJson,
    int SortOrder,
    bool IsRequired,
    bool AutoCreateOnArrival);

/// <summary>Agente del nodo referenciado por NOMBRE (no viajan ids de tenant ni colmena/whatsapp/lineas).</summary>
public sealed record FlowPackageAgent(
    string AgentName,
    string Autonomy,
    string? ExtraPrompt,
    bool CanSendEmail,
    string OnFailure,
    int FailureRetries,
    string? FailureRoute,
    string? VoiceAgentName);

/// <summary>Regla del nodo referenciada por NOMBRE.</summary>
public sealed record FlowPackageRule(
    string RuleName,
    bool IsAutonomous,
    int SortOrder);

public sealed record FlowPackageEdge(
    string From,
    string To,
    string? Name,
    string? Condition);

/// <summary>Opciones del asistente al "Traer" un flujo (ADR-0097 B3). Por ahora: si migrar o no los
/// formularios vinculados a los nodos.</summary>
public sealed record FlowImportOptions(bool IncludeNodeForms = true);

/// <summary>Reporte de la importacion de un paquete: que se creo y que quedo SIN asignar (para que el
/// tenant cablee lo faltante antes de publicar el flujo importado).</summary>
public sealed record FlowImportReport(
    Guid NewDefinitionId,
    string NewProcessCode,
    int FormsImported,
    IReadOnlyList<string> MappedCargos,
    IReadOnlyList<string> UnmappedCargos,
    IReadOnlyList<string> MappedAgents,
    IReadOnlyList<string> UnmappedAgents,
    IReadOnlyList<string> MappedRules,
    IReadOnlyList<string> UnmappedRules,
    IReadOnlyList<string> Warnings);
