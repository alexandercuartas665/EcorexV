namespace Ecorex.Domain.Enums;

/// <summary>
/// Que se le pide al cliente en el enlace publico de una salida de compuerta (feature generica de
/// "decision del cliente por enlace"). La salida se resuelve igual por su nodo destino; esto solo
/// cambia la captura que se pide antes de resolverla.
/// </summary>
public enum WorkflowDecisionCapture
{
    /// <summary>Confirmacion directa: el cliente pulsa el boton y la salida se toma (sin captura extra).</summary>
    None = 0,

    /// <summary>Firma dibujada (pad -> PNG) que se guarda como evidencia/adjunto de la actividad.</summary>
    Signature = 1,

    /// <summary>Observacion de texto (ej. motivo) que queda como comentario de la ruta + bitacora.</summary>
    Observation = 2
}
