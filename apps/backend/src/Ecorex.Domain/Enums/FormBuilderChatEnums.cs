namespace Ecorex.Domain.Enums;

/// <summary>Estado de una conversacion del asistente de creacion de formularios.</summary>
public enum FormBuilderConversationStatus
{
    Active = 0,
    Closed = 1,
}

/// <summary>
/// Rol de un mensaje de la conversacion del asistente. Ademas de user/assistant/tool (function calling),
/// existe Proposal: una accion MUTANTE que el agente quiere ejecutar y que el usuario debe confirmar
/// (gate humano) antes de correrla.
/// </summary>
public enum FormBuilderMessageRole
{
    User = 0,
    Assistant = 1,
    Tool = 2,
    Proposal = 3,
    System = 4,
}

/// <summary>Estado de una propuesta (accion mutante) que espera confirmacion del usuario.</summary>
public enum FormBuilderProposalState
{
    None = 0,
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
}
