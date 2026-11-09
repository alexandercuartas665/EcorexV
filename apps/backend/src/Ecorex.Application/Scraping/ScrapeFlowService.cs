using System.Text.Json;
using System.Text.Json.Serialization;
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

    public async Task<IReadOnlyList<ScrapeRuleOptionDto>> ListRulesAsync(CancellationToken ct = default)
    {
        // Reglas del tenant (filtro global por tenant) etiquetadas "Documento / Regla (verbo)".
        var rules = await _db.Rules.AsNoTracking()
            .Select(r => new { r.Id, r.Name, r.VerbName, r.DocumentId }).ToListAsync(ct);
        if (rules.Count == 0) { return Array.Empty<ScrapeRuleOptionDto>(); }
        var docIds = rules.Select(r => r.DocumentId).Distinct().ToList();
        var docNames = await _db.RuleDocuments.AsNoTracking().Where(d => docIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return rules
            .Select(r => new ScrapeRuleOptionDto(r.Id,
                (docNames.TryGetValue(r.DocumentId, out var dn) ? dn + " / " : "") + r.Name + " (" + r.VerbName + ")"))
            .OrderBy(x => x.Label)
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
        entity.OutputVar = NullIfBlank(req.OutputVar);
        entity.LoopOverVar = NullIfBlank(req.LoopOverVar);
        entity.IsLoopEnd = req.IsLoopEnd;
        entity.IngestPath = NullIfBlank(req.IngestPath);
        entity.IngestKeyColumn = NullIfBlank(req.IngestKeyColumn);
        entity.PageCountVar = NullIfBlank(req.PageCountVar);
        entity.IsPageNext = req.IsPageNext;
        entity.RuleId = req.RuleId;
        entity.RuleInputVar = NullIfBlank(req.RuleInputVar);

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

    // ---- Portabilidad de pasos (export/import JSON): mueve SOLO los pasos entre entornos ----

    public async Task<ScrapeStepsExportDto?> ExportStepsAsync(Guid flowId, CancellationToken ct = default)
    {
        var flow = await _db.ScrapeFlows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is null) { return null; }

        var steps = await _db.ScrapeSteps.AsNoTracking()
            .Where(s => s.FlowId == flowId).OrderBy(s => s.Order).ToListAsync(ct);

        var portable = steps.Select(s => new ScrapeStepPortable(
            s.Order, s.Kind, s.Name, s.WaitMs, s.Url, s.Script, s.Selector, s.MappingJson, s.Instruction,
            s.TargetContainerId, s.ToolAllowListJson, s.MaxSteps, s.MaxSeconds, s.AiProviderId, s.AiModel,
            s.WarningLabel, s.WarningAction, s.OutputVar, s.LoopOverVar, s.IsLoopEnd, s.IngestPath,
            s.IngestKeyColumn, s.PageCountVar, s.IsPageNext, s.RuleId, s.RuleInputVar)).ToList();

        var pkg = new ScrapeStepsPackage(StepsSchemaVersion, flow.Name, DateTimeOffset.UtcNow, portable);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pkg, PortableJson);
        var file = $"pasos-{Slug(flow.Name)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
        return new ScrapeStepsExportDto(file, bytes);
    }

    public async Task<ScrapeStepsImportResultDto> ImportStepsAsync(Guid flowId, string json, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return new ScrapeStepsImportResultDto(false, 0, 0, Array.Empty<string>(), "No hay tenant activo.");
        }
        var flow = await _db.ScrapeFlows.FirstOrDefaultAsync(f => f.Id == flowId, ct);
        if (flow is null)
        {
            return new ScrapeStepsImportResultDto(false, 0, 0, Array.Empty<string>(), "El flujo destino no existe.");
        }

        ScrapeStepsPackage? pkg;
        try
        {
            pkg = JsonSerializer.Deserialize<ScrapeStepsPackage>(json, PortableJson);
        }
        catch (JsonException ex)
        {
            return new ScrapeStepsImportResultDto(false, 0, 0, Array.Empty<string>(),
                "El archivo no es un paquete de pasos valido: " + ex.Message);
        }
        if (pkg?.Steps is null)
        {
            return new ScrapeStepsImportResultDto(false, 0, 0, Array.Empty<string>(), "El archivo no contiene pasos.");
        }
        if (pkg.SchemaVersion > StepsSchemaVersion)
        {
            return new ScrapeStepsImportResultDto(false, 0, 0, Array.Empty<string>(),
                $"El paquete es de una version mas nueva (v{pkg.SchemaVersion}); actualiza el sistema para importarlo.");
        }

        // Validar las referencias por Guid contra el tenant destino (filtro global). Las que no resuelven
        // (vienen de otro entorno) se dejan en null y se reportan para re-enlazar en la UI.
        var wantContainers = pkg.Steps.Where(s => s.TargetContainerId is Guid).Select(s => s.TargetContainerId!.Value).Distinct().ToList();
        var wantProviders = pkg.Steps.Where(s => s.AiProviderId is Guid).Select(s => s.AiProviderId!.Value).Distinct().ToList();
        var wantRules = pkg.Steps.Where(s => s.RuleId is Guid).Select(s => s.RuleId!.Value).Distinct().ToList();
        var okContainers = wantContainers.Count == 0 ? new HashSet<Guid>()
            : (await _db.DataContainers.AsNoTracking().Where(c => wantContainers.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct)).ToHashSet();
        var okProviders = wantProviders.Count == 0 ? new HashSet<Guid>()
            : (await _db.AiProviderConfigs.AsNoTracking().Where(p => wantProviders.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var okRules = wantRules.Count == 0 ? new HashSet<Guid>()
            : (await _db.Rules.AsNoTracking().Where(r => wantRules.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct)).ToHashSet();

        var warnings = new List<string>();

        // Reemplazo total: se quitan los pasos actuales y se crean los del paquete. Un unico SaveChanges
        // envuelve borrado + insercion en una sola transaccion (EF ordena DELETE antes que INSERT).
        var current = await _db.ScrapeSteps.Where(s => s.FlowId == flowId).ToListAsync(ct);
        var replaced = current.Count;
        if (current.Count > 0) { _db.ScrapeSteps.RemoveRange(current); }

        var order = 0;
        foreach (var p in pkg.Steps.OrderBy(s => s.Order))
        {
            var name = string.IsNullOrWhiteSpace(p.Name) ? $"Paso {order + 1}" : p.Name.Trim();

            Guid? container = p.TargetContainerId;
            if (container is Guid cc && !okContainers.Contains(cc))
            {
                container = null;
                warnings.Add($"Paso '{name}': el contenedor destino no existe en este entorno; quedo sin asignar.");
            }
            Guid? provider = p.AiProviderId;
            if (provider is Guid pp && !okProviders.Contains(pp))
            {
                provider = null;
                warnings.Add($"Paso '{name}': el proveedor IA no existe en este entorno; quedo sin asignar.");
            }
            Guid? rule = p.RuleId;
            if (rule is Guid rr && !okRules.Contains(rr))
            {
                rule = null;
                warnings.Add($"Paso '{name}': la regla no existe en este entorno; quedo sin asignar.");
            }

            _db.ScrapeSteps.Add(new ScrapeStep
            {
                TenantId = tenantId,
                FlowId = flowId,
                Order = order++,
                Kind = p.Kind,
                Name = name,
                WaitMs = p.WaitMs,
                Url = NullIfBlank(p.Url),
                Script = NullIfBlank(p.Script),
                Selector = NullIfBlank(p.Selector),
                MappingJson = NullIfBlank(p.MappingJson),
                Instruction = NullIfBlank(p.Instruction),
                TargetContainerId = container,
                ToolAllowListJson = NullIfBlank(p.ToolAllowListJson),
                MaxSteps = p.MaxSteps,
                MaxSeconds = p.MaxSeconds,
                AiProviderId = provider,
                AiModel = NullIfBlank(p.AiModel),
                WarningLabel = NullIfBlank(p.WarningLabel),
                WarningAction = p.WarningAction,
                OutputVar = NullIfBlank(p.OutputVar),
                LoopOverVar = NullIfBlank(p.LoopOverVar),
                IsLoopEnd = p.IsLoopEnd,
                IngestPath = NullIfBlank(p.IngestPath),
                IngestKeyColumn = NullIfBlank(p.IngestKeyColumn),
                PageCountVar = NullIfBlank(p.PageCountVar),
                IsPageNext = p.IsPageNext,
                RuleId = rule,
                RuleInputVar = NullIfBlank(p.RuleInputVar),
            });
        }

        await _db.SaveChangesAsync(ct);
        return new ScrapeStepsImportResultDto(true, order, replaced, warnings, null);
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

    public async Task<ScrapeSecretRevealDto?> RevealVariableSecretAsync(Guid variableId, CancellationToken ct = default)
    {
        // Tenant-scoped por el filtro global. El valor en claro solo sale aqui, bajo demanda.
        var v = await _db.ScrapeVariables.AsNoTracking().FirstOrDefaultAsync(x => x.Id == variableId, ct);
        if (v is null) { return null; }
        if (string.IsNullOrEmpty(v.ValueEncrypted)) { return new ScrapeSecretRevealDto(true, "", null); }
        if (!v.IsSecret) { return new ScrapeSecretRevealDto(true, v.ValueEncrypted, null); }
        try
        {
            return new ScrapeSecretRevealDto(true, _protector.Unprotect(v.ValueEncrypted), null);
        }
        catch
        {
            // Secreta cifrada con la llave de OTRO entorno (DataProtection es por entorno): no desencripta.
            return new ScrapeSecretRevealDto(false, null,
                "No se pudo descifrar: la secreta parece cifrada con la llave de otro entorno. Reingresa el valor aqui.");
        }
    }

    // ---- Helpers ----

    private static ScrapeStepDto MapStep(ScrapeStep s) => new(
        s.Id, s.FlowId, s.Order, s.Kind, s.Name, s.WaitMs, s.Url, s.Script, s.Selector, s.MappingJson,
        s.Instruction, s.TargetContainerId, s.ToolAllowListJson, s.MaxSteps, s.MaxSeconds, s.AiProviderId, s.AiModel,
        s.WarningLabel, s.WarningAction, s.OutputVar, s.LoopOverVar, s.IsLoopEnd, s.IngestPath,
        s.IngestKeyColumn, s.PageCountVar, s.IsPageNext, s.RuleId, s.RuleInputVar);

    // El valor de las NO secretas se guarda en texto plano (ver SaveVariableAsync), asi que se puede
    // mostrar tal cual. Las secretas van cifradas: aqui NO se devuelven, se piden con RevealVariableSecretAsync.
    private static ScrapeVariableDto MapVariable(ScrapeVariable v) =>
        new(v.Id, v.FlowId, v.Name, !string.IsNullOrEmpty(v.ValueEncrypted), v.IsSecret,
            v.IsSecret ? null : v.ValueEncrypted);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---- Portabilidad: version del esquema, opciones JSON y slug para el nombre de archivo ----

    private const int StepsSchemaVersion = 1;

    // Enums como texto (resisten reordenamientos), indentado para que el JSON sea legible/editable.
    private static readonly JsonSerializerOptions PortableJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Slug(string s)
    {
        var slug = new string((s ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--")) { slug = slug.Replace("--", "-"); }
        slug = slug.Trim('-');
        if (string.IsNullOrEmpty(slug)) { return "flujo"; }
        return slug.Length > 40 ? slug[..40].Trim('-') : slug;
    }
}
