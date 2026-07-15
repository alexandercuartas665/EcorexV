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
    private readonly IAgentImportService _imports;
    private readonly ILogger<AgenteHub> _log;

    public AgenteHub(IAgentRegistry registry, IAgentImportService imports, ILogger<AgenteHub> log)
    {
        _registry = registry;
        _imports = imports;
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

    public async Task FetchResult(FetchResultMsg msg)
    {
        _registry.Touch(Context.ConnectionId);
        var fields = msg.Fields is null ? string.Empty : string.Join(", ", msg.Fields);
        var sample = msg.Rows.Count > 0 ? string.Join(" | ", msg.Rows[0].Select(kv => $"{kv.Key}={kv.Value}")) : string.Empty;
        _log.LogInformation("[AGENTE] FetchResult: corr={Corr} rows={Rows} last={Last} cols=[{Cols}] row0={Sample}",
            msg.CorrelationId, msg.RowCount, msg.IsLast, fields, sample);
        // Ola 3 (doc 03 s6): si el correlationId corresponde a una peticion de ingesta, acumula y en
        // el ultimo chunk ingiere las filas al contenedor destino (reusando IRowIngestService).
        await _imports.OnFetchResultAsync(msg);
    }

    public async Task FetchFailed(FetchErrorMsg msg)
    {
        _registry.Touch(Context.ConnectionId);
        _log.LogWarning("[AGENTE] FetchFailed: corr={Corr} code={Code} msg={Msg}",
            msg.CorrelationId, msg.Code, msg.Message);
        await _imports.OnFetchFailedAsync(msg);
    }

    public Task Heartbeat()
    {
        _registry.Touch(Context.ConnectionId);
        return Task.CompletedTask;
    }

    public static string ClientGroup(string clientId) => $"client:{clientId}";
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";
}
