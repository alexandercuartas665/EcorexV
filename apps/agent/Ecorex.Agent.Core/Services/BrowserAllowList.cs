namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Allow-list LOCAL de dominios que el sub-agente Navegador puede visitar (doc 06 s4: nada fuera de
/// la lista, aunque la nube lo pida). Un dominio por linea; la persistencia cifrada la gobierna
/// <see cref="AgentVault"/> (ADR-0039). La coincidencia es por sufijo de host (ej. "example.com"
/// permite "www.example.com"). Si la lista esta VACIA o no se puede leer, se bloquea todo (fail-closed).
/// </summary>
public sealed class BrowserAllowList
{
    private const string FileName = "browser-allow.dat";

    public IReadOnlyList<string> Load()
    {
        var plain = AgentVault.ReadText(FileName);
        if (string.IsNullOrEmpty(plain)) { return Array.Empty<string>(); }
        return plain.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant()).ToArray();
    }

    public void Save(IEnumerable<string> domains)
    {
        var text = string.Join('\n', domains.Select(d => d.Trim().ToLowerInvariant()).Where(d => d.Length > 0).Distinct());
        AgentVault.WriteText(FileName, text);
    }

    /// <summary>true si el host (o un sufijo de dominio) esta permitido. Fail-closed si la lista esta vacia.</summary>
    public bool IsAllowed(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) { return false; }
        host = host.ToLowerInvariant();
        foreach (var d in Load())
        {
            if (host == d || host.EndsWith("." + d, StringComparison.Ordinal)) { return true; }
        }
        return false;
    }
}
