namespace Ecorex.Application.Voice;

/// <summary>
/// Vista de una LINEA de voz del tenant SIN secretos: solo booleanos "tiene key/tiene password" y datos no
/// sensibles. El secreto (key/password) jamas sale del backend ni se loguea.
/// </summary>
public sealed record RetellVoiceLineDto(
    Guid Id,
    string Name,
    bool HasApiKey,
    string? FromNumber,
    string? TerminationUri,
    string? SipUsername,
    bool HasSipPassword,
    string? VoiceId,
    string Language,
    bool IsEnabled,
    bool IsDefault,
    DateTimeOffset? LastValidatedAt);

/// <summary>
/// Alta/edicion de una linea. Secretos: null = NO cambiar (conserva el cifrado existente); cadena vacia =
/// BORRAR; valor = re-cifrar. Nunca se devuelve el secreto.
/// </summary>
public sealed record SaveRetellVoiceLineRequest(
    string Name,
    string? ApiKey,
    string? FromNumber,
    string? TerminationUri,
    string? SipUsername,
    string? SipPassword,
    string? VoiceId,
    string? Language,
    bool IsEnabled,
    bool IsDefault);

public sealed record RetellValidationResult(bool Ok, string? Error);

public interface IRetellVoiceLineService
{
    /// <summary>Lineas del tenant actual (ordenadas por nombre).</summary>
    Task<IReadOnlyList<RetellVoiceLineDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<RetellVoiceLineDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RetellVoiceLineDto> CreateAsync(SaveRetellVoiceLineRequest request, CancellationToken cancellationToken = default);

    /// <summary>Actualiza una linea del tenant. Null si no existe.</summary>
    Task<RetellVoiceLineDto?> UpdateAsync(Guid id, SaveRetellVoiceLineRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Valida la key de la linea contra Retell (ping liviano, sin colocar llamadas).</summary>
    Task<RetellValidationResult> ValidateAsync(Guid id, CancellationToken cancellationToken = default);
}
