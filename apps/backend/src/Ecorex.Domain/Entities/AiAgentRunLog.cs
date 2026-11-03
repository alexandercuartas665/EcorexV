using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Bitacora de atencion del agente de IA (capa 3). Entidad TENANT-SCOPED. Persiste el rastro del proceso:
/// mensajes recibidos, prompts enviados a la IA, herramientas ejecutadas (con argumentos y resultado) y
/// respuestas enviadas. Equivale al panel "PROMPTS enviados a la IA" del chat de prueba, pero guardado para
/// revisar la atencion real de cada cliente.
///
/// Ola 4 (ADR-0142, opcion A): el rastro puede colgar de una CONVERSACION de WhatsApp
/// (<see cref="ConversationId"/>) o de una LLAMADA de voz (<see cref="VoiceCallId"/>), pero NUNCA de las dos a la
/// vez (restriccion de BD "exactamente uno"). La voz no tiene <c>Conversation</c> (concepto de chat de WhatsApp);
/// su fuente de verdad es <see cref="VoiceCall"/> y la bitacora solo la APUNTA. El riel de /bitacora-agente agrupa
/// por CONTACTO (telefono), intercalando WhatsApp y voz.
/// </summary>
public class AiAgentRunLog : TenantEntity
{
    /// <summary>Conversacion de WhatsApp a la que pertenece el evento. Null cuando el evento es de una llamada de
    /// voz (<see cref="VoiceCallId"/>). Exactamente uno de los dos esta seteado.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>Ola 4: llamada de voz (Retell) a la que pertenece el evento. Null cuando es un evento de WhatsApp
    /// (<see cref="ConversationId"/>). Apunta a <see cref="VoiceCall"/> (fuente de verdad); no duplica su estado.</summary>
    public Guid? VoiceCallId { get; set; }

    public Guid AgentId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
    public AiAgentRunLogKind Kind { get; set; }

    /// <summary>Titulo corto del evento (ej. "Herramienta: crear_lead").</summary>
    public string Title { get; set; } = null!;

    /// <summary>Contenido principal (prompt enviado, argumentos de la herramienta, texto recibido).</summary>
    public string? Content { get; set; }

    /// <summary>Respuesta asociada (texto del LLM, resultado JSON de la herramienta).</summary>
    public string? Response { get; set; }
}
