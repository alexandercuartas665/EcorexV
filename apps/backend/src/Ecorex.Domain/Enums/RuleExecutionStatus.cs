namespace Ecorex.Domain.Enums;

/// <summary>Resultado de una ejecucion de regla registrada en RuleExecutionLog.</summary>
public enum RuleExecutionStatus
{
    Success = 0,
    Failed = 1,
    /// <summary>La regla no se ejecuto (estado no elegible para el disparador).</summary>
    Skipped = 2
}
