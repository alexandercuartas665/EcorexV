using CubotTravels.Domain.Enums;

namespace CubotTravels.Application.Tenancy;

public sealed record LeadDto(
    Guid Id,
    string ContactName,
    string? ContactPhone,
    string? Destination,
    decimal? EstimatedValue,
    string? Currency,
    Guid StageId,
    LeadStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset StageChangedAt);

public sealed record LeadActivityDto(Guid Id, string ActivityType, string? Description, DateTimeOffset CreatedAt);

public sealed record LeadDetailDto(LeadDto Lead, IReadOnlyList<LeadActivityDto> Activities);

public sealed record CreateLeadRequest(
    string ContactName,
    string? ContactPhone = null,
    string? Destination = null,
    decimal? EstimatedValue = null,
    string? Currency = null,
    Guid? StageId = null);

public sealed record MoveLeadRequest(Guid StageId, string? LossReason = null);

public sealed record AssignLeadRequest(Guid? TenantUserId);
