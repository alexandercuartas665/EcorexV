using Ecorex.Contracts.Agent;
using Ecorex.SuperAdmin.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.RealTime;

/// <summary>
/// Hub del Agente Conector On-Prem (doc 03 s2). Autenticado con el JWT corto del agente (esquema
/// bearer "Agent", claims client_id/tenant_id). Al conectar agrupa por client/tenant y registra la
/// presencia; recibe del agente AgentHello/FetchResult/FetchFailed/Heartbeat. Empujar ordenes se hace
/// desde el servidor via IHubContext al grupo client:{clientId}.
/// </summary>
[Authorize(AuthenticationSchemes = AgentChannel.Scheme)]
public sealed class AgenteHub : Hub
{
    private readonly IAgentRegistry _registry;
    private readonly ILogger<AgenteHub> _log;

    public AgenteHub(IAgentRegistry registry, ILogger<AgenteHub> log)
    {
        _registry = registry;
        _log = log;
    }

    public override async Task OnConnectedAsync()
    {
        var clientId = Context.User?.FindFirst("client_id")?.Value;
        var tenantRaw = Context.User?.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(clientId) || !Guid.TryParse(tenantRaw, out var tenantId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ClientGroup(clientId));
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        _registry.MarkOnline(clientId, tenantId, Context.ConnectionId);
        _log.LogInformation("[AGENTE] En linea: client={Client} tenant={Tenant} conn={Conn}",
            clientId, tenantId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.MarkOffline(Context.ConnectionId);
        _log.LogInformation("[AGENTE] Offline: conn={Conn}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task AgentHello(AgentHelloMsg msg)
    {
        _registry.Hello(Context.ConnectionId, msg.Host, msg.AgentVersion);
        _log.LogInformation("[AGENTE] Hello: client={Client} v={Ver} host={Host} caps=[{Caps}]",
            msg.ClientId, msg.AgentVersion, msg.Host, string.Join(", ", msg.Capabilities));
        return Task.CompletedTask;
    }

    public Task FetchResult(FetchResultMsg msg)
    {
        _registry.Touch(Context.ConnectionId);
        // La ingesta real (Append/Replace/Upsert reusando el motor REST) es una ola posterior
        // (doc 03 s6). Por ahora se registra el cierre del canal por correlationId.
        _log.LogInformation("[AGENTE] FetchResult: corr={Corr} rows={Rows} last={Last}",
            msg.CorrelationId, msg.RowCount, msg.IsLast);
        return Task.CompletedTask;
    }

    public Task FetchFailed(FetchErrorMsg msg)
    {
        _registry.Touch(Context.ConnectionId);
        _log.LogWarning("[AGENTE] FetchFailed: corr={Corr} code={Code} msg={Msg}",
            msg.CorrelationId, msg.Code, msg.Message);
        return Task.CompletedTask;
    }

    public Task Heartbeat()
    {
        _registry.Touch(Context.ConnectionId);
        return Task.CompletedTask;
    }

    public static string ClientGroup(string clientId) => $"client:{clientId}";
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";
}
