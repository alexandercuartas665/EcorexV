namespace CubotNails.Application.Tenancy;

public enum ChatIngestResult
{
    Unauthorized,
    Accepted,
    Duplicate
}

/// <summary>
/// Ingesta de mensajes entrantes desde el webhook de Evolution (modulo 2.3, ver ADR-0006).
/// Opera con tenantId explicito (sin JWT) y es idempotente por ExternalMessageId.
/// </summary>
public interface IChatIngestService
{
    Task<ChatIngestResult> IngestAsync(Guid tenantId, string? providedToken, IngestMessageRequest payload, CancellationToken cancellationToken = default);

    /// <summary>Persiste un entrante ya autorizado por el llamador (webhook crudo validado con token global + instancia conocida).</summary>
    Task<ChatIngestResult> IngestTrustedAsync(Guid tenantId, IngestMessageRequest payload, CancellationToken cancellationToken = default);
}
