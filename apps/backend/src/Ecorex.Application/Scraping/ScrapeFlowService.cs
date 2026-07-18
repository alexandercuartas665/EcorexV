using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Scraping;

/// <summary>
/// CRUD de flujos de extraccion por navegador (modulo 000730, capitulo "Extraccion de Datos", Ola 1).
/// Solo configuracion (el runtime es diferido). Tenant-scoped por el filtro global. Las variables
/// secretas se cifran con <see cref="ISecretProtector"/> y NUNCA se devuelven en claro.
/// </summary>
public sealed class ScrapeFlowService : IScrapeFlowService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _protector;

    public ScrapeFlowService(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector protector)
    {
        _db = db;
        _tenantContext = tenantContext;
        _protector = protector;
    }

    public async Task<IReadOnlyList<ScrapeFlowSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var flows = await _db.ScrapeFlows.AsNoTracking().OrderBy(f => f.Name).ToListAsync(ct);
        if (flows.Count == 0) { return Array.Empty<ScrapeFlowSummaryDto>(); }

        // Conteo de pasos y nombres del cliente/contenedor sin traerse las colecciones enteras.
        var flowIds = flows.Select(f => f.Id).ToList();
        var stepCounts = await _db.ScrapeSteps.AsNoTracking()
            .Where(s => flowIds.Contains(s.FlowId))
            .GroupBy(s => s.FlowId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var clientIds = flows.Where(f => f.ClientId is not null).Select(f => f.ClientId!.Value).Distinct().ToList();
        var containerIds = flows.Where(f => f.ContainerId is not null).Select(f => f.ContainerId!.Value).Distinct().ToList();
        var clientNames = clientIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.DataClients.AsNoTracking().Where(c => clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var containerNames = containerIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.DataContainers.AsNoTracking().Where(c => containerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return flows.Select(f => new ScrapeFlowSummaryDto(
            f.Id, f.Name, f.Status,
            stepCounts.TryGetValue(f.Id, out var n) ? n : 0,
            f.ClientId is Guid cl && clientNames.TryGetValue(cl, out var cn) ? cn : null,
            f.ContainerId is Guid co && containerNames.TryGetValue(co, out var kn) ? kn : null,
            f.LastRunAt, f.LastResultSummary)).ToList();
    }

    public async Task<IReadOnlyList<ScrapeTargetDto>> ListContainersAsync(CancellationToken ct = default)
    {
        // Tabla + su modelo, para etiquetar "Modelo / Tabla" sin que el operador tenga que adivinar de
        // que contenedor es cada tabla. Join en memoria (son pocas por tenant).
        var containers = await _db.DataContainers.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.ModelId }).ToListAsync(ct);
        if (containers.Count == 0) { return Array.Empty<ScrapeTargetDto>(); }
        var modelIds = containers.Where(c => c.ModelId != null).Select(c => c.ModelId!.Value).Distinct().ToList();
        var modelNames = modelIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.DataModels.AsNoTracking().Where(m => modelIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        return containers
            .Select(c => new ScrapeTargetDto(c.Id,
                (c.ModelId is Guid m && modelNames.TryGetValue(m, out var mn) ? mn + " / " : "") + c.Name))
            .OrderBy(t => t.Label)
            .ToList();
    }

    public async Task<ScrapeFlowDto?> GetAsync(Guid flowId, CancellationToken ct = default)
    {
        var flow = await _db.ScrapeFlows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is null) { return null; }

        var steps = await _db.ScrapeSteps.AsNoTracking()
            .Where(s => s.FlowId == flowId).OrderBy(s => s.Order).ToListAsync(ct);
        var vars = await _db.ScrapeVariables.AsNoTracking()
            .Where(v => v.FlowId == flowId).OrderBy(v => v.Name).ToListAsync(ct);

        string? clientName = flow.ClientId is Guid cl
            ? await _db.DataClients.AsNoTracking().Where(c => c.Id == cl).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        string? containerName = flow.ContainerId is Guid co
            ? await _db.DataContainers.AsNoTracking().Where(c => c.Id == co).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;

        return new ScrapeFlowDto(
            flow.Id, flow.Name, flow.Description, flow.StartUrl, flow.Status,
            flow.ClientId, clientName, flow.ContainerId, containerName,
            flow.LastRunAt, flow.LastResultSummary,
            steps.Select(MapStep).ToList(),
            vars.Select(MapVariable).ToList());
    }

    public async Task<ScrapeFlowDto?> SaveFlowAsync(SaveScrapeFlowRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { throw new InvalidOperationException("El nombre es obligatorio."); }
        if (string.IsNullOrWhiteSpace(req.StartUrl)) { throw new InvalidOperationException("La URL de arranque es obligatoria."); }

        // Nombre unico por tenant (el indice unico es la defensa en profundidad; aqui damos el mensaje).
        var clash = await _db.ScrapeFlows
            .AnyAsync(f => f.Name == name && (req.Id == null || f.Id != req.Id), ct);
        if (clash) { throw new InvalidOperationException($"Ya existe un flujo llamado '{name}'."); }

        ScrapeFlow entity;
        if (req.Id is { } id)
        {
            var existing = await _db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new ScrapeFlow { TenantId = tenantId };
            _db.ScrapeFlows.Add(entity);
        }

        entity.Name = name;
        entity.Description = NullIfBlank(req.Description);
        entity.StartUrl = req.StartUrl.Trim();
        entity.Status = req.Status;
        entity.ClientId = req.ClientId;
        entity.ContainerId = req.ContainerId;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(entity.Id, ct);
    }

    public async Task<bool> DeleteFlowAsync(Guid flowId, Guid actorUserId, CancellationToken ct = default)
    {
        var flow = await _db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is null) { return false; }
        // Pasos y variables caen por cascada de la BD; se quita el maestro.
        _db.ScrapeFlows.Remove(flow);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Pasos ----

    public async Task<ScrapeStepDto?> SaveStepAsync(SaveScrapeStepRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        if (string.IsNullOrWhiteSpace(req.Name)) { throw new InvalidOperationException("El nombre del paso es obligatorio."); }
        if (!await _db.ScrapeFlows.AnyAsync(f => f.Id == req.FlowId, ct)) { return null; }

        ScrapeStep entity;
        if (req.Id is { } id)
        {
            var existing = await _db.ScrapeSteps.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new ScrapeStep { TenantId = tenantId, FlowId = req.FlowId };
            _db.ScrapeSteps.Add(entity);
        }

        entity.FlowId = req.FlowId;
        entity.Order = req.Order;
        entity.Kind = req.Kind;
        entity.Name = req.Name.Trim();
        entity.WaitMs = req.WaitMs;
        // Se guarda cada campo tal cual; que campos aplican a cada Kind lo valida la UI y, al ejecutar,
        // el runtime. No se limpian los demas: un cambio de Kind conserva lo escrito por si se vuelve.
        entity.Url = NullIfBlank(req.Url);
        entity.Script = NullIfBlank(req.Script);
        entity.Selector = NullIfBlank(req.Selector);
        entity.MappingJson = NullIfBlank(req.MappingJson);
        entity.Instruction = NullIfBlank(req.Instruction);
        entity.TargetContainerId = req.TargetContainerId;
        entity.ToolAllowListJson = NullIfBlank(req.ToolAllowListJson);
        entity.MaxSteps = req.MaxSteps;
        entity.MaxSeconds = req.MaxSeconds;
        entity.AiProviderId = req.AiProviderId;
        entity.AiModel = NullIfBlank(req.AiModel);

        await _db.SaveChangesAsync(ct);
        return MapStep(entity);
    }

    public async Task<bool> DeleteStepAsync(Guid stepId, Guid actorUserId, CancellationToken ct = default)
    {
        var step = await _db.ScrapeSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step is null) { return false; }
        _db.ScrapeSteps.Remove(step);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReorderStepsAsync(Guid flowId, IReadOnlyList<Guid> orderedStepIds, Guid actorUserId, CancellationToken ct = default)
    {
        var steps = await _db.ScrapeSteps.Where(s => s.FlowId == flowId).ToListAsync(ct);
        if (steps.Count == 0) { return false; }
        var byId = steps.ToDictionary(s => s.Id);
        var order = 0;
        foreach (var id in orderedStepIds)
        {
            if (byId.TryGetValue(id, out var s)) { s.Order = order++; }
        }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Variables ----

    public async Task<ScrapeVariableDto?> SaveVariableAsync(SaveScrapeVariableRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { throw new InvalidOperationException("El nombre de la variable es obligatorio."); }
        if (!await _db.ScrapeFlows.AnyAsync(f => f.Id == req.FlowId, ct)) { return null; }

        var clash = await _db.ScrapeVariables
            .AnyAsync(v => v.FlowId == req.FlowId && v.Name == name && (req.Id == null || v.Id != req.Id), ct);
        if (clash) { throw new InvalidOperationException($"Ya existe una variable '{name}' en este flujo."); }

        ScrapeVariable entity;
        if (req.Id is { } id)
        {
            var existing = await _db.ScrapeVariables.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            entity = new ScrapeVariable { TenantId = tenantId, FlowId = req.FlowId };
            _db.ScrapeVariables.Add(entity);
        }

        entity.FlowId = req.FlowId;
        entity.Name = name;
        entity.IsSecret = req.IsSecret;
        // El valor: si viene, se cifra (secreto) o se guarda tal cual (no secreto). Si es edicion y NO
        // viene, se conserva el existente -no se puede consultar un secreto, pero si reescribirlo-.
        if (!string.IsNullOrEmpty(req.Value))
        {
            entity.ValueEncrypted = req.IsSecret ? _protector.Protect(req.Value) : req.Value;
        }

        await _db.SaveChangesAsync(ct);
        return MapVariable(entity);
    }

    public async Task<bool> DeleteVariableAsync(Guid variableId, Guid actorUserId, CancellationToken ct = default)
    {
        var v = await _db.ScrapeVariables.FirstOrDefaultAsync(x => x.Id == variableId, ct);
        if (v is null) { return false; }
        _db.ScrapeVariables.Remove(v);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Helpers ----

    private static ScrapeStepDto MapStep(ScrapeStep s) => new(
        s.Id, s.FlowId, s.Order, s.Kind, s.Name, s.WaitMs, s.Url, s.Script, s.Selector, s.MappingJson,
        s.Instruction, s.TargetContainerId, s.ToolAllowListJson, s.MaxSteps, s.MaxSeconds, s.AiProviderId, s.AiModel);

    private static ScrapeVariableDto MapVariable(ScrapeVariable v) =>
        new(v.Id, v.FlowId, v.Name, !string.IsNullOrEmpty(v.ValueEncrypted), v.IsSecret);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
