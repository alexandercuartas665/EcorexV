using System.Text;
using CubotTravels.Application.Admin;
using CubotTravels.Application.Common;
using CubotTravels.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CubotTravels.Application.Tenancy;

public sealed class AiInferenceService : IAiInferenceService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAiProviderClient _client;

    public AiInferenceService(IApplicationDbContext db, ISecretProtector secretProtector, IAiProviderClient client)
    {
        _db = db;
        _secretProtector = secretProtector;
        _client = client;
    }

    public async Task<AiChatResult> TestChatAsync(Guid agentId, IReadOnlyList<AiChatTurn> turns, string? systemPromptOverride = null, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return new AiChatResult(false, null, "El agente no existe."); }

        // La cuenta del proveedor (API key, modelo, base url) la define el Super Admin (config global).
        var providerCfg = await _db.AiProviderConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Provider == agent.Provider, cancellationToken);
        if (providerCfg is null || !providerCfg.IsEnabled || string.IsNullOrWhiteSpace(providerCfg.ApiKeyEncrypted))
        {
            return new AiChatResult(false, null, $"El proveedor {agent.Provider} no esta habilitado en la plataforma.");
        }

        string apiKey;
        try { apiKey = _secretProtector.Unprotect(providerCfg.ApiKeyEncrypted); }
        catch { return new AiChatResult(false, null, "La API key del proveedor esta cifrada con una version anterior. Vuelve a guardarla en Servidores de IA."); }

        var meta = AiProviderCatalog.For(agent.Provider);
        var model = !string.IsNullOrWhiteSpace(agent.Model) ? agent.Model!
            : !string.IsNullOrWhiteSpace(providerCfg.Model) ? providerCfg.Model!
            : meta.DefaultModel;

        var systemPrompt = BuildSystemPrompt(agentId, systemPromptOverride ?? agent.SystemPrompt, cancellationToken);

        if (turns.Count == 0) { return new AiChatResult(false, null, "Escribe un mensaje para probar el agente."); }

        return await _client.CompleteAsync(agent.Provider, apiKey, providerCfg.BaseUrl, model, await systemPrompt, turns, cancellationToken);
    }

    // Anexa al prompt los recursos de texto del agente como contexto utilizable.
    private async Task<string> BuildSystemPrompt(Guid agentId, string basePrompt, CancellationToken ct)
    {
        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == agentId && r.ResourceType == AgentResourceType.Text && r.Detail != null)
            .OrderBy(r => r.SortOrder)
            .Select(r => new { r.Name, r.Detail })
            .ToListAsync(ct);

        if (resources.Count == 0) { return basePrompt; }

        var sb = new StringBuilder(basePrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Recursos disponibles para responder:");
        foreach (var r in resources)
        {
            sb.AppendLine($"- {r.Name}: {r.Detail}");
        }
        return sb.ToString();
    }
}
