using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Nodo BPMN materializado de una definicion de flujo (port de DOC_PROCESOS_R). Se crea al
/// importar el XML y es de solo lectura para el motor, salvo RestartNodeId que se configura
/// aparte (los reinicios/loops no forman parte del XML BPMN estandar). Unico por
/// (DefinitionId, BpmnElementId). TENANT-SCOPED.
/// </summary>
public class WorkflowNode : TenantEntity
{
    public Guid DefinitionId { get; set; }
    public WorkflowDefinition? Definition { get; set; }

    /// <summary>Id del elemento en el XML BPMN (ej. "Activity_1wx9i90").</summary>
    public string BpmnElementId { get; set; } = null!;

    public string? Name { get; set; }

    public WorkflowNodeType NodeType { get; set; }

    /// <summary>Numero de paso informativo (PASO legacy): orden de aparicion en el XML.</summary>
    public int? StepNumber { get; set; }

    /// <summary>Si el paso admite reasignacion manual (PERMITE_ASIGNACION legacy).</summary>
    public bool AllowsAssignment { get; set; }

    /// <summary>
    /// Nodo destino del reinicio (ID_REINICIO legacy): si este nodo se alcanza durante el
    /// avance, en lugar de continuar se abre un ciclo nuevo (CycleIndex+1) en el nodo destino.
    /// Self-FK con NO ACTION (nunca cascada).
    /// </summary>
    public Guid? RestartNodeId { get; set; }
    public WorkflowNode? RestartNode { get; set; }

    // ---- Layout del canvas (editor propio del prototipo, ADR-0022) ----
    // Coordenadas del diagrama (bpmndi:BPMNShape/dc:Bounds). Se llenan al importar el XML
    // (con auto-layout si el XML no trae DI) y las mueve el editor; al guardar, el XML
    // BPMN se REGENERA con estas coordenadas para conservar la portabilidad bpmn.io
    // del ADR-0014.

    /// <summary>Posicion X del nodo en el canvas (px, esquina superior izquierda).</summary>
    public int X { get; set; }

    /// <summary>Posicion Y del nodo en el canvas (px, esquina superior izquierda).</summary>
    public int Y { get; set; }

    /// <summary>Ancho en px (null = ancho por defecto segun el tipo de nodo).</summary>
    public int? W { get; set; }

    /// <summary>Alto en px (null = alto por defecto segun el tipo de nodo).</summary>
    public int? H { get; set; }

    // ---- Apariencia del nodo en el graficador (restaurado del canvas propio previo a bpmn-js) ----

    /// <summary>
    /// Clave de color de la paleta del editor (violet/blue/green/amber/rose/slate). Null = sin color.
    /// NO viaja en el XML BPMN (el bundle de bpmn-js no soporta color): es metadato del nodo y el editor
    /// lo repinta sobre el SVG tras cada import. Fuente de verdad = esta columna.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>Nota libre del nodo, visible como post-it en el lienzo (overlay). Metadato, no viaja en el XML.</summary>
    public string? Note { get; set; }
}
