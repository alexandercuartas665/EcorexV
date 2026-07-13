using Ecorex.Application.Tenancy;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Implementacion SignalR de <see cref="IFormRecordBroadcaster"/> (ola F4): emite el evento
/// "FormRecord" al grupo del tenant (reusa <see cref="TaskHub"/> y su grupo por tenant), para que
/// la bandeja /m/{code} se refresque en vivo cuando entra un registro.
/// </summary>
public sealed class SignalRFormRecordBroadcaster : IFormRecordBroadcaster
{
    private readonly IHubContext<TaskHub> _hub;

    public SignalRFormRecordBroadcaster(IHubContext<TaskHub> hub)
    {
        _hub = hub;
    }

    public Task RecordConfirmedAsync(Guid tenantId, string formCode, string recordNumber, CancellationToken cancellationToken = default)
        => _hub.Clients.Group(TaskHub.TenantGroup(tenantId.ToString()))
            .SendAsync("FormRecord", formCode, recordNumber, cancellationToken);
}
