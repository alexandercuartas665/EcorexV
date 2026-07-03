namespace Ecorex.Domain.Enums;

/// <summary>
/// Tipo de nodo BPMN soportado por el WorkflowEngine (FASE 4, ola 1). Subconjunto del
/// estandar BPMN 2.0 que cubre el motor legacy AdmWorkflow: evento de inicio, tarea humana,
/// compuerta exclusiva (decision con aprobacion) y evento de fin.
/// </summary>
public enum WorkflowNodeType
{
    StartEvent = 0,
    Task,
    ExclusiveGateway,
    EndEvent
}
