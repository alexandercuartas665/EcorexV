using Ecorex.Application.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Difunde por SignalR el evento "NotificationAdded" al grupo del TenantUser destinatario
/// cuando recibe una notificacion in-app nueva, para refrescar el badge de la campana (#4b).</summary>
public sealed class SignalRNotificationBroadcaster : INotificationBroadcaster
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRNotificationBroadcaster(IHubContext<NotificationHub> hub)
    {
        _hub = hub;
    }

    public Task NotificationAddedAsync(Guid recipientTenantUserId, CancellationToken cancellationToken = default)
        => _hub.Clients.Group(NotificationHub.UserGroup(recipientTenantUserId.ToString()))
            .SendAsync("NotificationAdded", cancellationToken);
}
