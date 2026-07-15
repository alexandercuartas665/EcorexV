using System.Collections.Concurrent;
using Ecorex.Application.DataContainers;
using Ecorex.Contracts.Agent;
using Ecorex.SuperAdmin.Auth;
using Ecorex.SuperAdmin.RealTime;
using Microsoft.AspNetCore.SignalR;

namespace Ecorex.SuperAdmin.Agents;

public sealed record AgentIngestOutcome(bool Ok, int Inserted, int Updated, int Deleted, string? Error);

/// <summary>
/// Orquestador de ingesta via agente (doc 03 s6). Arma y empuja un <c>FetchRequest</c> hacia el
/// agente y, cuando llegan los <c>FetchResult</c> (por el hub), acumula los chunks y en el ultimo
/// ingiere las filas al contenedor destino reusando <see cref="IRowIngestService"/> (el mismo motor
/// del import REST). Singleton: mantiene el estado de las peticiones pendientes entre invocaciones
/// del hub; la ingesta corre en un scope propio con el tenant fijado (la peticion recuerda su tenant).
/// </summary>
public interface IAgentImportService
{
    Task<string> DispatchFetchAsync(
        string clientId, Guid tenantId, Guid containerId,
        IReadOnlyDictionary<Guid, string> mapping, ApiImportMode mode, Guid? keyColumnId,
        string query, CancellationToken ct);

    Task OnFetchResultAsync(FetchResultMsg chunk);
    Task OnFetchFailedAsync(FetchErrorMsg error);

    bool TryGetOutcome(string correlationId, out AgentIngestOutcome? outcome);
}

public sealed class AgentImportService : IAgentImportService
{
    private readonly IHubContext<AgenteHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentImportService> _log;

    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly ConcurrentDictionary<string, AgentIngestOutcome> _outcomes = new();

    public AgentImportService(IHubContext<AgenteHub> hub, IServiceScopeFactory scopeFactory, ILogger<AgentImportService> log)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public async Task<string> DispatchFetchAsync(
        string clientId, Guid tenantId, Guid containerId,
        IReadOnlyDictionary<Guid, string> mapping, ApiImportMode mode, Guid? keyColumnId,
        string query, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        _pending[correlationId] = new Pending(tenantId, containerId, mapping, mode, keyColumnId);

        var req = new FetchRequestMsg(
            CorrelationId: correlationId,
            TenantId: tenantId.ToString(),
            Connector: new ConnectorSpec("Database", DbEngine: "SqlServer"),
            Query: new QuerySpec(query),
            Paging: new PagingSpec("Offset", 500, 100000));

        await _hub.Clients.Group(AgenteHub.ClientGroup(clientId)).SendAsync(AgentHubMethods.FetchRequest, req, ct);
        _log.LogInformation("[INGESTA] dispatch corr={Corr} client={Client} container={Container} mode={Mode}",
            correlationId, clientId, containerId, mode);
        return correlationId;
    }

    public async Task OnFetchResultAsync(FetchResultMsg chunk)
    {
        if (!_pending.TryGetValue(chunk.CorrelationId, out var p))
        {
            return; // no es una peticion de ingesta (o ya cerrada).
        }

        p.Rows.AddRange(chunk.Rows);

        if (!chunk.IsLast) { return; }

        _pending.TryRemove(chunk.CorrelationId, out _);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using (AmbientTenantContext.Begin(p.TenantId))
            {
                var ingest = scope.ServiceProvider.GetRequiredService<IRowIngestService>();
                var session = ingest.CreateSession(p.ContainerId, p.TenantId, p.Mapping, p.Mode, p.KeyColumnId);
                await session.PrepareAsync(CancellationToken.None);
                await session.IngestChunkAsync(p.Rows, CancellationToken.None);
                var outcome = new AgentIngestOutcome(true, session.Inserted, session.Updated, session.Deleted, null);
                _outcomes[chunk.CorrelationId] = outcome;
                _log.LogInformation("[INGESTA] corr={Corr} OK ins={Ins} upd={Upd} del={Del}",
                    chunk.CorrelationId, outcome.Inserted, outcome.Updated, outcome.Deleted);
            }
        }
        catch (Exception ex)
        {
            _outcomes[chunk.CorrelationId] = new AgentIngestOutcome(false, 0, 0, 0, ex.Message);
            _log.LogError(ex, "[INGESTA] corr={Corr} fallo la ingesta", chunk.CorrelationId);
        }
    }

    public Task OnFetchFailedAsync(FetchErrorMsg error)
    {
        _pending.TryRemove(error.CorrelationId, out _);
        _outcomes[error.CorrelationId] = new AgentIngestOutcome(false, 0, 0, 0, $"{error.Code}: {error.Message}");
        _log.LogWarning("[INGESTA] corr={Corr} el agente reporto fallo: {Code} {Msg}",
            error.CorrelationId, error.Code, error.Message);
        return Task.CompletedTask;
    }

    public bool TryGetOutcome(string correlationId, out AgentIngestOutcome? outcome)
    {
        var ok = _outcomes.TryGetValue(correlationId, out var o);
        outcome = o;
        return ok;
    }

    private sealed class Pending
    {
        public Pending(Guid tenantId, Guid containerId, IReadOnlyDictionary<Guid, string> mapping, ApiImportMode mode, Guid? keyColumnId)
        {
            TenantId = tenantId;
            ContainerId = containerId;
            Mapping = mapping;
            Mode = mode;
            KeyColumnId = keyColumnId;
        }

        public Guid TenantId { get; }
        public Guid ContainerId { get; }
        public IReadOnlyDictionary<Guid, string> Mapping { get; }
        public ApiImportMode Mode { get; }
        public Guid? KeyColumnId { get; }
        public List<IReadOnlyDictionary<string, string?>> Rows { get; } = new();
    }
}
