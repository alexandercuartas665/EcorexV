namespace Ecorex.Domain.Enums;

/// <summary>
/// Que hacer con un paso de flujo cuando su AGENTE de IA no logra resolverlo (no pudo llenar el
/// formulario, el proveedor fallo, la herramienta -p.ej. buscar_web- no devolvio nada). Es
/// CONFIGURABLE POR NODO (vive en WorkflowNodeAgent). Por defecto el paso vuelve a una persona.
/// </summary>
public enum WorkflowAgentFailureAction
{
    /// <summary>Deja el paso vigente para que lo atienda una persona (el asignado o su cargo). Es el
    /// comportamiento por defecto y el mas seguro: nunca se pierde el paso.</summary>
    ReturnToHuman = 0,

    /// <summary>Reintenta al agente hasta FailureRetries veces (una por ciclo del worker) antes de
    /// rendirse; agotados los reintentos, vuelve a una persona.</summary>
    Retry = 1,

    /// <summary>Cierra el paso tomando una RUTA de salida configurada (FailureRoute): el motor enruta
    /// por esa rama (p.ej. "escalar" / "no encontrado"). Util cuando hay una compuerta con una via de
    /// contingencia.</summary>
    TakeRoute = 2,

    /// <summary>Deja el paso para una persona (como ReturnToHuman) pero ademas NOTIFICA al encargado
    /// para que sepa que el agente no pudo y hay que atenderlo.</summary>
    Notify = 3
}
