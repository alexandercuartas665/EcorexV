using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

public sealed record FollowUpTaskDto(
    Guid Id,
    Guid LeadId,
    string Title,
    string? Notes,
    DateTimeOffset DueAt,
    FollowUpTaskStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset? CompletedAt);

public sealed record CreateFollowUpTaskRequest(
    Guid LeadId,
    string Title,
    DateTimeOffset DueAt,
    string? Notes = null,
    Guid? AssignedToTenantUserId = null);
