namespace Ecorex.Domain.Enums;

/// <summary>Origen de una ejecucion de regla (RuleExecutionLog).</summary>
public enum RuleTriggerKind
{
    /// <summary>Ejecucion manual (boton "Ejecutar prueba" del modulo de reglas).</summary>
    Manual = 0,
    /// <summary>Disparada por el cambio de un campo de formulario (FormFieldRule).</summary>
    FormField = 1,
    /// <summary>Disparada al activarse un nodo de flujo (WorkflowNodeRule autonoma).</summary>
    WorkflowNode = 2
}
