using Ecorex.Contracts.Agent;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.Agent.HubSim;

/// <summary>
/// Hub simulado en la ruta <see cref="AgentProtocol.HubRoute"/>. Implementa el lado SERVIDOR del
/// protocolo (doc 02 s3): recibe del agente <c>AgentHello</c>/<c>FetchResult</c>/<c>FetchFailed</c>/
/// <c>Heartbeat</c> y los registra. Anonimo (sin auth) a proposito: es un simulador de dev.
/// </summary>
public sealed class AgenteHub : Hub
{
    private readonly ILogger<AgenteHub> _log;

    public AgenteHub(ILogger<AgenteHub> log) => _log = log;

    public override Task OnConnectedAsync()
    {
        _log.LogInformation("[HUB] Agente CONECTADO: {Conn}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _log.LogInformation("[HUB] Agente DESCONECTADO: {Conn}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task AgentHello(AgentHelloMsg msg)
    {
        _log.LogInformation("[HUB] AgentHello: client={Client} v={Ver} host={Host} os={Os} caps=[{Caps}]",
            msg.ClientId, msg.AgentVersion, msg.Host, msg.Os, string.Join(", ", msg.Capabilities));
        return Task.CompletedTask;
    }

    public Task FetchResult(FetchResultMsg msg)
    {
        var status = msg.Rows.Count > 0 && msg.Rows[0].TryGetValue("_status", out var s) ? s : null;
        _log.LogInformation("[HUB] FetchResult: corr={Corr} rows={Rows} last={Last} status={Status}",
            msg.CorrelationId, msg.RowCount, msg.IsLast, status);
        return Task.CompletedTask;
    }

    public Task FetchFailed(FetchErrorMsg msg)
    {
        _log.LogWarning("[HUB] FetchFailed: corr={Corr} code={Code} msg={Msg} retryable={Retry}",
            msg.CorrelationId, msg.Code, msg.Message, msg.Retryable);
        return Task.CompletedTask;
    }

    public Task Heartbeat()
    {
        _log.LogDebug("[HUB] Heartbeat de {Conn}", Context.ConnectionId);
        return Task.CompletedTask;
    }
}
