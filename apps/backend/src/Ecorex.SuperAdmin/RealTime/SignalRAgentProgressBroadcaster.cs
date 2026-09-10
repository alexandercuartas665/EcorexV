using Ecorex.Application.Tenancy;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Capa 2 (ADR-0091): difunde por SignalR el evento "AgentProgress" {taskId, nodeId, fase, tokens}
/// al grupo del tenant mientras el agente atiende un paso, para que el nodo abierto muestre su pensamiento
/// y los tokens en vivo (patron SignalRTaskBroadcaster, mismo TaskHub que ya escuchan los tableros y el
/// detalle de la tarea).</summary>
public sealed class SignalRAgentProgressBroadcaster : IAgentProgressBroadcaster
{
    private readonly IHubContext<TaskHub> _hub;

    public SignalRAgentProgressBroadcaster(IHubContext<TaskHub> hub)
    {
        _hub = hub;
    }

    public Task AgentProgressAsync(
        Guid tenantId, Guid taskId, Guid nodeId, string phase, long tokens,
        CancellationToken cancellationToken = default)
        => _hub.Clients.Group(TaskHub.TenantGroup(tenantId.ToString()))
            .SendAsync("AgentProgress", taskId.ToString(), nodeId.ToString(), phase, tokens, cancellationToken);
}
