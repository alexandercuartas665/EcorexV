namespace Ecorex.Application.Voice;

/// <summary>
/// Datos para colocar una llamada IA. El tenant es el AMBIENTE (filtro global); aqui solo van los datos del
/// contacto y de la accion (Fase A). El servicio compone el prompt del AiAgent y lo hace el prompt del agente
/// Retell (reemplaza el suyo).
/// </summary>
public sealed record VoicePlaceCallRequest(
    string ToNumber,
    Guid? AiAgentId,
    string? PromptExtra,
    string? Objetivo,
    IReadOnlyList<Guid> FormulariosPermitidos,
    IReadOnlyDictionary<string, string?> ContactVariables,
    Guid? ContactWorkflowRunId = null);

/// <summary>Resultado de colocar la llamada. Retryable = fallo transitorio (5xx/red); un 4xx NUNCA es retryable.</summary>
public sealed record VoicePlaceCallResult(bool Placed, string? CallId, string? Error, bool Retryable);

public interface IRetellVoiceService
{
    /// <summary>Coloca una llamada saliente para el tenant ambiente. No hace deduplicacion (eso lo garantiza
    /// el ContactWorkflowRun aguas arriba): nunca reintenta a ciegas una llamada.</summary>
    Task<VoicePlaceCallResult> PlaceCallAsync(VoicePlaceCallRequest request, CancellationToken cancellationToken = default);
}
