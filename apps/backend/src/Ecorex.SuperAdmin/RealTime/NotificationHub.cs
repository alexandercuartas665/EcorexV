using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Hub de notificaciones in-app (#4b). La campana del workspace se une al grupo de SU TenantUser para
/// recibir el evento "NotificationAdded" y refrescar el contador sin recargar.
/// </summary>
public sealed class NotificationHub : Hub
{
    public Task JoinUser(string tenantUserId)
        => Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantUserId));

    public Task LeaveUser(string tenantUserId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(tenantUserId));

    public static string UserGroup(string tenantUserId) => $"notif-user-{tenantUserId}";
}
