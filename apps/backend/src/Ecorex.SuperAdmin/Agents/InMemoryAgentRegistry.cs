using System.Collections.Concurrent;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Implementacion en memoria de <see cref="IAgentRegistry"/> (v1, una instancia). Indexa por
/// connectionId (fuente de verdad del ciclo de vida SignalR) y resuelve por clientId tomando la
/// conexion mas reciente. Thread-safe.
/// </summary>
public sealed class InMemoryAgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentPresence> _byConnection = new();

    public void MarkOnline(string clientId, Guid tenantId, string connectionId)
    {
        var now = DateTimeOffset.UtcNow;
        _byConnection[connectionId] = new AgentPresence(clientId, tenantId, connectionId, null, null, now, now);
    }

    public void MarkOffline(string connectionId) => _byConnection.TryRemove(connectionId, out _);

    public void Hello(string connectionId, string? host, string? version)
    {
        if (_byConnection.TryGetValue(connectionId, out var p))
        {
            _byConnection[connectionId] = p with { Host = host, Version = version, LastSeen = DateTimeOffset.UtcNow };
        }
    }

    public void Touch(string connectionId)
    {
        if (_byConnection.TryGetValue(connectionId, out var p))
        {
            _byConnection[connectionId] = p with { LastSeen = DateTimeOffset.UtcNow };
        }
    }

    public bool IsOnline(string clientId) =>
        _byConnection.Values.Any(p => p.ClientId == clientId);

    public AgentPresence? Get(string clientId) =>
        _byConnection.Values.Where(p => p.ClientId == clientId).OrderByDescending(p => p.LastSeen).FirstOrDefault();

    public IReadOnlyCollection<AgentPresence> ForTenant(Guid tenantId) =>
        _byConnection.Values.Where(p => p.TenantId == tenantId).ToArray();
}
