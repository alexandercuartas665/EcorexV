namespace Ecorex.Domain.Enums;

/// <summary>
/// Grado de autonomia con el que un agente de IA atiende un paso de flujo (ola 1 de agentes
/// en nodos). Es CONFIGURABLE POR NODO (vive en WorkflowNodeAgent, no en el AiAgent): el mismo
/// agente puede cerrar solo un paso trivial de clasificacion y, en otro flujo, limitarse a
/// proponer en un paso que exige responsabilidad humana.
/// </summary>
public enum WorkflowAgentAutonomy
{
    /// <summary>
    /// El agente COMPLETA el paso por su cuenta: su resultado se aplica y el flujo avanza sin
    /// intervencion humana (la ejecucion real es la ola 2).
    /// </summary>
    Autonomous = 0,

    /// <summary>
    /// El agente PROPONE un resultado y el paso queda esperando que una persona lo confirme o
    /// lo corrija antes de avanzar. Valor por defecto: lo prudente es no cerrar pasos solo.
    /// </summary>
    Proposes = 1
}
