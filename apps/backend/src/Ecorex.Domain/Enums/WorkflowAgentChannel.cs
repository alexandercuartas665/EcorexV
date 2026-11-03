namespace Ecorex.Domain.Enums;

/// <summary>
/// Ola 2 (ADR-0142): canales de comunicacion que un nodo de flujo PERMITE usar a su agente. Flags combinables.
/// Se guarda en <see cref="Ecorex.Domain.Entities.WorkflowNodeAgent.AllowedChannels"/> como int nullable:
/// NULL = todos los canales configurados permitidos (RETROCOMPATIBLE: los flujos que ya corren no cambian).
/// Con valor: solo los canales marcados se habilitan, ademas de que el recurso respectivo este configurado
/// (una linea de WhatsApp, un agente de voz, un cliente Colmena, permiso de correo).
/// </summary>
[Flags]
public enum WorkflowAgentChannel
{
    None = 0,

    /// <summary>Llamada de voz (Retell). Gatea 'llamar_telefono' (requiere VoiceAiAgentId).</summary>
    Voz = 1,

    /// <summary>Mensaje de WhatsApp. Gatea 'preguntar_whatsapp' (requiere WhatsAppLineId).</summary>
    WhatsApp = 2,

    /// <summary>Busqueda web con el navegador Colmena. Gatea 'buscar_web' (requiere ColmenaClientId).</summary>
    Web = 4,

    /// <summary>Envio de correo. Gatea 'enviar_correo' (requiere CanSendEmail).</summary>
    Correo = 8
}
