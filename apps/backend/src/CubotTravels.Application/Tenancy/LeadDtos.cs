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
    DateTimeOffset StageChangedAt,
    IReadOnlyDictionary<string, string?> FieldValues);

public sealed record LeadActivityDto(Guid Id, string ActivityType, string? Description, DateTimeOffset CreatedAt, string? ActorName);

public sealed record LeadNoteDto(Guid Id, string Content, string Color, DateTimeOffset CreatedAt, string? ActorName);

public sealed record LeadFileDto(Guid Id, string FileName, string Url, string ContentType, long SizeBytes, DateTimeOffset CreatedAt, string? ActorName);

public sealed record LeadDetailDto(LeadDto Lead, IReadOnlyList<LeadActivityDto> Activities);

public sealed record ArchivedLeadDto(
    Guid Id,
    string ContactName,
    string? ContactPhone,
    string? Destination,
    decimal? EstimatedValue,
    string? Currency,
    string? ArchiveReason,
    string? ArchiveNote,
    DateTimeOffset? ArchivedAt,
    string? ArchivedByName,
    Guid? AssignedToTenantUserId,
    Guid StageId,
    LeadStatus Status,
    DateTimeOffset StageChangedAt,
    IReadOnlyDictionary<string, string?> FieldValues);

public sealed record CreateLeadRequest(
    string ContactName,
    string? ContactPhone = null,
    string? Destination = null,
    decimal? EstimatedValue = null,
    string? Currency = null,
    Guid? StageId = null);

public sealed record UpdateLeadRequest(
    string ContactName,
    string? ContactPhone,
    string? Destination,
    decimal? EstimatedValue,
    string? Currency,
    Dictionary<string, string?>? FieldValues);

public sealed record MoveLeadRequest(Guid StageId, string? LossReason = null);

public sealed record AssignLeadRequest(Guid? TenantUserId);
