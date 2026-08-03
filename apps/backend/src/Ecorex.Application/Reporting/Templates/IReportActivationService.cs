using Ecorex.Application.Common;
using Ecorex.Application.Reporting.Authoring;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.Templates;

/// <summary>
/// Corazon del modelo HIBRIDO (ADR-0062): activa una plantilla de plataforma en el TENANT ACTIVO
/// creando una instancia <see cref="ReportDefinition"/> tenant-scoped (snapshot de la plantilla +
/// vinculo <see cref="ReportDefinition.TemplateId"/>). Todo corre bajo el filtro global del DbContext,
/// asi que la instancia solo ve los datos de su tenant; la plantilla es metadato de plataforma y el
/// dato NUNCA viaja con ella. La gobernanza por rol (report_definition_roles, doc 04) no se toca.
/// </summary>
public interface IReportActivationService
{
    /// <summary>Activa la plantilla en el tenant activo (valida compatibilidad de fuente). Idempotente:
    /// si ya hay una instancia de esa plantilla, no la duplica (reactiva si estaba archivada).</summary>
    Task<ReportActivationResult> ActivateTemplateAsync(Guid templateId, CancellationToken ct = default);

    /// <summary>Archiva (soft-delete) la instancia activada SIN tocar la plantilla.</summary>
    Task<bool> DeactivateTemplateAsync(Guid reportId, CancellationToken ct = default);

    /// <summary>Re-copia el snapshot desde la plantilla a la instancia SIN perder la asignacion de
    /// roles (report_definition_roles se conserva intacta).</summary>
    Task<bool> ResyncFromTemplateAsync(Guid reportId, CancellationToken ct = default);

    /// <summary>Barrido idempotente: activa en el tenant activo todas las plantillas publicadas
    /// compatibles que aun no esten activadas. <paramref name="includeNative"/> = false auto-activa solo
    /// las de fuente CONTENEDOR (para el enganche de galeria: el Panel OCS aparece solo donde hay datos).</summary>
    Task<int> ActivateCompatibleAsync(bool includeNative, CancellationToken ct = default);

    /// <summary>Plantillas publicadas con su estado de activacion para el tenant activo (para la pagina
    /// de activacion del tenant). Incluye compatibilidad y el id de la instancia si ya esta activada.</summary>
    Task<IReadOnlyList<ActivatableTemplate>> ListActivatableAsync(CancellationToken ct = default);
}

public sealed record ReportActivationResult(bool Ok, Guid? ReportId, string? Message);

public sealed record ActivatableTemplate(
    Guid TemplateId, string Name, string? Description, ReportTemplateKind Kind, string SourceKey,
    ReportTemplateSourceKind RequiredSourceKind, string? RequiredContainerName, string? Category,
    bool IsCompatible, string? IncompatibleReason, bool IsActivated, Guid? ReportId);

public sealed class ReportActivationService : IReportActivationService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public ReportActivationService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<ReportActivationResult> ActivateTemplateAsync(Guid templateId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            return new ReportActivationResult(false, null, "No hay tenant activo.");
        }

        // La plantilla es GLOBAL (sin filtro): se resuelve por Id directo.
        var template = await _db.ReportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
        {
            return new ReportActivationResult(false, null, "La plantilla no existe.");
        }
        if (!template.IsPublished)
        {
            return new ReportActivationResult(false, null, "La plantilla no esta publicada.");
        }

        var compat = await ReportTemplateCompatibility.EvaluateAsync(_db, template, ct);
        if (!compat.Ok)
        {
            return new ReportActivationResult(false, null, compat.Reason);
        }

        // Idempotencia: una sola instancia por (tenant, plantilla). Si ya existe, no se duplica.
        var existing = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.TemplateId == templateId, ct);
        if (existing is not null)
        {
            if (existing.Status == ReportDefinitionStatus.Archived)
            {
                existing.Status = ReportDefinitionStatus.Active;
                CopySnapshot(template, existing);
                await _db.SaveChangesAsync(ct);
            }
            return new ReportActivationResult(true, existing.Id, "La plantilla ya estaba activada en este tenant.");
        }

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            TemplateId = template.Id,
            Status = ReportDefinitionStatus.Active
        };
        CopySnapshot(template, def);
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return new ReportActivationResult(true, def.Id, null);
    }

    public async Task<bool> DeactivateTemplateAsync(Guid reportId, CancellationToken ct = default)
    {
        // Solo instancias PROVENIENTES de plantilla (TemplateId != null); un reporte propio no se toca aqui.
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == reportId && d.TemplateId != null, ct);
        if (def is null)
        {
            return false;
        }

        def.Status = ReportDefinitionStatus.Archived;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ResyncFromTemplateAsync(Guid reportId, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == reportId && d.TemplateId != null, ct);
        if (def is null)
        {
            return false;
        }

        var template = await _db.ReportTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == def.TemplateId, ct);
        if (template is null)
        {
            return false;
        }

        // Re-copia el snapshot. NO se tocan report_definition_roles: la asignacion de roles del tenant
        // se conserva por completo (solo se refrescan SourceKey/Kind/SpecJson/Rdl/Name/Description).
        CopySnapshot(template, def);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> ActivateCompatibleAsync(bool includeNative, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            return 0;
        }

        var templates = await _db.ReportTemplates.AsNoTracking()
            .Where(t => t.IsPublished)
            .ToListAsync(ct);
        if (templates.Count == 0)
        {
            return 0;
        }

        // Instancias ya vinculadas a una plantilla en este tenant (cualquier estado): no se re-activan.
        var alreadyLinked = await _db.ReportDefinitions
            .Where(d => d.TemplateId != null)
            .Select(d => d.TemplateId!.Value)
            .ToListAsync(ct);
        var linkedSet = new HashSet<Guid>(alreadyLinked);

        var activated = 0;
        foreach (var template in templates)
        {
            if (linkedSet.Contains(template.Id))
            {
                continue;
            }
            if (!includeNative && template.RequiredSourceKind == ReportTemplateSourceKind.Native)
            {
                continue;
            }

            var compat = await ReportTemplateCompatibility.EvaluateAsync(_db, template, ct);
            if (!compat.Ok)
            {
                continue;
            }

            var def = new ReportDefinition
            {
                Id = Guid.CreateVersion7(),
                TemplateId = template.Id,
                Status = ReportDefinitionStatus.Active
            };
            CopySnapshot(template, def);
            _db.ReportDefinitions.Add(def);
            linkedSet.Add(template.Id);
            activated++;
        }

        if (activated > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
        return activated;
    }

    public async Task<IReadOnlyList<ActivatableTemplate>> ListActivatableAsync(CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            return Array.Empty<ActivatableTemplate>();
        }

        var templates = await _db.ReportTemplates.AsNoTracking()
            .Where(t => t.IsPublished)
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .ToListAsync(ct);

        // Instancias activas por plantilla en este tenant (para marcar "ya activada").
        var active = await _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.TemplateId != null && d.Status == ReportDefinitionStatus.Active)
            .Select(d => new { d.TemplateId, d.Id })
            .ToListAsync(ct);
        var activeByTemplate = active
            .GroupBy(x => x.TemplateId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var result = new List<ActivatableTemplate>(templates.Count);
        foreach (var t in templates)
        {
            var compat = await ReportTemplateCompatibility.EvaluateAsync(_db, t, ct);
            var isActivated = activeByTemplate.TryGetValue(t.Id, out var reportId);
            result.Add(new ActivatableTemplate(
                t.Id, t.Name, t.Description, t.Kind, t.SourceKey, t.RequiredSourceKind,
                t.RequiredContainerName, t.Category, compat.Ok, compat.Reason,
                isActivated, isActivated ? reportId : null));
        }
        return result;
    }

    /// <summary>Copia el molde de la plantilla a la instancia: SourceKey/Kind/SpecJson/Rdl/Name/Description.
    /// Panel se mapea a Dashboard (la galeria detecta el panel por el prefijo del SourceKey). Si la plantilla
    /// no trae SpecJson, se sintetiza uno minimo (la instancia lo exige no-null).</summary>
    private static void CopySnapshot(ReportTemplate template, ReportDefinition def)
    {
        def.Name = template.Name;
        def.Description = template.Description;
        def.Kind = template.Kind == ReportTemplateKind.Printable
            ? ReportDefinitionKind.Printable
            : ReportDefinitionKind.Dashboard;
        def.SourceKey = template.SourceKey;
        def.Rdl = template.Rdl;
        def.SpecJson = !string.IsNullOrWhiteSpace(template.SpecJson)
            ? template.SpecJson!
            : new ReportSpec { Title = template.Name, SourceKey = template.SourceKey, Chart = ReportChartKind.Table }.ToJson();
    }
}
