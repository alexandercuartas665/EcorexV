namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de un documento de reglas o de una regla individual (RulesEngine, FASE 4 ola 3).
/// Development permite probar la regla manualmente sin que los disparadores automaticos
/// (campo de formulario / nodo de flujo) la ejecuten.
/// </summary>
public enum RuleStatus
{
    Active = 0,
    Development = 1,
    Inactive = 2
}
