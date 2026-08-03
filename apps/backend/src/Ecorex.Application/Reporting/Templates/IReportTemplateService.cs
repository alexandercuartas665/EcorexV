using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Templates;

/// <summary>
/// Catalogo MAESTRO de plantillas de reportes (ADR-0062). Lectura de las publicadas para cualquiera;
/// el CRUD es cross-tenant (metadato de plataforma) y por eso queda restringido a PlatformAdmin y
/// AUDITADO (AdminAuditLog dentro de la transaccion), como el resto de acciones de plataforma.
/// </summary>
public interface IReportTemplateService
{
    /// <summary>Plantillas PUBLICADAS (lectura para los tenants).</summary>
    Task<IReadOnlyList<ReportTemplateSummary>> ListPublishedAsync(CancellationToken ct = default);

    /// <summary>TODAS las plantillas, publicadas o no (administracion de plataforma).</summary>
    Task<IReadOnlyList<ReportTemplateSummary>> ListAllAsync(CancellationToken ct = default);

    Task<ReportTemplateDetail?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea una plantilla (PlatformAdmin, auditado).</summary>
    Task<Guid> CreateAsync(ReportTemplateInput input, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Edita una plantilla (PlatformAdmin, auditado). Devuelve false si no existe.</summary>
    Task<bool> UpdateAsync(Guid id, ReportTemplateInput input, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Publica o despublica una plantilla (PlatformAdmin, auditado). Devuelve false si no existe.</summary>
    Task<bool> SetPublishedAsync(Guid id, bool published, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Plantillas activables para el tenant activo (evalua compatibilidad y estado de activacion).
    /// <paramref name="tenantId"/> es el tenant en contexto (el filtro global del DbContext lo aplica).</summary>
    Task<IReadOnlyList<ActivatableTemplate>> GetActivatableForTenantAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed record ReportTemplateSummary(
    Guid Id, string Name, ReportTemplateKind Kind, string SourceKey,
    ReportTemplateSourceKind RequiredSourceKind, string? RequiredContainerName,
    string? Category, bool IsPublished, DateTimeOffset? UpdatedAt);

public sealed record ReportTemplateDetail(
    Guid Id, string Name, string? Description, ReportTemplateKind Kind, string SourceKey,
    string? SpecJson, string? Rdl, ReportTemplateSourceKind RequiredSourceKind,
    string? RequiredContainerName, string? Category, string? Icon, bool IsPublished);

public sealed record ReportTemplateInput(
    string Name, string? Description, ReportTemplateKind Kind, string SourceKey,
    string? SpecJson, string? Rdl, ReportTemplateSourceKind RequiredSourceKind,
    string? RequiredContainerName, string? Category, string? Icon);

public sealed class ReportTemplateService : IReportTemplateService
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IReportActivationService _activation;

    public ReportTemplateService(IApplicationDbContext db, IAuditWriter audit, IReportActivationService activation)
    {
        _db = db;
        _audit = audit;
        _activation = activation;
    }

    public async Task<IReadOnlyList<ReportTemplateSummary>> ListPublishedAsync(CancellationToken ct = default)
    {
        return await _db.ReportTemplates.AsNoTracking()
            .Where(t => t.IsPublished)
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .Select(Summary)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReportTemplateSummary>> ListAllAsync(CancellationToken ct = default)
    {
        return await _db.ReportTemplates.AsNoTracking()
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .Select(Summary)
            .ToListAsync(ct);
    }

    public async Task<ReportTemplateDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.ReportTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? null : Detail(t);
    }

    public async Task<Guid> CreateAsync(ReportTemplateInput input, Guid actorUserId, CancellationToken ct = default)
    {
        var template = new ReportTemplate { Id = Guid.CreateVersion7() };
        Apply(template, input);

        _db.ReportTemplates.Add(template);
        _audit.Write(actorUserId, "report-template.create", nameof(ReportTemplate), template.Id,
            previousValue: null,
            newValue: new { template.Name, template.Kind, template.SourceKey, template.RequiredSourceKind, template.RequiredContainerName, template.IsPublished });
        await _db.SaveChangesAsync(ct);
        return template.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, ReportTemplateInput input, Guid actorUserId, CancellationToken ct = default)
    {
        var template = await _db.ReportTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (template is null)
        {
            return false;
        }

        var previous = new { template.Name, template.Kind, template.SourceKey, template.RequiredSourceKind, template.RequiredContainerName, template.IsPublished };
        Apply(template, input);
        _audit.Write(actorUserId, "report-template.update", nameof(ReportTemplate), template.Id,
            previousValue: previous,
            newValue: new { template.Name, template.Kind, template.SourceKey, template.RequiredSourceKind, template.RequiredContainerName, template.IsPublished });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetPublishedAsync(Guid id, bool published, Guid actorUserId, CancellationToken ct = default)
    {
        var template = await _db.ReportTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (template is null)
        {
            return false;
        }
        if (template.IsPublished == published)
        {
            return true;
        }

        template.IsPublished = published;
        _audit.Write(actorUserId, published ? "report-template.publish" : "report-template.unpublish",
            nameof(ReportTemplate), template.Id,
            previousValue: new { IsPublished = !published },
            newValue: new { IsPublished = published });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<IReadOnlyList<ActivatableTemplate>> GetActivatableForTenantAsync(Guid tenantId, CancellationToken ct = default)
        => _activation.ListActivatableAsync(ct);

    private static void Apply(ReportTemplate template, ReportTemplateInput input)
    {
        template.Name = input.Name.Trim();
        template.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        template.Kind = input.Kind;
        template.SourceKey = input.SourceKey.Trim();
        template.SpecJson = string.IsNullOrWhiteSpace(input.SpecJson) ? null : input.SpecJson;
        template.Rdl = string.IsNullOrWhiteSpace(input.Rdl) ? null : input.Rdl;
        template.RequiredSourceKind = input.RequiredSourceKind;
        template.RequiredContainerName = input.RequiredSourceKind == ReportTemplateSourceKind.Container
            ? (string.IsNullOrWhiteSpace(input.RequiredContainerName) ? null : input.RequiredContainerName.Trim())
            : null;
        template.Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
        template.Icon = string.IsNullOrWhiteSpace(input.Icon) ? null : input.Icon.Trim();
    }

    private static readonly System.Linq.Expressions.Expression<Func<ReportTemplate, ReportTemplateSummary>> Summary =
        t => new ReportTemplateSummary(t.Id, t.Name, t.Kind, t.SourceKey, t.RequiredSourceKind,
            t.RequiredContainerName, t.Category, t.IsPublished, t.UpdatedAt);

    private static ReportTemplateDetail Detail(ReportTemplate t) =>
        new(t.Id, t.Name, t.Description, t.Kind, t.SourceKey, t.SpecJson, t.Rdl,
            t.RequiredSourceKind, t.RequiredContainerName, t.Category, t.Icon, t.IsPublished);
}
