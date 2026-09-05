using Ecorex.Domain.Enums;

namespace Ecorex.Application.Workflows;

/// <summary>
/// Seam ESTRECHO hacia el proveedor de IA para atender un paso de flujo (agentes en nodos, ola 2).
/// Es lo unico que hace red: recibe el contexto ya armado (ola 1) y devuelve la decision del modelo.
///
/// Existe como interfaz propia por dos razones:
/// (1) la llamada al proveedor tarda segundos y puede fallar, asi que tiene que quedar FUERA de la
///     transaccion del motor de flujos; separarla en su propio servicio hace imposible por
///     construccion que alguien la meta dentro;
/// (2) en pruebas se sustituye por un doble, de modo que el cupo, la auditoria del autor, el
///     registro de consumo y la vuelta a una persona se ejercitan DE VERDAD sin llamar a nadie.
/// </summary>
public interface IWorkflowAgentInvoker
{
    /// <summary>
    /// Pregunta al agente que hacer con el paso. NUNCA lanza por un fallo del proveedor: un error
    /// de red o una respuesta ilegible se devuelven como resultado no-Ok con su motivo legible,
    /// porque para el flujo "el agente no pudo" es un caso de negocio, no una excepcion.
    /// </summary>
    Task<WorkflowAgentInvocationResult> InvokeAsync(
        WorkflowAgentContextDto context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lo que el agente respondio. Los tokens vienen SIEMPRE (incluso si Ok es false): si el proveedor
/// alcanzo a facturar la llamada, el tenant la tiene que ver en su consumo.
/// </summary>
/// <param name="Ok">El agente resolvio. False = no pudo, y <paramref name="Error"/> dice por que.</param>
/// <param name="Result">Resultado propuesto (mismo vocabulario que ApprovalResult, ej. "Approved"). Para un
/// paso Task. Null en una compuerta (ahi la decision es <paramref name="Route"/>).</param>
/// <param name="Comment">Justificacion del agente, legible por una persona.</param>
/// <param name="Error">Motivo legible por el que no pudo resolver.</param>
/// <param name="Route">Clave (BpmnElementId del destino) de la RUTA elegida en una compuerta (ADR-0090 ola
/// B). Null en un paso Task.</param>
public sealed record WorkflowAgentInvocationResult(
    bool Ok,
    string? Result,
    string? Comment,
    string? Error,
    AiProvider Provider = AiProvider.Claude,
    string Model = "",
    int InputTokens = 0,
    int OutputTokens = 0,
    string? Route = null,
    // Valores que el agente puso en el FORMULARIO del paso (fieldCode -> valor), via tool-calling (ADR-0090
    // ola C). Null si el nodo no tiene formulario. El runner los envia por SaveAsync (misma validacion que un
    // humano); el invoker NO escribe: solo acumula lo que el modelo fijo.
    IReadOnlyDictionary<string, string?>? Fields = null)
{
    public static WorkflowAgentInvocationResult Failed(string error, AiProvider provider = AiProvider.Claude, string model = "", int inputTokens = 0, int outputTokens = 0)
        => new(false, null, null, error, provider, model, inputTokens, outputTokens);
}
