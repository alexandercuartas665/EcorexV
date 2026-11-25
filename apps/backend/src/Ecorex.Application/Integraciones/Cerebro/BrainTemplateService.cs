using System.Text.Json;
using System.Text.RegularExpressions;
using Ecorex.Application.Common;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Integraciones.Cerebro;

public sealed record BrainTemplateSummaryDto(
    Guid Id, string Name, string? Description, string? CategoryName, string? CategoryColor, bool IsLocked,
    string TargetMode, IReadOnlyList<Guid> TargetIds, int PublishedVersion, DateTimeOffset? PublishedAt, int Notes,
    bool IsMcpGuide = false, string? McpGuideTopic = null);

public sealed record BrainTemplateNoteDto(Guid Id, string Title, string Body);

public sealed record BrainTemplateDetailDto(BrainTemplateSummaryDto Template, IReadOnlyList<BrainTemplateNoteDto> Notes);

public sealed record BrainTemplateInput(
    string Name, string? Description, string? CategoryName, string? CategoryColor, bool IsLocked, string TargetMode,
    IReadOnlyList<Guid>? TargetIds);

/// <summary>Opcion de destino (plan o empresa) para el selector de siembra.</summary>
public sealed record BrainTemplateTargetOption(Guid Id, string Name);

/// <summary>Lo que el Super Admin necesita para sembrar: el contenido versionado y las empresas destino.</summary>
public sealed record BrainTemplatePublishPlan(BrainTemplateSnapshot Snapshot, IReadOnlyList<BrainTemplateTargetOption> Tenants);

/// <summary>
/// CEREBROS PLANTILLA de PLATAFORMA (ADR-0200, ola 3). Solo PlatformOperator (la pagina lo exige) y todo AUDITADO.
/// Este servicio maneja la plantilla (metadato de plataforma, sin tenant); la SIEMBRA la orquesta el Super Admin
/// aplicando <see cref="IBrainService.ApplyTemplateAsync"/> dentro del contexto de CADA tenant, asi el filtro global
/// sigue protegiendo los datos (no hay escrituras cross-tenant con IgnoreQueryFilters).
/// </summary>
public interface IBrainTemplateService
{
    Task<IReadOnlyList<BrainTemplateSummaryDto>> ListAsync(CancellationToken ct = default);

    Task<BrainTemplateDetailDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea (<paramref name="id"/> null) o edita la plantilla.</summary>
    Task<TaskCoreResult<Guid>> SaveAsync(Guid? id, BrainTemplateInput input, Guid platformUserId, CancellationToken ct = default);

    /// <summary>Archiva la plantilla: deja de publicarse; los cerebros ya sembrados quedan como estan.</summary>
    Task<TaskCoreResult<bool>> ArchiveAsync(Guid id, Guid platformUserId, CancellationToken ct = default);

    Task<TaskCoreResult<Guid>> SaveNoteAsync(Guid templateId, Guid? noteId, string title, string body, Guid platformUserId,
        CancellationToken ct = default);

    Task<TaskCoreResult<bool>> DeleteNoteAsync(Guid templateId, Guid noteId, Guid platformUserId, CancellationToken ct = default);

    /// <summary>Importa un vault de Obsidian (.md); las notas con el mismo titulo se reemplazan. Devuelve cuantas entraron.</summary>
    Task<TaskCoreResult<int>> ImportVaultAsync(Guid templateId, byte[] zip, Guid platformUserId, CancellationToken ct = default);

    Task<IReadOnlyList<BrainTemplateTargetOption>> ListPlansAsync(CancellationToken ct = default);

    Task<IReadOnlyList<BrainTemplateTargetOption>> ListTenantsAsync(CancellationToken ct = default);

    /// <summary>
    /// Incrementa la version publicada y devuelve el contenido y las empresas destino (todas las activas, las de los
    /// planes elegidos o las elegidas). Auditado. La aplicacion por tenant la hace el Super Admin despues.
    /// </summary>
    Task<TaskCoreResult<BrainTemplatePublishPlan>> PreparePublishAsync(Guid id, Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Marca (o desmarca) la plantilla como GUIA de los MCP (ADR-0204), la que sirve guia_ecorex. Solo una activa: al
    /// marcar una se desmarca la anterior en el MISMO SaveChanges (una transaccion). Auditado.
    /// </summary>
    Task<TaskCoreResult<bool>> SetMcpGuideAsync(Guid id, bool isGuide, Guid platformUserId, CancellationToken ct = default);

    /// <summary>ADR-0211: marca (o desmarca) la plantilla como guia MCP de un TEMA ("general", "flujos",
    /// "formularios"...). A lo sumo una guia por tema: al marcar se desmarca la anterior del mismo tema.</summary>
    Task<TaskCoreResult<bool>> SetMcpGuideAsync(Guid id, bool isGuide, string? topic, Guid platformUserId, CancellationToken ct = default);
}

public sealed partial class BrainTemplateService : IBrainTemplateService
{
    private const string AuditReason = "cerebro-plantilla";

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    /// <summary>Estados de empresa que reciben siembra (las suspendidas, bloqueadas o cerrandose no).</summary>
    private static readonly TenantStatus[] SeedableStatuses =
        { TenantStatus.Trial, TenantStatus.Active, TenantStatus.PendingPayment, TenantStatus.PastDue };

    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public BrainTemplateService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<BrainTemplateSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.BrainTemplates.AsNoTracking()
            .Where(t => !t.IsArchived)
            .OrderBy(t => t.Name)
            .Select(t => new { T = t, Notes = t.Notes.Count })
            .ToListAsync(ct);
        return rows.Select(r => ToSummary(r.T, r.Notes)).ToList();
    }

    public async Task<BrainTemplateDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.BrainTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct);
        if (t is null) { return null; }
        var notes = await _db.BrainTemplateNotes.AsNoTracking()
            .Where(n => n.TemplateId == id)
            .OrderBy(n => n.Title)
            .Select(n => new BrainTemplateNoteDto(n.Id, n.Title, n.Body))
            .ToListAsync(ct);
        return new BrainTemplateDetailDto(ToSummary(t, notes.Count), notes);
    }

    public async Task<TaskCoreResult<Guid>> SaveAsync(Guid? id, BrainTemplateInput input, Guid platformUserId, CancellationToken ct = default)
    {
        var name = Regex.Replace((input.Name ?? string.Empty).Trim(), @"\s+", " ");
        if (name.Length == 0) { return TaskCoreResult<Guid>.Invalid("El nombre es obligatorio."); }
        if (name.Length > BrainService.MaxNameLength) { return TaskCoreResult<Guid>.Invalid($"El nombre admite maximo {BrainService.MaxNameLength} caracteres."); }
        var desc = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        if (desc is { Length: > BrainService.MaxDescriptionLength }) { return TaskCoreResult<Guid>.Invalid($"La descripcion admite maximo {BrainService.MaxDescriptionLength} caracteres."); }
        var category = string.IsNullOrWhiteSpace(input.CategoryName) ? null : Regex.Replace(input.CategoryName.Trim(), @"\s+", " ");
        if (category is { Length: > BrainService.MaxCategoryNameLength }) { return TaskCoreResult<Guid>.Invalid($"La categoria admite maximo {BrainService.MaxCategoryNameLength} caracteres."); }
        var color = string.IsNullOrWhiteSpace(input.CategoryColor) ? null : input.CategoryColor.Trim();
        if (color is not null && !ColorPattern().IsMatch(color)) { return TaskCoreResult<Guid>.Invalid("El color debe tener la forma #RRGGBB."); }
        if (!BrainTemplateTargets.IsValid(input.TargetMode)) { return TaskCoreResult<Guid>.Invalid("Destino invalido (all, plans o tenants)."); }
        var targets = (input.TargetIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (input.TargetMode != BrainTemplateTargets.All && targets.Count == 0)
        {
            return TaskCoreResult<Guid>.Invalid(input.TargetMode == BrainTemplateTargets.Plans ? "Elige al menos un plan." : "Elige al menos una empresa.");
        }

        BrainTemplate template;
        object? before = null;
        if (id is { } templateId)
        {
            var found = await _db.BrainTemplates.FirstOrDefaultAsync(t => t.Id == templateId && !t.IsArchived, ct);
            if (found is null) { return TaskCoreResult<Guid>.NotFound("La plantilla no existe."); }
            template = found;
            before = new { template.Name, template.IsLocked, template.TargetMode, template.TargetIdsJson };
        }
        else
        {
            template = new BrainTemplate();
            _db.BrainTemplates.Add(template);
        }
        template.Name = name;
        template.Description = desc;
        template.CategoryName = category;
        template.CategoryColor = color;
        template.IsLocked = input.IsLocked;
        template.TargetMode = input.TargetMode;
        template.TargetIdsJson = input.TargetMode == BrainTemplateTargets.All ? null : JsonSerializer.Serialize(targets);
        _audit.Write(platformUserId, id is null ? "cerebro.plantilla.crear" : "cerebro.plantilla.editar", nameof(BrainTemplate), template.Id,
            previousValue: before, newValue: new { template.Name, template.IsLocked, template.TargetMode, destinos = targets.Count },
            reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<Guid>.Ok(template.Id);
    }

    public async Task<TaskCoreResult<bool>> ArchiveAsync(Guid id, Guid platformUserId, CancellationToken ct = default)
    {
        var template = await _db.BrainTemplates.FirstOrDefaultAsync(t => t.Id == id && !t.IsArchived, ct);
        if (template is null) { return TaskCoreResult<bool>.NotFound("La plantilla no existe."); }
        template.IsArchived = true;
        _audit.Write(platformUserId, "cerebro.plantilla.archivar", nameof(BrainTemplate), template.Id, previousValue: null,
            newValue: new { template.Name }, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<Guid>> SaveNoteAsync(Guid templateId, Guid? noteId, string title, string body, Guid platformUserId,
        CancellationToken ct = default)
    {
        if (!await _db.BrainTemplates.AnyAsync(t => t.Id == templateId && !t.IsArchived, ct)) { return TaskCoreResult<Guid>.NotFound("La plantilla no existe."); }
        var (cleanTitle, titleError) = MockupBrainLinks.CleanTitle(title);
        if (titleError is not null) { return TaskCoreResult<Guid>.Invalid(titleError); }
        var cleanBody = (body ?? string.Empty).Replace("\r\n", "\n");
        if (cleanBody.Length > MockupBrainService.MaxBodyLength) { return TaskCoreResult<Guid>.Invalid($"La nota supera {MockupBrainService.MaxBodyLength:N0} caracteres."); }
        var key = MockupBrainLinks.NormalizeTitle(cleanTitle);
        if (await _db.BrainTemplateNotes.AnyAsync(n => n.TemplateId == templateId && n.TitleKey == key && n.Id != (noteId ?? Guid.Empty), ct))
        {
            return TaskCoreResult<Guid>.Conflict($"Ya hay una nota llamada '{cleanTitle}' en la plantilla.");
        }

        BrainTemplateNote note;
        if (noteId is { } nid)
        {
            var found = await _db.BrainTemplateNotes.FirstOrDefaultAsync(n => n.Id == nid && n.TemplateId == templateId, ct);
            if (found is null) { return TaskCoreResult<Guid>.NotFound("La nota no existe."); }
            note = found;
        }
        else
        {
            note = new BrainTemplateNote { TemplateId = templateId };
            _db.BrainTemplateNotes.Add(note);
        }
        note.Title = cleanTitle!;
        note.TitleKey = key;
        note.Body = cleanBody;
        _audit.Write(platformUserId, "cerebro.plantilla.nota", nameof(BrainTemplate), templateId, previousValue: null,
            newValue: new { note.Title, caracteres = cleanBody.Length }, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<Guid>.Ok(note.Id);
    }

    public async Task<TaskCoreResult<bool>> DeleteNoteAsync(Guid templateId, Guid noteId, Guid platformUserId, CancellationToken ct = default)
    {
        var note = await _db.BrainTemplateNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.TemplateId == templateId, ct);
        if (note is null) { return TaskCoreResult<bool>.NotFound("La nota no existe."); }
        // La nota es parte de la plantilla (no un agregado del tenant): al publicar, los cerebros bloqueados la archivan.
        _db.BrainTemplateNotes.Remove(note);
        _audit.Write(platformUserId, "cerebro.plantilla.nota-quitar", nameof(BrainTemplate), templateId, previousValue: new { note.Title },
            newValue: null, reason: AuditReason);
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(true);
    }

    public async Task<TaskCoreResult<int>> ImportVaultAsync(Guid templateId, byte[] zip, Guid platformUserId, CancellationToken ct = default)
    {
        if (!await _db.BrainTemplates.AnyAsync(t => t.Id == templateId && !t.IsArchived, ct)) { return TaskCoreResult<int>.NotFound("La plantilla no existe."); }
        var read = MockupBrainService.ReadVault(zip);
        if (read.Error is not null) { return TaskCoreResult<int>.Invalid(read.Error); }
        var existing = await _db.BrainTemplateNotes.Where(n => n.TemplateId == templateId).ToListAsync(ct);
        var byKey = existing.ToDictionary(n => n.TitleKey);
        var count = 0;
        foreach (var v in read.Notes)
        {
            var key = MockupBrainLinks.NormalizeTitle(v.Title);
            if (!byKey.TryGetValue(key, out var note))
            {
                note = new BrainTemplateNote { TemplateId = templateId, TitleKey = key };
                _db.BrainTemplateNotes.Add(note);
                byKey[key] = note;
            }
            note.Title = v.Title;
            note.Body = v.Body;
            count++;
        }
        _audit.Write(platformUserId, "cerebro.plantilla.importar", nameof(BrainTemplate), templateId, previousValue: null,
            newValue: new { notas = count, ignorados = read.IgnoredFiles }, reason: AuditReason);
        // Un solo SaveChanges = una transaccion (entra todo el vault o nada).
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<int>.Ok(count);
    }

    public async Task<IReadOnlyList<BrainTemplateTargetOption>> ListPlansAsync(CancellationToken ct = default)
        => await _db.SaasPlans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new BrainTemplateTargetOption(p.Id, p.Name)).ToListAsync(ct);

    public async Task<IReadOnlyList<BrainTemplateTargetOption>> ListTenantsAsync(CancellationToken ct = default)
        => await _db.Tenants.AsNoTracking().Where(t => SeedableStatuses.Contains(t.Status)).OrderBy(t => t.Name)
            .Select(t => new BrainTemplateTargetOption(t.Id, t.Name)).ToListAsync(ct);

    public async Task<TaskCoreResult<BrainTemplatePublishPlan>> PreparePublishAsync(Guid id, Guid platformUserId, CancellationToken ct = default)
    {
        var template = await _db.BrainTemplates.FirstOrDefaultAsync(t => t.Id == id && !t.IsArchived, ct);
        if (template is null) { return TaskCoreResult<BrainTemplatePublishPlan>.NotFound("La plantilla no existe."); }
        var notes = await _db.BrainTemplateNotes.AsNoTracking().Where(n => n.TemplateId == id).OrderBy(n => n.Title)
            .Select(n => new BrainTemplateSnapshotNote(n.Title, n.Body)).ToListAsync(ct);
        var ids = ParseIds(template.TargetIdsJson);
        var tenants = _db.Tenants.AsNoTracking().Where(t => SeedableStatuses.Contains(t.Status));
        tenants = template.TargetMode switch
        {
            BrainTemplateTargets.Tenants => tenants.Where(t => ids.Contains(t.Id)),
            BrainTemplateTargets.Plans => tenants.Where(t => _db.TenantSubscriptions.Any(s => s.TenantId == t.Id && ids.Contains(s.PlanId)
                && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Suspended)),
            _ => tenants
        };
        var targets = await tenants.OrderBy(t => t.Name).Select(t => new BrainTemplateTargetOption(t.Id, t.Name)).ToListAsync(ct);

        template.PublishedVersion += 1;
        template.PublishedAt = DateTimeOffset.UtcNow;
        _audit.Write(platformUserId, "cerebro.plantilla.publicar", nameof(BrainTemplate), template.Id, previousValue: null,
            newValue: new { template.Name, version = template.PublishedVersion, template.TargetMode, empresas = targets.Count, notas = notes.Count },
            reason: AuditReason);
        await _db.SaveChangesAsync(ct);

        var snapshot = new BrainTemplateSnapshot(template.Id, template.Name, template.Description, template.CategoryName, template.CategoryColor,
            template.IsLocked, template.PublishedVersion, notes);
        return TaskCoreResult<BrainTemplatePublishPlan>.Ok(new BrainTemplatePublishPlan(snapshot, targets));
    }

    public Task<TaskCoreResult<bool>> SetMcpGuideAsync(Guid id, bool isGuide, Guid platformUserId, CancellationToken ct = default)
        => SetMcpGuideAsync(id, isGuide, McpGuideTopics.General, platformUserId, ct);

    public async Task<TaskCoreResult<bool>> SetMcpGuideAsync(Guid id, bool isGuide, string? topic, Guid platformUserId, CancellationToken ct = default)
    {
        var template = await _db.BrainTemplates.FirstOrDefaultAsync(t => t.Id == id && !t.IsArchived, ct);
        if (template is null) { return TaskCoreResult<bool>.NotFound("La plantilla no existe."); }
        var key = McpGuideTopics.Normalize(topic);
        if (key is null) { return TaskCoreResult<bool>.Invalid("Tema de guia no valido (minusculas, letras, numeros y guion bajo)."); }
        var stored = key == McpGuideTopics.General ? null : key;
        if (template.IsMcpGuide == isGuide && (!isGuide || McpGuideTopics.Normalize(template.McpGuideTopic) == key))
        {
            return TaskCoreResult<bool>.Ok(isGuide);
        }

        string? previous = null;
        if (isGuide)
        {
            // Una guia por TEMA: se desmarca la anterior del mismo tema (la de otros temas sigue).
            var candidates = await _db.BrainTemplates.Where(t => t.IsMcpGuide && t.Id != id).ToListAsync(ct);
            var others = candidates.Where(o => McpGuideTopics.Normalize(o.McpGuideTopic) == key).ToList();
            foreach (var o in others) { o.IsMcpGuide = false; }
            previous = others.Count > 0 ? string.Join(", ", others.Select(o => o.Name)) : null;
        }
        template.IsMcpGuide = isGuide;
        template.McpGuideTopic = isGuide ? stored : template.McpGuideTopic;
        _audit.Write(platformUserId, "cerebro.plantilla.guia-mcp", nameof(BrainTemplate), template.Id,
            previousValue: new { guia_anterior = previous }, newValue: new { template.Name, es_guia = isGuide, tema = key }, reason: AuditReason);
        // Un solo SaveChanges = una transaccion: nunca quedan dos guias marcadas.
        await _db.SaveChangesAsync(ct);
        return TaskCoreResult<bool>.Ok(isGuide);
    }

    private static BrainTemplateSummaryDto ToSummary(BrainTemplate t, int notes)
        => new(t.Id, t.Name, t.Description, t.CategoryName, t.CategoryColor, t.IsLocked, t.TargetMode, ParseIds(t.TargetIdsJson),
            t.PublishedVersion, t.PublishedAt, notes, t.IsMcpGuide, McpGuideTopics.Normalize(t.McpGuideTopic));

    private static List<Guid> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new List<Guid>(); }
        try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>(); }
        catch (JsonException) { return new List<Guid>(); }
    }
}
