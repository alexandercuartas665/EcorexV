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
    Guid? ContactWorkflowRunId = null,
    // Linea de voz a usar. Null = se elige la por defecto (o la primera habilitada+configurada).
    // La prueba por tarjeta la fija para probar una linea CONCRETA.
    Guid? RetellVoiceLineId = null);

/// <summary>Resultado de colocar la llamada. Retryable = fallo transitorio (5xx/red); un 4xx NUNCA es retryable.</summary>
public sealed record VoicePlaceCallResult(bool Placed, string? CallId, string? Error, bool Retryable);

/// <summary>Estado y resultado de una llamada (lo que llega por webhook), para mostrarlo en la UI de prueba.</summary>
public sealed record VoiceCallResultDto(
    string CallId,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int? DurationSeconds,
    decimal? CostUsd,
    string? TranscriptText,
    string? RecordingUrl,
    string? AnalysisSummary);

public interface IRetellVoiceService
{
    /// <summary>Coloca una llamada saliente para el tenant ambiente. No hace deduplicacion (eso lo garantiza
    /// el ContactWorkflowRun aguas arriba): nunca reintenta a ciegas una llamada.</summary>
    Task<VoicePlaceCallResult> PlaceCallAsync(VoicePlaceCallRequest request, CancellationToken cancellationToken = default);

    /// <summary>Estado/resultado de una llamada del tenant por su call_id (para refrescar la UI de prueba).
    /// Null si no existe (o no es del tenant). Se va completando a medida que llegan los webhooks.</summary>
    Task<VoiceCallResultDto?> GetCallResultAsync(string callId, CancellationToken cancellationToken = default);
}
