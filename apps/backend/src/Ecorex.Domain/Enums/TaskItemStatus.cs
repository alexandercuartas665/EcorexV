namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de una tarea del nucleo TaskItem. Las transiciones validas las gobierna
/// TaskItemStateMachine (Ecorex.Domain.Rules); Closed es terminal e inmutable.
/// En FASE 4, cuando el ActivityType tenga WorkflowDefinition, el estado dejara de
/// ser libre y lo dictara el flujo.
/// </summary>
public enum TaskItemStatus
{
    Pending = 0,
    Active,
    InProgress,
    Done,
    Suspended,
    Closed
}
