namespace Ecorex.Domain.Enums;

/// <summary>
/// ADR-0125 v2: estado de RUTEO de un adjunto entrante del cliente (soporte de pago, documento) respecto a
/// una tarea del flujo. Vive en <see cref="Entities.Message"/> (el adjunto ya es un Message con MediaUrl) para
/// no crear un agregado nuevo. Solo lo marcan los entrantes CON media; el texto normal queda en None.
/// </summary>
public enum ClientSupportStatus
{
    /// <summary>No aplica: mensaje de texto, o media que no se evaluo para ruteo (p.ej. salientes).</summary>
    None = 0,

    /// <summary>Media del cliente que NO encontro tarea destino abierta: queda en la BANDEJA de soportes sin
    /// tarea para que un humano la archive. El archivo NO se pierde (sigue en el chat).</summary>
    Pending = 1,

    /// <summary>La media quedo adjunta a una tarea (automatica por hold/dueña/fallback, o movida por un humano).
    /// La tarea destino queda en <see cref="Entities.Message.SupportTaskItemId"/>.</summary>
    Routed = 2,

    /// <summary>Un humano descarto el soporte desde la bandeja sin asignarlo a ninguna tarea (no era un
    /// soporte de tarea). Sale de la bandeja; el archivo sigue en el chat.</summary>
    Dismissed = 3
}
