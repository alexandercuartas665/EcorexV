using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>Hub del nucleo de tareas. Las paginas /actividades y el detalle de proyecto se unen
/// al grupo del tenant para refrescar las tarjetas del kanban cuando otra sesion cambia una tarea.</summary>
public sealed class TaskHub : Hub
{
    public Task JoinTenant(string tenantId)
        => Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));

    public Task LeaveTenant(string tenantId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantGroup(tenantId));

    public static string TenantGroup(string tenantId) => $"tenant-{tenantId}";
}
