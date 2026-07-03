using Ecorex.Domain.Enums;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Notifica en tiempo real (SignalR) que una tarea del nucleo cambio (creacion, edicion o
/// cambio de estado) para que los tableros abiertos del tenant refresquen la tarjeta afectada.
/// La implementacion vive en la app host (patron IChatBroadcaster).
/// </summary>
public interface ITaskBroadcaster
{
    Task TaskChangedAsync(Guid tenantId, Guid taskId, TaskItemStatus status, CancellationToken cancellationToken = default);
}

/// <summary>Implementacion por defecto (no hace nada) para procesos sin SignalR (Api, tests).</summary>
public sealed class NoOpTaskBroadcaster : ITaskBroadcaster
{
    public Task TaskChangedAsync(Guid tenantId, Guid taskId, TaskItemStatus status, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
