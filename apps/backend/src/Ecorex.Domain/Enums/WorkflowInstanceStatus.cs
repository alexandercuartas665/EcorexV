namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de una instancia (caso) de flujo. Stuck es el estado defensivo heredado del
/// motor legacy: el avance en cascada supero el tope de 50 iteraciones (flujo mal modelado)
/// y la instancia queda marcada para diagnostico (KPI workflow_stuck_rate).
/// </summary>
public enum WorkflowInstanceStatus
{
    Running = 0,
    Completed,
    Cancelled,
    Stuck
}
