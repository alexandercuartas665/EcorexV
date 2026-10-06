using Ecorex.Domain.Common;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Conversacion de WhatsApp con un contacto (modulo 2.3). Entidad TENANT-SCOPED.
/// Una por (TenantId, ContactPhone). Puede asociarse a un lead.
/// </summary>
public class Conversation : TenantEntity
{
    public string ContactPhone { get; set; } = null!;

    /// <summary>
    /// Jid COMPLETO del contacto en WhatsApp (key.remoteJid del webhook), con su sufijo:
    /// "@s.whatsapp.net" para numeros reales o "@lid" para contactos por LID (identificador de
    /// privacidad de WhatsApp que NO es un telefono). Se usa como destino real del envio saliente;
    /// null en conversaciones viejas (antes de esta funcion), donde se reconstruye desde ContactPhone.
    /// </summary>
    public string? RemoteJid { get; set; }

    public string? ContactName { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? WhatsAppLineId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }

    /// <summary>Cuando se archivo la conversacion (se oculta de la bandeja activa). Null = activa.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>
    /// Punto de REINICIO del contexto del agente (cierre "olvidar cliente", no destructivo): el agente solo
    /// considera los mensajes POSTERIORES a esta marca al construir su contexto, asi saluda desde cero en la
    /// proxima interaccion. El historial NO se borra (sigue visible para humanos). Null = sin reinicio.
    /// </summary>
    public DateTimeOffset? AgentContextResetAt { get; set; }

    /// <summary>
    /// Reactivacion (secuencia de seguimiento del agente): cuantos PASOS de la secuencia ya se enviaron a
    /// esta conversacion (0 = ninguno). Se reinicia a 0 cuando el cliente vuelve a responder (revivio). El
    /// paso se cuenta sobre la lista de pasos HABILITADOS ordenados por horas de inactividad.
    /// </summary>
    public int ReactivacionUltimoPaso { get; set; }

    /// <summary>Cuando se envio el ultimo paso de reactivacion. Null = aun no se envio ninguno. Sirve para
    /// detectar que el cliente respondio DESPUES del ultimo seguimiento (reinicio de la secuencia).</summary>
    public DateTimeOffset? ReactivacionUltimoEnvioAt { get; set; }

    /// <summary>
    /// ADR-0122: la conversacion esta "tomada por el flujo". Cuando un paso de agente toma la linea para
    /// preguntarle al cliente, anota AQUI el PASO (<see cref="FlowHoldStepId"/>) y el NODO
    /// (<see cref="FlowHoldNodeId"/>) que la tomo. Sirve para: (1) UN SOLO DUENO -ningun otro paso puede tomar
    /// la misma conversacion a la vez-, (2) enrutar la respuesta del cliente EXACTAMENTE a ese paso/nodo (no a
    /// todos los que esperan), (3) que SARA (agente de la linea) se calle mientras este tomada. Null = libre;
    /// el flujo la libera (pone null) al resolver la salida o devolver a persona.
    /// </summary>
    public Guid? FlowHoldStepId { get; set; }

    /// <summary>ADR-0122: el NODO del flujo que tomo esta conversacion (ver <see cref="FlowHoldStepId"/>). Es "el
    /// id del nodo que lo hizo", para atender la respuesta desde ese flujo y ese nodo.</summary>
    public Guid? FlowHoldNodeId { get; set; }
}
