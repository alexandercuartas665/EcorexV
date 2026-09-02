namespace Ecorex.Domain.Enums;

/// <summary>
/// Estado de una llamada de voz IA (Retell). Espeja el ciclo de vida de la llamada segun los eventos de
/// webhook (call_started/ended/analyzed) mas los estados locales de creacion/fallo.
/// </summary>
public enum VoiceCallStatus
{
    /// <summary>Creada en Retell (respuesta 201 de create-phone-call), aun no conectada.</summary>
    Registered,

    /// <summary>En curso (evento call_started).</summary>
    Ongoing,

    /// <summary>Terminada (evento call_ended); aun sin analisis post-llamada.</summary>
    Ended,

    /// <summary>Analizada (evento call_analyzed): transcripcion y analisis disponibles.</summary>
    Analyzed,

    /// <summary>Error reportado por Retell durante la llamada.</summary>
    Error,

    /// <summary>No se pudo colocar (fallo local: config, E.164, error del API al crear).</summary>
    Failed
}
