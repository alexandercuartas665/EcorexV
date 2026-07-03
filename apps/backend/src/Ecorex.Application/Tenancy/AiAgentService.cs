using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class AiAgentService : IAiAgentService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public AiAgentService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AiAgentDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var agents = await _db.AiAgents.AsNoTracking()
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(cancellationToken);
        var counts = await _db.AiAgentResources.AsNoTracking()
            .GroupBy(r => r.AgentId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return agents.Select(a => Map(a, counts.TryGetValue(a.Id, out var c) ? c : 0)).ToList();
    }

    public async Task<AiAgentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return null; }
        var resources = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.AgentId == id)
            .OrderBy(r => r.SortOrder)
            .Select(r => MapResource(r))
            .ToListAsync(cancellationToken);
        var prompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == id)
            .OrderBy(p => p.SortOrder)
            .Select(p => MapPrompt(p))
            .ToListAsync(cancellationToken);
        return new AiAgentDetailDto(Map(agent, resources.Count), resources, prompts);
    }

    public async Task<AiAgentDto?> CreateAsync(CreateAiAgentRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var nextOrder = (await _db.AiAgents.Select(a => (int?)a.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var agent = new AiAgent
        {
            TenantId = tenantId,
            Name = (request.Name ?? "Agente").Trim(),
            Role = request.Role?.Trim(),
            Provider = request.Provider,
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
            SystemPrompt = request.SystemPrompt ?? "",
            IsActive = false,
            SortOrder = nextOrder,
            DisabledToolsJson = SerializeTools(request.DisabledTools)
        };
        _db.AiAgents.Add(agent);
        _audit.Write(actorUserId, "ai-agent.create", nameof(AiAgent), agent.Id,
            previousValue: null, newValue: new { agent.Name, agent.Provider }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(agent, 0);
    }

    public async Task<AiAgentDto?> UpdateAsync(Guid id, UpdateAiAgentRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return null; }
        agent.Name = (request.Name ?? agent.Name).Trim();
        agent.Role = request.Role?.Trim();
        agent.Provider = request.Provider;
        agent.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        agent.SystemPrompt = request.SystemPrompt ?? "";
        agent.DisabledToolsJson = SerializeTools(request.DisabledTools);

        // Red de seguridad: guarda una instantanea {prompt base + enrutados} en el historial (ultimas 5).
        var snapshotPrompts = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.AgentId == id).OrderBy(p => p.SortOrder)
            .Select(p => new AgentPromptSnapshotDto(p.Name, p.Rule, p.Body, p.SortOrder))
            .ToListAsync(cancellationToken);
        agent.PromptHistoryJson = PushPromptVersion(agent.PromptHistoryJson,
            new StoredPromptVersion(DateTimeOffset.UtcNow, agent.SystemPrompt, snapshotPrompts));

        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.AiAgentResources.CountAsync(r => r.AgentId == id, cancellationToken);
        return Map(agent, count);
    }

    public async Task<AiAgentDto?> SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return null; }
        agent.IsActive = active;
        _audit.Write(actorUserId, active ? "ai-agent.activate" : "ai-agent.deactivate", nameof(AiAgent), agent.Id,
            previousValue: null, newValue: new { active }, tenantId: agent.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.AiAgentResources.CountAsync(r => r.AgentId == id, cancellationToken);
        return Map(agent, count);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (agent is null) { return false; }
        _db.AiAgents.Remove(agent);
        _audit.Write(actorUserId, "ai-agent.delete", nameof(AiAgent), agent.Id,
            previousValue: new { agent.Name }, newValue: null, tenantId: agent.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AiAgentResourceDto?> AddResourceAsync(CreateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, cancellationToken);
        if (agent is null) { return null; }
        var nextOrder = (await _db.AiAgentResources.Where(r => r.AgentId == request.AgentId).Select(r => (int?)r.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var res = new AiAgentResource
        {
            TenantId = tenantId,
            AgentId = request.AgentId,
            Name = (request.Name ?? "Recurso").Trim(),
            ResourceType = request.ResourceType,
            Detail = request.Detail,
            FileUrl = request.FileUrl,
            FileName = request.FileName,
            SortOrder = nextOrder
        };
        _db.AiAgentResources.Add(res);
        await _db.SaveChangesAsync(cancellationToken);
        return MapResource(res);
    }

    public async Task<AiAgentResourceDto?> UpdateResourceAsync(Guid id, UpdateAgentResourceRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var res = await _db.AiAgentResources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (res is null) { return null; }
        res.Name = (request.Name ?? res.Name).Trim();
        res.ResourceType = request.ResourceType;
        res.Detail = request.Detail;
        res.FileUrl = request.FileUrl;
        res.FileName = request.FileName;
        await _db.SaveChangesAsync(cancellationToken);
        return MapResource(res);
    }

    public async Task<bool> DeleteResourceAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var res = await _db.AiAgentResources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (res is null) { return false; }
        _db.AiAgentResources.Remove(res);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AiAgentPromptDto?> AddPromptAsync(CreateAgentPromptRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == request.AgentId, cancellationToken);
        if (agent is null) { return null; }
        var nextOrder = (await _db.AiAgentPrompts.Where(p => p.AgentId == request.AgentId).Select(p => (int?)p.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;
        var prompt = new AiAgentPrompt
        {
            TenantId = tenantId,
            AgentId = request.AgentId,
            Name = (request.Name ?? "Prompt").Trim(),
            Rule = string.IsNullOrWhiteSpace(request.Rule) ? null : request.Rule.Trim(),
            Body = request.Body ?? "",
            SortOrder = nextOrder
        };
        _db.AiAgentPrompts.Add(prompt);
        await _db.SaveChangesAsync(cancellationToken);
        return MapPrompt(prompt);
    }

    public async Task<AiAgentPromptDto?> UpdatePromptAsync(Guid id, UpdateAgentPromptRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.AiAgentPrompts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (prompt is null) { return null; }
        prompt.Name = (request.Name ?? prompt.Name).Trim();
        prompt.Rule = string.IsNullOrWhiteSpace(request.Rule) ? null : request.Rule.Trim();
        prompt.Body = request.Body ?? "";
        await _db.SaveChangesAsync(cancellationToken);
        return MapPrompt(prompt);
    }

    public async Task<bool> DeletePromptAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var prompt = await _db.AiAgentPrompts.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (prompt is null) { return false; }
        _db.AiAgentPrompts.Remove(prompt);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ===== Duplicar agente =====
    public async Task<AiAgentDto?> DuplicateAsync(Guid sourceId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var src = await _db.AiAgents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == sourceId, cancellationToken);
        if (src is null) { return null; }
        var nextOrder = (await _db.AiAgents.Select(a => (int?)a.SortOrder).MaxAsync(cancellationToken) ?? -1) + 1;

        // Agente nuevo: copia la config base + herramientas + historial. Queda APAGADO y SIN linea vinculada.
        var copy = new AiAgent
        {
            TenantId = tenantId,
            Name = $"Copia de {src.Name}",
            Role = src.Role,
            Provider = src.Provider,
            Model = src.Model,
            SystemPrompt = src.SystemPrompt,
            IsActive = false,
            SortOrder = nextOrder,
            DisabledToolsJson = src.DisabledToolsJson,
            PromptHistoryJson = src.PromptHistoryJson
        };
        _db.AiAgents.Add(copy);
        await _db.SaveChangesAsync(cancellationToken); // asegura copy.Id para los hijos
        var newId = copy.Id;

        foreach (var p in await _db.AiAgentPrompts.AsNoTracking().Where(p => p.AgentId == sourceId).ToListAsync(cancellationToken))
        {
            _db.AiAgentPrompts.Add(new AiAgentPrompt { TenantId = tenantId, AgentId = newId, Name = p.Name, Rule = p.Rule, Body = p.Body, SortOrder = p.SortOrder });
        }
        foreach (var r in await _db.AiAgentResources.AsNoTracking().Where(r => r.AgentId == sourceId).ToListAsync(cancellationToken))
        {
            _db.AiAgentResources.Add(new AiAgentResource { TenantId = tenantId, AgentId = newId, Name = r.Name, ResourceType = r.ResourceType, Detail = r.Detail, FileUrl = r.FileUrl, FileName = r.FileName, SortOrder = r.SortOrder });
        }
        foreach (var f in await _db.AiAgentCacheFields.AsNoTracking().Where(f => f.AgentId == sourceId).ToListAsync(cancellationToken))
        {
            _db.AiAgentCacheFields.Add(new AiAgentCacheField { TenantId = tenantId, AgentId = newId, FieldKey = f.FieldKey, Label = f.Label, Description = f.Description, SortOrder = f.SortOrder, IsUpdatable = f.IsUpdatable });
        }
        // NO se copian: AiAgentLineBindings (linea de atencion) ni AiAgentCacheValues (datos de conversacion).
        _audit.Write(actorUserId, "ai-agent.duplicate", nameof(AiAgent), newId,
            previousValue: new { source = sourceId }, newValue: new { copy.Name }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        var count = await _db.AiAgentResources.CountAsync(r => r.AgentId == newId, cancellationToken);
        return Map(copy, count);
    }

    // ===== Versiones de prompts =====
    private const int MaxPromptVersions = 5;
    private sealed record StoredPromptVersion(DateTimeOffset SavedAt, string BasePrompt, List<AgentPromptSnapshotDto> Prompts);

    public async Task<IReadOnlyList<AiAgentPromptVersionDto>> GetPromptHistoryAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var json = await _db.AiAgents.AsNoTracking().Where(a => a.Id == agentId).Select(a => a.PromptHistoryJson).FirstOrDefaultAsync(cancellationToken);
        return DeserializeVersions(json)
            .Select((v, i) => new AiAgentPromptVersionDto(i, v.SavedAt, v.BasePrompt ?? "", v.Prompts ?? new List<AgentPromptSnapshotDto>()))
            .ToList();
    }

    public async Task<IReadOnlyList<AgentPromptVersionEntryDto>> GetPromptVersionsAsync(Guid agentId, string promptName, CancellationToken cancellationToken = default)
    {
        var json = await _db.AiAgents.AsNoTracking().Where(a => a.Id == agentId).Select(a => a.PromptHistoryJson).FirstOrDefaultAsync(cancellationToken);
        var name = (promptName ?? string.Empty).Trim();
        var result = new List<AgentPromptVersionEntryDto>();
        var idx = 0;
        foreach (var v in DeserializeVersions(json))
        {
            var match = (v.Prompts ?? new List<AgentPromptSnapshotDto>())
                .FirstOrDefault(p => string.Equals((p.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                result.Add(new AgentPromptVersionEntryDto(idx, v.SavedAt, match.Rule, match.Body ?? string.Empty));
                idx++;
            }
        }
        return result;
    }

    public async Task<AiAgentDetailDto?> RestorePromptVersionAsync(Guid agentId, int versionIndex, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var agent = await _db.AiAgents.FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
        if (agent is null) { return null; }
        var list = DeserializeVersions(agent.PromptHistoryJson);
        if (versionIndex < 0 || versionIndex >= list.Count) { return null; }
        var version = list[versionIndex];

        agent.SystemPrompt = version.BasePrompt ?? "";
        var current = await _db.AiAgentPrompts.Where(p => p.AgentId == agentId).ToListAsync(cancellationToken);
        _db.AiAgentPrompts.RemoveRange(current);
        var order = 0;
        foreach (var p in (version.Prompts ?? new List<AgentPromptSnapshotDto>()).OrderBy(p => p.SortOrder))
        {
            _db.AiAgentPrompts.Add(new AiAgentPrompt
            {
                TenantId = agent.TenantId,
                AgentId = agentId,
                Name = string.IsNullOrWhiteSpace(p.Name) ? "Prompt" : p.Name,
                Rule = string.IsNullOrWhiteSpace(p.Rule) ? null : p.Rule,
                Body = p.Body ?? "",
                SortOrder = order++
            });
        }
        // Registra la restauracion como nueva instantanea (asi el indice 0 = estado vivo).
        agent.PromptHistoryJson = PushPromptVersion(agent.PromptHistoryJson,
            new StoredPromptVersion(DateTimeOffset.UtcNow, agent.SystemPrompt, (version.Prompts ?? new List<AgentPromptSnapshotDto>()).ToList()));
        _audit.Write(actorUserId, "ai-agent.restore-prompt-version", nameof(AiAgent), agent.Id,
            previousValue: null, newValue: new { versionIndex, version.SavedAt }, tenantId: agent.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(agentId, cancellationToken);
    }

    private static string PushPromptVersion(string? existingJson, StoredPromptVersion version)
    {
        var list = DeserializeVersions(existingJson);
        if (list.Count > 0 && SameContent(list[0], version) && existingJson is not null) { return existingJson; }
        list.Insert(0, version);
        if (list.Count > MaxPromptVersions) { list = list.Take(MaxPromptVersions).ToList(); }
        return JsonSerializer.Serialize(list);
    }

    private static List<StoredPromptVersion> DeserializeVersions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new List<StoredPromptVersion>(); }
        try { return JsonSerializer.Deserialize<List<StoredPromptVersion>>(json) ?? new List<StoredPromptVersion>(); }
        catch { return new List<StoredPromptVersion>(); }
    }

    private static bool SameContent(StoredPromptVersion a, StoredPromptVersion b)
    {
        if (!string.Equals(a.BasePrompt ?? "", b.BasePrompt ?? "", StringComparison.Ordinal)) { return false; }
        var pa = a.Prompts ?? new List<AgentPromptSnapshotDto>();
        var pb = b.Prompts ?? new List<AgentPromptSnapshotDto>();
        if (pa.Count != pb.Count) { return false; }
        for (var i = 0; i < pa.Count; i++)
        {
            if (pa[i].Name != pb[i].Name || (pa[i].Rule ?? "") != (pb[i].Rule ?? "") || (pa[i].Body ?? "") != (pb[i].Body ?? "")) { return false; }
        }
        return true;
    }

    private static AiAgentDto Map(AiAgent a, int resourceCount) =>
        new(a.Id, a.Name, a.Role, a.Provider, a.Model, a.SystemPrompt, a.IsActive, a.SortOrder, resourceCount, ParseTools(a.DisabledToolsJson));

    // Serializacion de la lista de herramientas deshabilitadas del agente (jsonb).
    private static string? SerializeTools(IReadOnlyList<string>? tools)
    {
        var clean = (tools ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
        return clean.Count == 0 ? null : JsonSerializer.Serialize(clean);
    }

    private static IReadOnlyList<string> ParseTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return Array.Empty<string>(); }
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static AiAgentResourceDto MapResource(AiAgentResource r) =>
        new(r.Id, r.AgentId, r.Name, r.ResourceType, r.Detail, r.FileUrl, r.FileName, r.SortOrder);

    private static AiAgentPromptDto MapPrompt(AiAgentPrompt p) =>
        new(p.Id, p.AgentId, p.Name, p.Rule, p.Body, p.SortOrder);
}
