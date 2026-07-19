using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Scheduling;
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

    public async Task<IReadOnlyList<ScrapeFlowRunDto>> ListRunsAsync(Guid flowId, int take = 10, CancellationToken ct = default)
    {
        return await _db.ScrapeFlowRuns.AsNoTracking()
            .Where(r => r.FlowId == flowId)
            .OrderByDescending(r => r.FiredAt)
            .Take(take)
            .Select(r => new ScrapeFlowRunDto(
                r.Id, r.FiredAt, r.FinishedAt, r.Trigger, r.Result, r.StepCount, r.Inserted, r.Detail))
            .ToListAsync(ct);
    }

    public async Task<ScrapeScheduleDto?> GetScheduleAsync(Guid flowId, CancellationToken ct = default)
    {
        // Un flujo tiene a lo sumo un ImportProcess (su programacion). Si no hay, no esta programado.
        var p = await _db.ImportProcesses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FlowId == flowId, ct);
        if (p is null) { return null; }
        return new ScrapeScheduleDto(p.ScheduleKind, p.IntervalMinutes, p.CronExpression, p.IsActive, p.NextRunAt, p.DisabledReason);
    }

    public async Task<ScrapeScheduleDto> SaveScheduleAsync(SaveScrapeScheduleRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }
        var flow = await _db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == req.FlowId, ct)
            ?? throw new InvalidOperationException("El flujo no existe.");

        // Un proceso por flujo: se reusa el existente o se crea. Un proceso de flujo NO tiene modelo ni
        // conector (esos son del Contenedor); solo FlowId + la regla de tiempo.
        var entity = await _db.ImportProcesses.FirstOrDefaultAsync(x => x.FlowId == req.FlowId, ct);
        if (entity is null)
        {
            entity = new ImportProcess { TenantId = tenantId, FlowId = req.FlowId };
            _db.ImportProcesses.Add(entity);
        }
        entity.Name = $"Flujo: {flow.Name}";
        entity.ScheduleKind = req.Kind;
        entity.IntervalMinutes = req.IntervalMinutes;
        entity.CronExpression = string.IsNullOrWhiteSpace(req.CronExpression) ? null : req.CronExpression!.Trim();
        entity.IsActive = req.IsActive;

        // La proxima ventana se calcula AL GUARDAR (el operador ve cuando corre) y un cron invalido se
        // rechaza aqui, no mas tarde en silencio. Mismo criterio que la programacion del Contenedor.
        var tz = ScheduledJobRecurrence.ResolveTimeZone(await _db.Tenants
            .Where(t => t.Id == tenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(ct));
        var next = ImportRecurrence.ComputeNextRun(entity, DateTimeOffset.UtcNow, tz);
        if (next.Problem == ImportScheduleProblem.Invalid)
        {
            throw new InvalidOperationException(next.Reason ?? "El horario no es valido.");
        }
        entity.NextRunAt = next.NextRunAt;
        entity.DisabledReason = null;

        await _db.SaveChangesAsync(ct);
        return new ScrapeScheduleDto(entity.ScheduleKind, entity.IntervalMinutes, entity.CronExpression,
            entity.IsActive, entity.NextRunAt, entity.DisabledReason);
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
            vars.Select(MapVariable).ToList(),
            flow.PageVar, flow.PageFrom, flow.PageTo);
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
        entity.PageVar = NullIfBlank(req.PageVar);
        entity.PageFrom = req.PageFrom;
        entity.PageTo = req.PageTo;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(entity.Id, ct);
    }

    public async Task<bool> DeleteFlowAsync(Guid flowId, Guid actorUserId, CancellationToken ct = default)
    {
        var flow = await _db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is null) { return false; }
        // La programacion (ImportProcess) apunta al flujo por referencia SUAVE (sin FK): no cae por
        // cascada, hay que quitarla a mano para no dejar un proceso huerfano que el worker intentaria.
        var schedules = await _db.ImportProcesses.Where(p => p.FlowId == flowId).ToListAsync(ct);
        if (schedules.Count > 0) { _db.ImportProcesses.RemoveRange(schedules); }
        // Pasos, variables y corridas caen por cascada de la BD; se quita el maestro.
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
        entity.WarningLabel = NullIfBlank(req.WarningLabel);
        entity.WarningAction = req.WarningAction;

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
        s.Instruction, s.TargetContainerId, s.ToolAllowListJson, s.MaxSteps, s.MaxSeconds, s.AiProviderId, s.AiModel,
        s.WarningLabel, s.WarningAction);

    private static ScrapeVariableDto MapVariable(ScrapeVariable v) =>
        new(v.Id, v.FlowId, v.Name, !string.IsNullOrEmpty(v.ValueEncrypted), v.IsSecret);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
