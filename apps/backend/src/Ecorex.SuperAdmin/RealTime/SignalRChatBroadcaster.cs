using Ecorex.Application.Tenancy;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Difunde por SignalR los mensajes nuevos al grupo de la conversacion.</summary>
public sealed class SignalRChatBroadcaster : IChatBroadcaster
{
    private readonly IHubContext<ChatHub> _hub;

    public SignalRChatBroadcaster(IHubContext<ChatHub> hub)
    {
        _hub = hub;
    }

    public async Task MessageAddedAsync(Guid tenantId, Guid conversationId, MessageDto message, CancellationToken cancellationToken = default)
    {
        // Al grupo de la conversacion (chat abierto) y al grupo del tenant (recolorear el tablero).
        await _hub.Clients.Group(ChatHub.Group(conversationId.ToString())).SendAsync("MessageAdded", message, cancellationToken);
        await _hub.Clients.Group(ChatHub.TenantGroup(tenantId.ToString())).SendAsync("BoardChanged", cancellationToken);
    }
}
