namespace Ecorex.Domain.Enums;

/// <summary>Estado del ciclo de vida de un proyecto del tenant.</summary>
public enum ProjectStatus
{
    Planning = 0,
    Active,
    InExecution,
    Closed,
    Cancelled
}
