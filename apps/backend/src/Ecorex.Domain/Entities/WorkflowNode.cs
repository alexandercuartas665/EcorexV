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

    /// <summary>
    /// Concepto de cierre (ADR-0123) que el flujo ESTAMPA en la tarea cuando este nodo la lleva a una
    /// columna de cierre (<see cref="TargetColumnId"/> con IsDone). Debe ser uno de los motivos de cierre
    /// del tablero destino (<c>TaskBoard.CloseReasonsJson</c>). Como el flujo es automatico, no se pregunta:
    /// el disenador lo elige por adelantado. Null = el nodo no cae en columna de cierre (o sin concepto).
    /// </summary>
    public string? CloseReason { get; set; }

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

    /// <summary>
    /// Desplazamiento X del post-it de la nota RELATIVO a la esquina superior izquierda del nodo (px de
    /// diagrama). Null = posicion por defecto (debajo del nodo). El usuario arrastra la nota en el lienzo y
    /// esta posicion se persiste. Metadato del editor, no viaja en el XML BPMN.
    /// </summary>
    public int? NoteOffsetX { get; set; }

    /// <summary>Desplazamiento Y del post-it relativo al nodo (px de diagrama). Null = por defecto. Ver <see cref="NoteOffsetX"/>.</summary>
    public int? NoteOffsetY { get; set; }

    /// <summary>
    /// Reglas de NOTIFICACION del nodo (jsonb): a quien y por que canal avisar cuando el paso LLEGA (se
    /// vuelve actual). Cada regla trae canal (correo/WhatsApp/grupo/Telegram), destinatario, plantilla de
    /// mensaje con tokens ({tarea.x} / {form.x}) y si adjunta el enlace a la tarea. Null/vacio = sin avisos.
    /// Metadato del nodo (no viaja en el XML BPMN), editable sobre una definicion publicada. Ver NodeNotifyConfig.
    /// </summary>
    public string? NotifyJson { get; set; }

    /// <summary>
    /// PLAZO (SLA) del paso (Fase 1 - plazos de flujo): dias + horas + minutos, con el modo de los dias
    /// (calendario o habil). JSON { "days","hours","minutes","dayMode":"calendar|business" } (ver StepSla).
    /// El reloj real de cada paso arranca cuando el paso anterior TERMINA de verdad; el plazo es un ESTIMADO
    /// para calcular el vencimiento del paso y la fecha final (que rueda) de la actividad. Null = sin plazo.
    /// </summary>
    public string? SlaJson { get; set; }

    /// <summary>
    /// TIEMPO PARA ARRANCAR el paso (Plazos v2 - ADR-0119): cuanto se espera DESDE que el paso se activa antes
    /// de que arranque. Mismo shape que <see cref="SlaJson"/> (JSON {days,hours,minutes,dayMode}, ver StepSla).
    /// Null/ausente = Inmediato (arranca al activarse = comportamiento de la Fase 1). Para un nodo con AGENTE
    /// es un retardo REAL (el agente se ejecuta en inicio = activacion + arranque); para humano es informativo.
    /// </summary>
    public string? StartDelayJson { get; set; }

    /// <summary>
    /// SEGUIMIENTO POR PLAZOS (cadencia de reintentos) de un nodo con AGENTE: una LISTA ordenada de esperas
    /// entre relanzamientos. JSON array de objetos {days,hours,minutes} (ver StepSla / StepSlaList). El 1er
    /// elemento es el primer contacto (tipicamente 0 = inmediato); los siguientes son la espera ANTES de cada
    /// recordatorio si el cliente no respondio. Al agotarse la lista el agente DEJA de insistir (no se cierra:
    /// el paso sigue vigente esperando respuesta o a una persona). Null/vacio = sin cadencia (comportamiento
    /// previo: el agente se auto-reprograma via 'programar_reintento', o no reintenta). Lo re-dispara el barrido
    /// por AgentNextRetryAt; el indice de la cadencia vive en WorkflowStepHistory.AgentFollowUpIndex.
    /// </summary>
    public string? AgentFollowUpJson { get; set; }

    /// <summary>
    /// Desplazamiento manual (dx, dy) del nodo en el DIAGRAMA DE LA TAREA (runtime), persistido y COMPARTIDO
    /// entre usuarios (ADR-0051 v2). Se suma al auto-layout por capas. Null = el nodo sigue el layout calculado.
    /// Es solo presentacion del runtime; no afecta el editor BPMN (X/Y) ni la ejecucion.
    /// </summary>
    public int? RuntimeLayoutDx { get; set; }

    public int? RuntimeLayoutDy { get; set; }
}
