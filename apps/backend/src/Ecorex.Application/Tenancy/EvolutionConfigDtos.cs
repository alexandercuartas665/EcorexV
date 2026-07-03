namespace Ecorex.Application.Tenancy;

public sealed record EvolutionConfigDto(
    string BaseUrl,
    string InstanceName,
    string MaskedToken,
    string? WebhookUrl,
    bool IsActive,
    DateTimeOffset? LastValidatedAt);

public sealed record UpsertEvolutionConfigRequest(
    string BaseUrl,
    string InstanceName,
    string? ApiToken,
    string? WebhookUrl = null);
