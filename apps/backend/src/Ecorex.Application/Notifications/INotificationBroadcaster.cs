namespace Ecorex.Application.Notifications;

/// <summary>
/// Notifica en tiempo real (SignalR) que un usuario recibio una notificacion in-app nueva, para que
/// la campana del workspace refresque su contador sin recargar. La implementacion vive en la app host
/// (patron ITaskBroadcaster/IChatBroadcaster).
/// </summary>
public interface INotificationBroadcaster
{
    Task NotificationAddedAsync(Guid recipientTenantUserId, CancellationToken cancellationToken = default);
}

/// <summary>Implementacion por defecto (no hace nada) para procesos sin SignalR (Api, tests).</summary>
public sealed class NoOpNotificationBroadcaster : INotificationBroadcaster
{
    public Task NotificationAddedAsync(Guid recipientTenantUserId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
