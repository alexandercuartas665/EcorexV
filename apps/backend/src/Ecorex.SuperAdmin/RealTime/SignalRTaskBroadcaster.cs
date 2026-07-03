using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Difunde por SignalR el evento "TaskChanged" {taskId, status} al grupo del tenant
/// cuando se crea/edita/cambia de estado una tarea del nucleo (patron SignalRChatBroadcaster).</summary>
public sealed class SignalRTaskBroadcaster : ITaskBroadcaster
{
    private readonly IHubContext<TaskHub> _hub;

    public SignalRTaskBroadcaster(IHubContext<TaskHub> hub)
    {
        _hub = hub;
    }

    public Task TaskChangedAsync(Guid tenantId, Guid taskId, TaskItemStatus status, CancellationToken cancellationToken = default)
        => _hub.Clients.Group(TaskHub.TenantGroup(tenantId.ToString()))
            .SendAsync("TaskChanged", taskId.ToString(), status.ToString(), cancellationToken);
}
