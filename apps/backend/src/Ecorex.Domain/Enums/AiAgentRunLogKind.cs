namespace Ecorex.Domain.Enums;

/// <summary>Tipo de evento en la bitacora de atencion del agente de IA.</summary>
public enum AiAgentRunLogKind
{
    /// <summary>Mensaje entrante del cliente recibido por la linea.</summary>
    Inbound,
    /// <summary>Prompt enviado al proveedor de IA (principal o extractor de cache).</summary>
    Prompt,
    /// <summary>El modelo solicito y se ejecuto una herramienta (con argumentos y resultado).</summary>
    Tool,
    /// <summary>Respuesta del agente enviada al cliente por la linea.</summary>
    Reply,
    /// <summary>Nota informativa del proceso (cache vaciada, sesion cerrada, etc.).</summary>
    Info,
    /// <summary>Error durante la atencion.</summary>
    Error
}
