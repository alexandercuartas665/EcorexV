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
    /// <param name="connector">
    /// Fuente a consultar, con la credencial YA descifrada (ADR-0040). null = el agente usa su
    /// cadena local (opcion b, la de la Ola C).
    /// </param>
    /// <param name="correlationId">
    /// Identificador de la orden. Lo impone QUIEN DESPACHA cuando necesita conocerlo de antemano
    /// (el runner lo guarda en la bitacora ANTES de despachar: si se generara aqui, un agente rapido
    /// podria responder antes de que la corrida supiera su propio id y el resultado se perderia).
    /// null = se genera uno.
    /// </param>
    Task<string> DispatchFetchAsync(
        string clientId, Guid tenantId, Guid containerId,
        IReadOnlyDictionary<Guid, string> mapping, ApiImportMode mode, Guid? keyColumnId,
        string query, ConnectorSpec? connector, CancellationToken ct, string? correlationId = null);

    Task OnFetchResultAsync(FetchResultMsg chunk);
    Task OnFetchFailedAsync(FetchErrorMsg error);

    bool TryGetOutcome(string correlationId, out AgentIngestOutcome? outcome);

    /// <summary>
    /// Pide al agente ABORTAR el fetch en curso y libera la peticion en el servidor. Se usa cuando ya
    /// no interesa el resultado (vencio el plazo, o alguien cancelo a mano): sin esto, el agente
    /// seguiria consultando la BD y mandando chunks que se descartan. Best-effort: si el correlationId
    /// ya no esta pendiente, no hace nada. Devuelve false si no habia nada que cancelar.
    /// </summary>
    Task<bool> CancelAsync(string correlationId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Cierra las peticiones que llevan demasiado esperando y descarta resultados viejos. Lo llama el
    /// worker en cada pasada. Sin esto, un agente que se cae a mitad de un fetch deja la peticion
    /// -y todas sus filas acumuladas- en memoria PARA SIEMPRE, y su corrida se queda en "Ejecutando"
    /// eternamente, que es justo la mentira que la bitacora debe evitar.
    /// </summary>
    Task SweepAsync(CancellationToken ct = default);
}

public sealed class AgentImportService : IAgentImportService
{
    /// <summary>Cuanto se espera a que el agente termine un fetch antes de darlo por perdido. Generoso
    /// a proposito: una consulta grande con paginacion puede tardar minutos y matarla antes de tiempo
    /// seria peor que esperar.</summary>
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(10);

    /// <summary>Cuanto se conserva el resultado para que la UI lo lea. Solo lo consulta el que acaba
    /// de pulsar el boton; pasado ese rato, la verdad duradera esta en la bitacora.</summary>
    private static readonly TimeSpan OutcomeTtl = TimeSpan.FromMinutes(30);

    private readonly IHubContext<AgenteHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentImportService> _log;
    private readonly TimeProvider _clock;

    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly ConcurrentDictionary<string, (AgentIngestOutcome Outcome, DateTimeOffset At)> _outcomes = new();

    public AgentImportService(IHubContext<AgenteHub> hub, IServiceScopeFactory scopeFactory,
        ILogger<AgentImportService> log, TimeProvider? clock = null)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _log = log;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<string> DispatchFetchAsync(
        string clientId, Guid tenantId, Guid containerId,
        IReadOnlyDictionary<Guid, string> mapping, ApiImportMode mode, Guid? keyColumnId,
        string query, ConnectorSpec? connector, CancellationToken ct, string? correlationId = null)
    {
        var corr = correlationId ?? NewCorrelationId();
        // Se guarda el clientId: es a QUE agente hay que mandarle el Cancel si esta peticion se vence
        // o se aborta. Sin esto, el servidor sabria que cancelar pero no a quien decirselo.
        _pending[corr] = new Pending(clientId, tenantId, containerId, mapping, mode, keyColumnId,
            _clock.GetUtcNow() + PendingTtl);

        var req = new FetchRequestMsg(
            CorrelationId: corr,
            TenantId: tenantId.ToString(),
            // La fuente la manda quien despacha (ADR-0040): host/base/usuario + la credencial
            // descifrada del conector. Antes esto iba fijo a "SqlServer" sin credencial porque el
            // agente usaba su cadena local (opcion b). Si llega null, el agente cae a esa cadena
            // local: asi un agente configurado a mano sigue funcionando.
            Connector: connector ?? new ConnectorSpec("Database", DbEngine: "SqlServer"),
            Query: new QuerySpec(query),
            Paging: new PagingSpec("Offset", 500, 100000));

        await _hub.Clients.Group(AgenteHub.ClientGroup(clientId)).SendAsync(AgentHubMethods.FetchRequest, req, ct);
        _log.LogInformation("[INGESTA] dispatch corr={Corr} client={Client} container={Container} mode={Mode}",
            corr, clientId, containerId, mode);
        return corr;
    }

    public static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..8];

    public async Task OnFetchResultAsync(FetchResultMsg chunk)
    {
        if (!_pending.TryGetValue(chunk.CorrelationId, out var p))
        {
            return; // no es una peticion de ingesta (o ya cerrada).
        }

        // Un chunk repetido (reintento del agente, reconexion del hub) duplicaria filas en silencio:
        // la ingesta no puede distinguir "otra vez la fila 1" de "otra fila igual". Se descarta por
        // indice, que es el unico dato que identifica al chunk dentro de su orden.
        lock (p.Gate)
        {
            if (!p.SeenChunks.Add(chunk.ChunkIndex))
            {
                _log.LogWarning("[INGESTA] corr={Corr} chunk {Idx} repetido: se descarta",
                    chunk.CorrelationId, chunk.ChunkIndex);
                return;
            }
            p.Rows.AddRange(chunk.Rows);
        }

        if (!chunk.IsLast) { return; }

        if (!_pending.TryRemove(chunk.CorrelationId, out _))
        {
            return; // otro hilo ya lo cerro (IsLast duplicado).
        }

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
                Remember(chunk.CorrelationId, outcome);
                _log.LogInformation("[INGESTA] corr={Corr} OK ins={Ins} upd={Upd} del={Del}",
                    chunk.CorrelationId, outcome.Inserted, outcome.Updated, outcome.Deleted);

                var runLog = scope.ServiceProvider.GetRequiredService<IImportRunLog>();
                await runLog.CloseAsync(chunk.CorrelationId, true, outcome.Inserted, outcome.Updated,
                    outcome.Deleted, $"{outcome.Inserted} insertadas, {outcome.Deleted} reemplazadas");
            }
        }
        catch (Exception ex)
        {
            Remember(chunk.CorrelationId, new AgentIngestOutcome(false, 0, 0, 0, ex.Message));
            _log.LogError(ex, "[INGESTA] corr={Corr} fallo la ingesta", chunk.CorrelationId);
            await CloseRunAsync(p.TenantId, chunk.CorrelationId, false, ex.Message);
        }
    }

    public async Task OnFetchFailedAsync(FetchErrorMsg error)
    {
        var tenantId = _pending.TryRemove(error.CorrelationId, out var p) ? p.TenantId : (Guid?)null;
        Remember(error.CorrelationId, new AgentIngestOutcome(false, 0, 0, 0, $"{error.Code}: {error.Message}"));
        _log.LogWarning("[INGESTA] corr={Corr} el agente reporto fallo: {Code} {Msg}",
            error.CorrelationId, error.Code, error.Message);

        if (tenantId is Guid t)
        {
            await CloseRunAsync(t, error.CorrelationId, false, $"{error.Code}: {error.Message}");
        }
    }

    public bool TryGetOutcome(string correlationId, out AgentIngestOutcome? outcome)
    {
        var ok = _outcomes.TryGetValue(correlationId, out var entry);
        outcome = ok ? entry.Outcome : null;
        return ok;
    }

    public async Task SweepAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();

        foreach (var (corr, p) in _pending)
        {
            if (p.DeadlineUtc > now) { continue; }
            if (!_pending.TryRemove(corr, out _)) { continue; }

            var detail = $"El agente no completo la consulta en {PendingTtl.TotalMinutes:0} minutos.";
            Remember(corr, new AgentIngestOutcome(false, 0, 0, 0, detail));
            _log.LogWarning("[INGESTA] corr={Corr} vencio el plazo; se descarta la peticion", corr);
            // Ademas de soltar la peticion aqui, se le dice al AGENTE que pare: sin esto seguiria
            // consultando la BD y mandando chunks que ya nadie acepta. Best-effort.
            await PushCancelAsync(p.ClientId, corr, "timeout", ct);
            await CloseRunAsync(p.TenantId, corr, false, detail);
            if (ct.IsCancellationRequested) { return; }
        }

        foreach (var (corr, entry) in _outcomes)
        {
            if (now - entry.At > OutcomeTtl) { _outcomes.TryRemove(corr, out _); }
        }
    }

    public async Task<bool> CancelAsync(string correlationId, string reason, CancellationToken ct = default)
    {
        if (!_pending.TryRemove(correlationId, out var p))
        {
            return false; // ya termino, ya se cancelo, o nunca existio: nada que hacer.
        }

        await PushCancelAsync(p.ClientId, correlationId, reason, ct);
        var detail = $"Cancelado: {reason}";
        Remember(correlationId, new AgentIngestOutcome(false, 0, 0, 0, detail));
        await CloseRunAsync(p.TenantId, correlationId, false, detail);
        _log.LogInformation("[INGESTA] corr={Corr} cancelado ({Reason})", correlationId, reason);
        return true;
    }

    /// <summary>Empuja un <c>Cancel</c> al agente. Best-effort: si el agente ya no esta, no pasa nada
    /// (la peticion ya se solto en el servidor de todos modos).</summary>
    private async Task PushCancelAsync(string clientId, string correlationId, string? reason, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(AgenteHub.ClientGroup(clientId))
                .SendAsync(AgentHubMethods.Cancel, new CancelMsg(correlationId, reason), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[INGESTA] corr={Corr} no se pudo empujar el Cancel al agente", correlationId);
        }
    }

    private void Remember(string correlationId, AgentIngestOutcome outcome) =>
        _outcomes[correlationId] = (outcome, _clock.GetUtcNow());

    /// <summary>Cierra la corrida en un scope propio: este servicio es singleton y la bitacora es
    /// scoped (necesita DbContext), y ademas hay que fijar el tenant a mano porque aqui no hay
    /// peticion HTTP de la que sacarlo.</summary>
    private async Task CloseRunAsync(Guid tenantId, string correlationId, bool ok, string? detail)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            using (AmbientTenantContext.Begin(tenantId))
            {
                var runLog = scope.ServiceProvider.GetRequiredService<IImportRunLog>();
                await runLog.CloseAsync(correlationId, ok, 0, 0, 0, detail);
            }
        }
        catch (Exception ex)
        {
            // Que falle la bitacora no debe tumbar el canal.
            _log.LogError(ex, "[INGESTA] corr={Corr} no se pudo cerrar la corrida en la bitacora", correlationId);
        }
    }

    private sealed class Pending
    {
        public Pending(string clientId, Guid tenantId, Guid containerId, IReadOnlyDictionary<Guid, string> mapping,
            ApiImportMode mode, Guid? keyColumnId, DateTimeOffset deadlineUtc)
        {
            ClientId = clientId;
            TenantId = tenantId;
            ContainerId = containerId;
            Mapping = mapping;
            Mode = mode;
            KeyColumnId = keyColumnId;
            DeadlineUtc = deadlineUtc;
        }

        /// <summary>A que agente se le mando la orden (para poder mandarle el Cancel).</summary>
        public string ClientId { get; }
        public Guid TenantId { get; }
        public Guid ContainerId { get; }
        public IReadOnlyDictionary<Guid, string> Mapping { get; }
        public ApiImportMode Mode { get; }
        public Guid? KeyColumnId { get; }

        /// <summary>Cuando se da por perdida. Sin esto la entrada -y sus filas- viven para siempre.</summary>
        public DateTimeOffset DeadlineUtc { get; }

        /// <summary>Los chunks llegan por el hub y pueden repetirse; este es el filtro.</summary>
        public HashSet<int> SeenChunks { get; } = new();

        /// <summary>SeenChunks y Rows se tocan juntos desde varias invocaciones del hub.</summary>
        public object Gate { get; } = new();

        public List<IReadOnlyDictionary<string, string?>> Rows { get; } = new();
    }
}
