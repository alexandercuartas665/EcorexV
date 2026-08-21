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

    /// <summary>Si el paso admite reasignacion manual (PERMITE_ASIGNACION legacy). Ademas, en una
    /// compuerta exclusiva o un evento de fin, activa que el nodo sea un PUNTO DE ATENCION HUMANO
    /// (ver <see cref="WaitsForHuman"/>).</summary>
    public bool AllowsAssignment { get; set; }

    /// <summary>
    /// True si este nodo ESPERA a un humano antes de avanzar (ADR-0068, extiende ADR-0035/ADR-0037):
    /// - <see cref="WorkflowNodeType.Task"/>: siempre (salvo auto-cierre por regla, decidido en runtime).
    /// - <see cref="WorkflowNodeType.ExclusiveGateway"/> / <see cref="WorkflowNodeType.EndEvent"/>: SOLO si
    ///   el disenador activo la asignacion (<see cref="AllowsAssignment"/>). Entonces el usuario/cargo
    ///   asignado ELIGE la ruta (compuerta) o CONFIRMA el cierre (fin) en vez de auto-resolverse.
    /// - <see cref="WorkflowNodeType.StartEvent"/>: nunca (se completa solo; el asignado es el iniciador).
    /// Es propiedad calculada (no columna): no requiere migracion. Preserva el comportamiento historico
    /// (compuertas/fines sin asignacion siguen siendo automaticos, ADR-0037).
    /// </summary>
    public bool WaitsForHuman => NodeType switch
    {
        WorkflowNodeType.Task => true,
        WorkflowNodeType.ExclusiveGateway or WorkflowNodeType.EndEvent => AllowsAssignment,
        _ => false
    };

    // ---- Origen del asignado (ADR-0056): como resuelve el motor el encargado al activar el paso ----
    /// <summary>Modo de resolucion del asignado (Policy=cargo/dependencia por defecto; InheritStart;
    /// InheritPrevious; FormField). Metadato de nodo, no viaja en el XML.</summary>
    public WorkflowAssigneeSource AssigneeSource { get; set; } = WorkflowAssigneeSource.Policy;

    /// <summary>Solo para <see cref="WorkflowAssigneeSource.FormField"/>: codigo del campo (de un formulario
    /// de un nodo anterior) cuyo valor (id o correo de usuario) define el asignado.</summary>
    public string? AssigneeFormFieldCode { get; set; }

    /// <summary>
    /// Nodo destino del reinicio (ID_REINICIO legacy): si este nodo se alcanza durante el
    /// avance, en lugar de continuar se abre un ciclo nuevo (CycleIndex+1) en el nodo destino.
    /// Self-FK con NO ACTION (nunca cascada).
    /// </summary>
    public Guid? RestartNodeId { get; set; }
    public WorkflowNode? RestartNode { get; set; }

    // ---- Destino en TABLERO (enlace flujo <-> tableros) ----
    /// <summary>Tablero al que debe SALTAR la actividad cuando este nodo (paso) se vuelve el actual.
    /// Null = no mueve la actividad de tablero en este paso. Referencia suelta (sin FK dura; se valida
    /// en el motor). Solo aplica a nodos de tipo Task (los que esperan a un humano).</summary>
    public Guid? TargetBoardId { get; set; }

    /// <summary>Columna/estado del tablero (<see cref="TargetBoardId"/>) donde cae la actividad al
    /// activarse este paso. Null = primera columna del tablero destino.</summary>
    public Guid? TargetColumnId { get; set; }

    // ---- Salto a otro flujo (ADR-0056; visual por ahora, el vinculo runtime es deuda) ----
    /// <summary>Definicion de flujo a la que "salta" este nodo (handoff a otro proceso). Null = no salta.
    /// Referencia suelta (sin FK dura, como el destino de tablero; se valida en el servicio). Se muestra
    /// en el panel del nodo, NO se dibuja en el lienzo.</summary>
    public Guid? JumpToDefinitionId { get; set; }

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
