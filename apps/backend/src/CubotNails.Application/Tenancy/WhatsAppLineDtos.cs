using CubotNails.Domain.Enums;

namespace CubotNails.Application.Tenancy;

public sealed record WhatsAppLineDto(
    Guid Id,
    string InstanceName,
    string? PhoneNumber,
    WhatsAppLineStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastStatusAt);

public sealed record CreateWhatsAppLineRequest(string InstanceName, string? PhoneNumber = null);

public sealed record ChangeLineStatusRequest(WhatsAppLineStatus Status);

public sealed record AssignLineRequest(Guid? TenantUserId);
