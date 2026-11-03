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

    /// <summary>Cursor anti-rafaga del agente conversacional (SARA): SentAt del ULTIMO mensaje ENTRANTE que el
    /// agente ya respondio. Si llega un entrante DURANTE la corrida del agente (posterior a la foto que tomo de la
    /// conversacion) queda con SentAt &gt; este cursor, y una corrida de respaldo lo detecta como "sin responder" y
    /// lo contesta, en vez de ignorarlo porque el ultimo mensaje ya es la saliente del agente. Null = nunca
    /// respondio (se comporta como MinValue). Solo lo AVANZA el agente al enviar; una saliente HUMANA no lo toca.</summary>
    public DateTimeOffset? LastAnsweredInboundAt { get; set; }

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

    /// <summary>
    /// CIERRE-POR-FLUJO (traspaso a SARA): cuando el agente de un flujo enruta la compuerta de cierre de este caso,
    /// se estampa AQUI el momento. SARA (agente de la linea) lee esta marca para NO crear tarea/lead duplicados: el
    /// caso ya lo gestiono el flujo. Acotado a la actividad por <see cref="FlowHandledTaskId"/>. Se limpia cuando un
    /// flujo NUEVO toma la linea (el flujo que entra es el que atiende) y se ignora pasada una ventana corta (para
    /// no bloquear un cierre legitimo futuro en la misma conversacion). Null = no hay cierre-por-flujo pendiente.
    /// </summary>
    public DateTimeOffset? FlowHandledCloseAt { get; set; }

    /// <summary>La ACTIVIDAD (tarea) cuyo cierre gestiono el flujo (ver <see cref="FlowHandledCloseAt"/>). Acota la
    /// marca a ese caso. Null = sin cierre-por-flujo pendiente.</summary>
    public Guid? FlowHandledTaskId { get; set; }

    /// <summary>
    /// ADR-0125 v2: TAREA DUENA ACTUAL del hilo. El mismo hilo de WhatsApp (linea + contacto) se reutiliza para
    /// TODOS los negocios del cliente en el tiempo; un soporte de pago llega de forma asincrona (casi siempre
    /// cuando el flujo NO esta en hold), asi que atar la captura del adjunto al hold es demasiado estrecho. Esta
    /// marca fija, de forma deterministica, A QUE tarea cae la media entrante: la actualiza el motor cada vez que
    /// un paso ENGANCHA al cliente por esta conversacion (toma la linea / le pregunta). Al abrir un negocio nuevo
    /// que vuelve a enganchar al mismo cliente, la titularidad pasa a la tarea nueva. Si la dueña ya esta cerrada o
    /// no esta fijada, la ingesta cae a un fallback (unica tarea abierta del hilo) y, si no hay, a la bandeja de
    /// soportes sin tarea. NUNCA se adjunta a una tarea cerrada. Null = sin dueña fijada (holds/hilos viejos).
    /// </summary>
    public Guid? CurrentTaskItemId { get; set; }
}
