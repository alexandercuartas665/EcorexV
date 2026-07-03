using Ecorex.Application.Common;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.SuperAdmin.Seeders;

/// <summary>
/// Sembrador del agente "TravelFans - Asistente Comercial".
/// Lee el blueprint <see cref="TravelFansAgentBlueprint"/> y crea o actualiza el agente.
/// Estrategia: SystemPrompt y prompts enrutados (por nombre) se sobreescriben cada vez para
/// que las correcciones del guion se propaguen al agente activo. Recursos y campos cache se
/// agregan solo si faltan (idempotente, no se borran cargas existentes ni se sobreescriben).
/// </summary>
public sealed class TravelFansAgentSeeder
{
    private readonly IApplicationDbContext _db;
    private readonly IAiAgentService _agents;
    private readonly IAiAgentCacheService _cache;
    private readonly ITenantContext _tenantContext;

    public TravelFansAgentSeeder(IApplicationDbContext db, IAiAgentService agents, IAiAgentCacheService cache, ITenantContext tenantContext)
    {
        _db = db;
        _agents = agents;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    public sealed record Result(
        Guid AgentId,
        bool AgentCreated,
        bool SystemPromptUpdated,
        int PromptsAdded,
        int PromptsUpdated,
        int ResourcesAdded,
        int ResourcesSkipped,
        int CacheFieldsAdded,
        int CacheFieldsSkipped);

    public async Task<Result> SeedAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("No hay tenant activo en el contexto.");
        }

        // 1) Localizar o crear el agente por nombre.
        var agent = await _db.AiAgents
            .FirstOrDefaultAsync(a => a.Name == TravelFansAgentBlueprint.AgentName, cancellationToken);

        bool agentCreated = false;
        bool systemPromptUpdated = false;
        Guid agentId;
        if (agent is null)
        {
            // Elegimos el primer proveedor disponible (Claude por defecto si no hay otro).
            var provider = AiProvider.Claude;
            var existingProvider = await _db.AiProviderConfigs.AsNoTracking()
                .Where(p => p.IsEnabled)
                .OrderBy(p => p.Provider)
                .Select(p => (AiProvider?)p.Provider)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingProvider is not null) { provider = existingProvider.Value; }

            var created = await _agents.CreateAsync(new CreateAiAgentRequest(
                Name: TravelFansAgentBlueprint.AgentName,
                Role: TravelFansAgentBlueprint.AgentRole,
                Provider: provider,
                Model: null,
                SystemPrompt: TravelFansAgentBlueprint.SystemPrompt
            ), actorUserId, cancellationToken);

            if (created is null) { throw new InvalidOperationException("No se pudo crear el agente."); }
            agentId = created.Id;
            agentCreated = true;
            systemPromptUpdated = true;
        }
        else
        {
            agentId = agent.Id;
            // Actualizar el SystemPrompt si cambio respecto del blueprint (asi corregimos el guion sin recrear el agente).
            if (!string.Equals(agent.SystemPrompt, TravelFansAgentBlueprint.SystemPrompt, StringComparison.Ordinal))
            {
                await _agents.UpdateAsync(agent.Id, new UpdateAiAgentRequest(
                    Name: agent.Name,
                    Role: agent.Role,
                    Provider: agent.Provider,
                    Model: agent.Model,
                    SystemPrompt: TravelFansAgentBlueprint.SystemPrompt
                ), actorUserId, cancellationToken);
                systemPromptUpdated = true;
            }
        }

        // 2) Prompts enrutados (por nombre): se SOBREESCRIBEN si existen, se crean si no.
        var existingPrompts = await _db.AiAgentPrompts
            .Where(p => p.AgentId == agentId)
            .ToListAsync(cancellationToken);

        int promptsAdded = 0, promptsUpdated = 0;
        foreach (var p in TravelFansAgentBlueprint.Prompts)
        {
            var existing = existingPrompts.FirstOrDefault(e => string.Equals(e.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                await _agents.AddPromptAsync(new CreateAgentPromptRequest(agentId, p.Name, p.Rule, p.Body ?? ""), actorUserId, cancellationToken);
                promptsAdded++;
            }
            else if (!string.Equals(existing.Rule ?? "", p.Rule ?? "", StringComparison.Ordinal)
                  || !string.Equals(existing.Body ?? "", p.Body ?? "", StringComparison.Ordinal))
            {
                await _agents.UpdatePromptAsync(existing.Id, new UpdateAgentPromptRequest(p.Name, p.Rule, p.Body ?? ""), actorUserId, cancellationToken);
                promptsUpdated++;
            }
        }

        // 3) Recursos (por nombre): solo se agregan los que falten. No se sobreescriben para no perder
        //    archivos que el equipo ya haya subido a un recurso existente.
        var existingResourceNames = (await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == agentId)
            .Select(r => r.Name)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int resourcesAdded = 0, resourcesSkipped = 0;
        foreach (var r in TravelFansAgentBlueprint.Resources)
        {
            if (existingResourceNames.Contains(r.Name)) { resourcesSkipped++; continue; }
            await _agents.AddResourceAsync(new CreateAgentResourceRequest(
                AgentId: agentId,
                Name: r.Name,
                ResourceType: r.ResourceType,
                Detail: r.Detail,
                FileUrl: null,
                FileName: null
            ), actorUserId, cancellationToken);
            resourcesAdded++;
        }

        // 4) Campos cache (por label): solo se agregan los que falten.
        var existingFieldLabels = (await _db.AiAgentCacheFields.AsNoTracking()
            .Where(f => f.AgentId == agentId)
            .Select(f => f.Label)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int cacheFieldsAdded = 0, cacheFieldsSkipped = 0;
        foreach (var f in TravelFansAgentBlueprint.CacheFields)
        {
            if (existingFieldLabels.Contains(f.Label)) { cacheFieldsSkipped++; continue; }
            await _cache.CreateFieldAsync(new CreateAgentCacheFieldRequest(agentId, f.Label, f.Description), actorUserId, cancellationToken);
            cacheFieldsAdded++;
        }

        return new Result(
            AgentId: agentId,
            AgentCreated: agentCreated,
            SystemPromptUpdated: systemPromptUpdated,
            PromptsAdded: promptsAdded,
            PromptsUpdated: promptsUpdated,
            ResourcesAdded: resourcesAdded,
            ResourcesSkipped: resourcesSkipped,
            CacheFieldsAdded: cacheFieldsAdded,
            CacheFieldsSkipped: cacheFieldsSkipped);
    }
}
