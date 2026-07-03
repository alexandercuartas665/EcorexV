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
}
