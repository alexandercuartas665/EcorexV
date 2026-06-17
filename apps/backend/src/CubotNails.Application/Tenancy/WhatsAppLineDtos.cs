using CubotNails.Domain.Enums;

namespace CubotNails.Application.Tenancy;

public sealed record WhatsAppLineDto(
    Guid Id,
    string InstanceName,
    string? PhoneNumber,
    WhatsAppLineStatus Status,
    Guid? AssignedToTenantUserId,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastStatusAt,
    WhatsAppProvider Provider = WhatsAppProvider.Evolution,
    string? CloudPhoneNumberId = null,
    string? CloudBusinessAccountId = null,
    bool HasCloudToken = false);

public sealed record CreateWhatsAppLineRequest(
    string InstanceName,
    string? PhoneNumber = null,
    WhatsAppProvider Provider = WhatsAppProvider.Evolution,
    string? CloudPhoneNumberId = null,
    string? CloudAccessToken = null,
    string? CloudBusinessAccountId = null);

public sealed record ChangeLineStatusRequest(WhatsAppLineStatus Status);

public sealed record AssignLineRequest(Guid? TenantUserId);
