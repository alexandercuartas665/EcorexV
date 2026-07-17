using System.Collections.Concurrent;

namespace Ecorex.SuperAdmin.Agents;

/// <summary>
/// Cache breve de nonces del handshake para evitar replays (doc 02 s2). Como el <c>ts</c> se valida
/// dentro de +/-120s, basta con recordar los nonces ~5 min. En memoria (v1, una instancia).
/// </summary>
public sealed class AgentNonceCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();

    /// <summary>Marca el nonce como usado. Devuelve false si ya se habia visto (replay).</summary>
    public bool TryUse(string nonce)
    {
        var now = DateTimeOffset.UtcNow;
        Prune(now);
        return _seen.TryAdd(nonce, now.Add(Ttl));
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var kv in _seen)
        {
            if (kv.Value <= now) { _seen.TryRemove(kv.Key, out _); }
        }
    }
}
