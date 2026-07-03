namespace Ecorex.Domain.Enums;

/// <summary>Disparador (evento) de una regla de automatizacion sobre el embudo y los chats.</summary>
public enum AutomationTrigger
{
    /// <summary>El cliente/chat lleva N minutos sin respuesta del asesor.</summary>
    NoReply = 0,

    /// <summary>El lead entra/pasa a una etapa especifica.</summary>
    StageEntered,

    /// <summary>Se crea un lead nuevo.</summary>
    LeadCreated,

    /// <summary>Llega un mensaje entrante de un numero que aun no es lead.</summary>
    IncomingNoLead,

    /// <summary>Entra un chat dentro de una ventana horaria (ej. 22:00-06:00).</summary>
    ChatInTimeWindow
}

/// <summary>Accion a ejecutar cuando se cumple el disparador.</summary>
public enum AutomationAction
{
    /// <summary>Crea una tarea de seguimiento para el asesor.</summary>
    CreateFollowUp = 0,

    /// <summary>Marca el lead para revision (alerta al asesor).</summary>
    NotifyAdvisor,

    /// <summary>Crea el lead y responde con una plantilla pregrabada.</summary>
    CreateLeadAndReply,

    /// <summary>Notifica al supervisor del equipo.</summary>
    NotifySupervisor,

    /// <summary>Asigna el chat/lead a un turno (ej. turno noche).</summary>
    AssignToShift,

    /// <summary>Genera un link de pago Wompi para el lead.</summary>
    GenerateWompiLink
}
