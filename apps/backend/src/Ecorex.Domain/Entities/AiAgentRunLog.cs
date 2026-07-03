using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Bitacora de atencion del agente de IA (capa 3). Entidad TENANT-SCOPED. Persiste, por
/// conversacion, el rastro del proceso: mensajes recibidos, prompts enviados a la IA,
/// herramientas ejecutadas (con argumentos y resultado) y respuestas enviadas. Equivale al
/// panel "PROMPTS enviados a la IA" del chat de prueba, pero guardado para revisar la atencion
/// real de cada cliente.
/// </summary>
public class AiAgentRunLog : TenantEntity
{
    public Guid ConversationId { get; set; }
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
