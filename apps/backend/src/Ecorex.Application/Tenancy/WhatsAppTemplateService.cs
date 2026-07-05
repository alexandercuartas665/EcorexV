using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Implementacion de IWhatsAppTemplateService (plantillas HSM, ADR-0029). Aislamiento por tenant
/// via filtro global. Unica por (Name, Language) por tenant (validado con mensaje claro + indice
/// unico como defensa en profundidad). Auditoria en las acciones sensibles.
///
/// DEUDA (ADR-0029): NO hay integracion real con la WhatsApp Cloud API de Meta. Submit es un stub
/// que solo cambia el estado a Submitted; SyncStatus devuelve NotImplemented. Cuando exista el
/// gateway del proveedor, Submit compilaria el cuerpo (tokens {{x}} -> {{1}}..{{n}}) y llamaria a
/// Meta; hoy no se invoca ningun endpoint HTTP externo.
/// </summary>
public sealed class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _timeProvider;

    public WhatsAppTemplateService(IApplicationDbContext db, ITenantContext tenantContext,
        IAuditWriter audit, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<WhatsAppTemplateVariableDef> Catalog() => WhatsAppTemplateVariableCatalog.All;

    public async Task<IReadOnlyList<WhatsAppTemplateDto>> ListAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _db.WhatsAppTemplates.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }
        var rows = await query
            .OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        var lineNames = await LineNamesAsync(rows, cancellationToken);
        return rows.Select(t => Map(t, lineNames)).ToList();
    }

    public async Task<WhatsAppTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var t = await _db.WhatsAppTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (t is null) { return null; }
        var lineNames = await LineNamesAsync([t], cancellationToken);
        return Map(t, lineNames);
    }

    public async Task<WhatsAppTemplateResult<WhatsAppTemplateDto>> CreateAsync(
        SaveWhatsAppTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid("No hay tenant activo.");
        }
        var validation = WhatsAppTemplateCalculations.ValidateSave(request);
        if (validation is not null)
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid(validation);
        }

        var name = WhatsAppTemplateCalculations.NormalizeName(request.Name);
        var language = request.Language.Trim();
        // Choque con el indice unico (TenantId, Name, Language): reporta Conflict tipado.
        if (await _db.WhatsAppTemplates.AnyAsync(x => x.Name == name && x.Language == language, cancellationToken))
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Conflict(
                $"Ya existe una plantilla '{name}' en idioma '{language}'.");
        }

        var line = await _db.WhatsAppLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.WhatsAppLineId, cancellationToken);
        if (line is null)
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid("La linea de WhatsApp elegida no existe.");
        }

        var template = new WhatsAppTemplate { TenantId = tenantId, Status = WhatsAppTemplateStatus.Draft };
        Apply(template, request, name, language, line);

        _db.WhatsAppTemplates.Add(template);
        _audit.Write(_tenantContext.UserId ?? Guid.Empty, "wa-template.create", nameof(WhatsAppTemplate), template.Id,
            previousValue: null, newValue: new { template.Name, template.Language, Category = template.Category.ToString() }, tenantId: tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return WhatsAppTemplateResult<WhatsAppTemplateDto>.Ok((await GetAsync(template.Id, cancellationToken))!);
    }

    public async Task<WhatsAppTemplateResult<WhatsAppTemplateDto>> UpdateAsync(
        Guid id, SaveWhatsAppTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var template = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.NotFound("La plantilla no existe.");
        }
        if (!WhatsAppTemplateCalculations.CanEdit(template.Status))
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid(
                "Solo se pueden editar plantillas en borrador o rechazadas.");
        }
        var validation = WhatsAppTemplateCalculations.ValidateSave(request);
        if (validation is not null)
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid(validation);
        }

        var name = WhatsAppTemplateCalculations.NormalizeName(request.Name);
        var language = request.Language.Trim();
        if (await _db.WhatsAppTemplates.AnyAsync(
                x => x.Id != id && x.Name == name && x.Language == language, cancellationToken))
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Conflict(
                $"Ya existe otra plantilla '{name}' en idioma '{language}'.");
        }

        var line = await _db.WhatsAppLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.WhatsAppLineId, cancellationToken);
        if (line is null)
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid("La linea de WhatsApp elegida no existe.");
        }

        Apply(template, request, name, language, line);
        // Al reeditar una plantilla rechazada, vuelve a borrador y se limpia el motivo.
        if (template.Status == WhatsAppTemplateStatus.Rejected)
        {
            template.Status = WhatsAppTemplateStatus.Draft;
            template.RejectionReason = null;
        }
        _audit.Write(_tenantContext.UserId ?? Guid.Empty, "wa-template.update", nameof(WhatsAppTemplate), template.Id,
            previousValue: null, newValue: new { template.Name, template.Language, Category = template.Category.ToString() }, tenantId: template.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return WhatsAppTemplateResult<WhatsAppTemplateDto>.Ok((await GetAsync(template.Id, cancellationToken))!);
    }

    public async Task<WhatsAppTemplateResult<bool>> SetActiveAsync(
        Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var template = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
        {
            return WhatsAppTemplateResult<bool>.NotFound("La plantilla no existe.");
        }
        template.IsActive = active;
        _audit.Write(_tenantContext.UserId ?? Guid.Empty, active ? "wa-template.restore" : "wa-template.archive",
            nameof(WhatsAppTemplate), template.Id,
            previousValue: null, newValue: new { template.Name, IsActive = active }, tenantId: template.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return WhatsAppTemplateResult<bool>.Ok(true);
    }

    public async Task<WhatsAppTemplateResult<WhatsAppTemplateDto>> SubmitAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        // STUB (ADR-0029): NO se llama a Meta/WhatsApp Cloud API. Solo se transiciona el estado y
        // se registra la auditoria. Cuando exista el gateway del proveedor, aqui iria la
        // compilacion del cuerpo y la llamada HTTP real.
        var template = await _db.WhatsAppTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.NotFound("La plantilla no existe.");
        }
        if (!WhatsAppTemplateCalculations.CanSubmit(template.Status))
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid(
                "Solo se pueden someter plantillas en borrador o rechazadas.");
        }
        if (string.IsNullOrWhiteSpace(template.BodyText))
        {
            return WhatsAppTemplateResult<WhatsAppTemplateDto>.Invalid("El cuerpo es obligatorio.");
        }

        template.Status = WhatsAppTemplateStatus.Submitted;
        template.SubmittedAt = _timeProvider.GetUtcNow();
        template.RejectionReason = null;
        _audit.Write(_tenantContext.UserId ?? Guid.Empty, "wa-template.submit", nameof(WhatsAppTemplate), template.Id,
            previousValue: null, newValue: new { template.Name, Status = template.Status.ToString() }, tenantId: template.TenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return WhatsAppTemplateResult<WhatsAppTemplateDto>.Ok((await GetAsync(template.Id, cancellationToken))!);
    }

    public Task<WhatsAppTemplateResult<bool>> SyncStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // STUB (ADR-0029): sin integracion real con Meta no hay estado remoto que consultar.
        return Task.FromResult(WhatsAppTemplateResult<bool>.NotImplemented(
            "La sincronizacion con el proveedor no esta implementada (sin integracion real con Meta)."));
    }

    // ===== Helpers ============================================================

    private static void Apply(WhatsAppTemplate template, SaveWhatsAppTemplateRequest request,
        string name, string language, WhatsAppLine line)
    {
        template.Name = name;
        template.Language = language;
        template.Category = request.Category;
        template.HeaderType = string.IsNullOrWhiteSpace(request.HeaderText)
            ? request.HeaderType
            : (request.HeaderType ?? WhatsAppTemplateHeaderType.Text);
        template.HeaderText = string.IsNullOrWhiteSpace(request.HeaderText) ? null : request.HeaderText.Trim();
        template.BodyText = request.BodyText.Trim();
        template.FooterText = string.IsNullOrWhiteSpace(request.FooterText) ? null : request.FooterText.Trim();
        template.VariablesJson = JsonSerializer.Serialize(request.Variables ?? Array.Empty<WhatsAppTemplateVariable>());
        template.WhatsAppLineId = line.Id;
        template.Provider = line.Provider;
        template.WabaId = line.CloudBusinessAccountId;
    }

    private async Task<Dictionary<Guid, string>> LineNamesAsync(
        IReadOnlyCollection<WhatsAppTemplate> templates, CancellationToken cancellationToken)
    {
        var ids = templates.Select(t => t.WhatsAppLineId).Distinct().ToList();
        if (ids.Count == 0) { return new Dictionary<Guid, string>(); }
        return await _db.WhatsAppLines.AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.InstanceName, cancellationToken);
    }

    private static WhatsAppTemplateDto Map(WhatsAppTemplate t, IReadOnlyDictionary<Guid, string> lineNames)
    {
        IReadOnlyList<WhatsAppTemplateVariable> vars;
        try { vars = JsonSerializer.Deserialize<List<WhatsAppTemplateVariable>>(t.VariablesJson) ?? new(); }
        catch { vars = new List<WhatsAppTemplateVariable>(); }
        return new WhatsAppTemplateDto(
            t.Id, t.Name, t.Language, t.Category, t.HeaderType, t.HeaderText, t.BodyText, t.FooterText,
            vars, t.Provider, t.WhatsAppLineId,
            lineNames.TryGetValue(t.WhatsAppLineId, out var lineName) ? lineName : null,
            t.WabaId, t.Status, t.ProviderTemplateId, t.RejectionReason,
            t.SubmittedAt, t.ReviewedAt, t.IsActive);
    }
}
