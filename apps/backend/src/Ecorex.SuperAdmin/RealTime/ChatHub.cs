using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Hub de chat en tiempo real. Los clientes se unen al grupo de una conversacion para
/// recibir los mensajes nuevos (entrantes y salientes) en caliente.</summary>
public sealed class ChatHub : Hub
{
    public Task JoinConversation(string conversationId)
        => Groups.AddToGroupAsync(Context.ConnectionId, Group(conversationId));

    public Task LeaveConversation(string conversationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(conversationId));

    /// <summary>El tablero (pipeline) se une al grupo del tenant para recolorear las tarjetas en vivo.</summary>
    public Task JoinTenant(string tenantId)
        => Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));

    public static string Group(string conversationId) => $"conv-{conversationId}";
    public static string TenantGroup(string tenantId) => $"tenant-{tenantId}";
}
