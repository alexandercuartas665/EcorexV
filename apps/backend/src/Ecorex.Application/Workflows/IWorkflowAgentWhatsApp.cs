namespace Ecorex.Application.Workflows;

/// <summary>
/// ADR-0092: costura para que el agente de un paso de flujo PREGUNTE por WhatsApp y consiga un dato. Vive en
/// Application (no necesita SuperAdmin, a diferencia de Colmena): usa el conector WhatsApp y el store de chat,
/// ambos de Application. Resuelve/crea la conversacion de <c>(tenant, linea, telefono)</c> para que la RESPUESTA
/// entrante correlacione (el webhook de entrada casa por esa misma clave), decide si el primer mensaje va como
/// PLANTILLA (ventana de 24h cerrada) o texto libre (ventana abierta), envia y persiste el saliente. Devuelve el
/// Id de la conversacion para que el runner deje el paso EN ESPERA de la respuesta. NUNCA lanza: un fallo vuelve
/// como resultado con error legible (el paso volvera a una persona).
/// </summary>
public interface IWorkflowAgentWhatsApp
{
    Task<WhatsAppAskResult> AskAsync(WhatsAppAskCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Pregunta que el agente quiere enviar por WhatsApp para conseguir un dato.</summary>
/// <param name="TenantId">Tenant del paso (se fija en las entidades creadas: en este codigo el TenantId se
/// asigna a mano, no por interceptor).</param>
/// <param name="LineId">Linea WhatsApp del nodo (permiso explicito).</param>
/// <param name="Phone">Telefono del destinatario (se normaliza a digitos).</param>
/// <param name="Question">Texto de la pregunta (cuerpo libre, o la variable {{1}} de la plantilla).</param>
/// <param name="TemplateName">Plantilla aprobada para el primer contacto en frio. Null = solo ventana abierta.</param>
/// <param name="TemplateLang">Idioma de la plantilla (ej. "es"); se asume "es" si viene vacio con plantilla.</param>
public sealed record WhatsAppAskCommand(
    Guid TenantId, Guid LineId, string Phone, string Question, string? TemplateName, string? TemplateLang);

/// <summary>Resultado del envio. <paramref name="ConversationId"/> es la conversacion en la que el paso espera
/// la respuesta (no null cuando <paramref name="Sent"/> es true).</summary>
public sealed record WhatsAppAskResult(bool Sent, Guid? ConversationId, string? Error)
{
    public static WhatsAppAskResult Fail(string error) => new(false, null, error);
    public static WhatsAppAskResult Ok(Guid conversationId) => new(true, conversationId, null);
}
