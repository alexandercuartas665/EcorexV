using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record QuoteTemplateDto(Guid Id, string Name, string HtmlContent, bool IsDefault, DateTimeOffset UpdatedAt, bool SendAsImage);

public interface IQuoteTemplateService
{
    Task<IReadOnlyList<QuoteTemplateDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<QuoteTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<QuoteTemplateDto?> CreateAsync(string name, string html, bool sendAsImage, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<QuoteTemplateDto?> UpdateAsync(Guid id, string name, string html, bool sendAsImage, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> SetDefaultAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Plantillas HTML de cotizacion por agencia (modulo Plantillas). Entidad TENANT-SCOPED.
/// Permite crear/editar varias plantillas y marcar una como predeterminada. El HTML se renderiza
/// luego con los datos de un lead para imprimir/generar el PDF de la cotizacion.
/// </summary>
public sealed class QuoteTemplateService : IQuoteTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public QuoteTemplateService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<QuoteTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.QuoteTemplates
            .AsNoTracking()
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .Select(t => new QuoteTemplateDto(t.Id, t.Name, t.HtmlContent, t.IsDefault, t.UpdatedAt ?? t.CreatedAt, t.SendAsImage))
            .ToListAsync(cancellationToken);

    public async Task<QuoteTemplateDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.QuoteTemplates
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new QuoteTemplateDto(t.Id, t.Name, t.HtmlContent, t.IsDefault, t.UpdatedAt ?? t.CreatedAt, t.SendAsImage))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<QuoteTemplateDto?> CreateAsync(string name, string html, bool sendAsImage, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var clean = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(clean)) { return null; }

        var isFirst = !await _db.QuoteTemplates.AnyAsync(cancellationToken);
        var entity = new QuoteTemplate
        {
            TenantId = tenantId,
            Name = clean,
            HtmlContent = html ?? string.Empty,
            IsDefault = isFirst,   // la primera plantilla de la agencia queda como predeterminada
            SendAsImage = sendAsImage
        };
        _db.QuoteTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<QuoteTemplateDto?> UpdateAsync(Guid id, string name, string html, bool sendAsImage, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.QuoteTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null) { return null; }
        var clean = (name ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(clean)) { entity.Name = clean; }
        entity.HtmlContent = html ?? string.Empty;
        entity.SendAsImage = sendAsImage;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var entity = await _db.QuoteTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null) { return false; }
        var wasDefault = entity.IsDefault;
        _db.QuoteTemplates.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        // Si se borro la predeterminada, promover otra (la primera por nombre) para no quedar sin default.
        if (wasDefault)
        {
            var next = await _db.QuoteTemplates.OrderBy(t => t.Name).FirstOrDefaultAsync(cancellationToken);
            if (next is not null) { next.IsDefault = true; await _db.SaveChangesAsync(cancellationToken); }
        }
        return true;
    }

    public async Task<bool> SetDefaultAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var target = await _db.QuoteTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (target is null) { return false; }
        var all = await _db.QuoteTemplates.Where(t => t.IsDefault).ToListAsync(cancellationToken);
        foreach (var t in all) { t.IsDefault = false; }
        target.IsDefault = true;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static QuoteTemplateDto Map(QuoteTemplate t) => new(t.Id, t.Name, t.HtmlContent, t.IsDefault, t.UpdatedAt ?? t.CreatedAt, t.SendAsImage);
}
