using System.Collections.Concurrent;
using Ecorex.Application.DataContainers;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Almacen en memoria de las corridas de importacion lanzadas por la API de configuracion (FASE 1).
/// El importador server-direct (<see cref="IApiImportService"/>) es SINCRONO: la corrida se ejecuta
/// dentro del POST /run y su resultado se guarda aqui bajo un runId para que GET /runs/{id} lo consulte.
/// Singleton por proceso; el estado no sobrevive a un reinicio (aceptable en FASE 1: la persistencia
/// de corridas programadas ya vive en ImportRun). Acotado para no crecer sin limite.
/// </summary>
public sealed class ConfigRunStore
{
    private const int MaxEntries = 500;
    private readonly ConcurrentDictionary<Guid, RunState> _runs = new();
    private readonly ConcurrentQueue<Guid> _order = new();

    public Guid Start(Guid tenantId, Guid connectorId)
    {
        var id = Guid.CreateVersion7();
        _runs[id] = new RunState(id, tenantId, connectorId, "running", DateTimeOffset.UtcNow, null, null, null);
        _order.Enqueue(id);
        while (_order.Count > MaxEntries && _order.TryDequeue(out var old)) { _runs.TryRemove(old, out _); }
        return id;
    }

    public void Complete(Guid id, ApiImportOutcome outcome)
    {
        if (_runs.TryGetValue(id, out var s))
        {
            _runs[id] = s with { Status = outcome.Success ? "ok" : "error", FinishedAt = DateTimeOffset.UtcNow, Outcome = outcome };
        }
    }

    public void Fail(Guid id, string error)
    {
        if (_runs.TryGetValue(id, out var s))
        {
            _runs[id] = s with { Status = "error", FinishedAt = DateTimeOffset.UtcNow, Error = error };
        }
    }

    /// <summary>Estado de la corrida, SOLO si pertenece al tenant dado (aislamiento tambien en memoria).</summary>
    public RunState? Get(Guid id, Guid tenantId)
        => _runs.TryGetValue(id, out var s) && s.TenantId == tenantId ? s : null;

    public sealed record RunState(
        Guid RunId,
        Guid TenantId,
        Guid ConnectorId,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt,
        ApiImportOutcome? Outcome,
        string? Error);
}
