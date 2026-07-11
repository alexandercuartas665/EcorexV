using Ecorex.Domain.Enums;

namespace Ecorex.Application.Notifications;

/// <summary>Notificacion in-app para la campana / bandeja del workspace (Ola 7).</summary>
public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    string Title,
    string Body,
    string? LinkRoute,
    Guid? RelatedTaskItemId,
    string? ActorName,
    bool IsRead,
    DateTimeOffset CreatedAt);
