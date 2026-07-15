using Ecorex.Contracts.Agent;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.Agent.HubSim;

/// <summary>
/// Simula al Scheduler/orquestador del backend: cada pocos segundos EMPUJA un <c>FetchRequest</c>
/// a los agentes conectados (doc 02 s6). Alterna fuentes Database (-> Gateway en la colmena) y
/// RestApi (-> Navegador) para que se vea encender distintas capacidades y crecer el panal.
/// </summary>
public sealed class FetchPump : BackgroundService
{
    private static readonly string TenantId = "11111111-1111-1111-1111-111111111111";

    private readonly IHubContext<AgenteHub> _hub;
    private readonly ILogger<FetchPump> _log;

    public FetchPump(IHubContext<AgenteHub> hub, ILogger<FetchPump> log)
    {
        _hub = hub;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Da tiempo a que el agente conecte antes del primer empuje.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch { return; }

        var n = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            n++;
            var isDb = n % 3 != 0; // 2 de cada 3 son Database; el resto RestApi.
            var correlationId = Guid.NewGuid().ToString("N")[..8];

            var req = new FetchRequestMsg(
                CorrelationId: correlationId,
                TenantId: TenantId,
                Connector: isDb
                    ? new ConnectorSpec("Database", DbEngine: "SqlServer", Host: "10.0.0.20", Port: 1433, Database: "db3dev", Username: "ecorex_ro")
                    : new ConnectorSpec("RestApi", Host: "api.interno.local"),
                Query: new QuerySpec(
                    Text: isDb
                        ? "SELECT id, name, price FROM items WHERE updated_at > @since"
                        : "GET /clientes?desde=2026-07-01",
                    Params: new Dictionary<string, string?> { ["since"] = "2026-07-01T00:00:00Z" }),
                Paging: new PagingSpec("Offset", 500, 100000));

            _log.LogInformation("[SIM] -> FetchRequest {Corr} ({Kind})", correlationId, req.Connector.Kind);
            try
            {
                await _hub.Clients.All.SendAsync(AgentHubMethods.FetchRequest, req, stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            try { await Task.Delay(TimeSpan.FromSeconds(isDb ? 3 : 4), stoppingToken); }
            catch { break; }
        }
    }
}
