using System.Text.Json;
using Ecorex.Contracts.Agent;

namespace Ecorex.Agent.Core.Services;

/// <summary>
/// Persistencia local del ClientId/URL/secreto. El cifrado y la ubicacion son asunto de
/// <see cref="AgentVault"/> (ADR-0039: %ProgramData%, DPAPI de MAQUINA, ACL SYSTEM+Admins), no de
/// este store: antes cada store repetia el P/Invoke a DPAPI y la ruta. Nunca en el repo ni en claro.
/// </summary>
public sealed class DpapiConfigStore : IAgentConfigStore
{
    private const string FileName = "config.dat";

    public AgentConfig Load()
    {
        var json = AgentVault.ReadText(FileName);
        if (string.IsNullOrEmpty(json)) { return AgentConfig.Empty; }
        try
        {
            return JsonSerializer.Deserialize<AgentConfig>(json) ?? AgentConfig.Empty;
        }
        catch
        {
            return AgentConfig.Empty; // corrupto: arranca sin config
        }
    }

    public void Save(AgentConfig config) => AgentVault.WriteText(FileName, JsonSerializer.Serialize(config));

    public void Clear() => AgentVault.Delete(FileName);
}
