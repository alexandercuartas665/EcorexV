using Ecorex.Application.Common;
using Ecorex.Application.Reporting.Authoring;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting;

/// <summary>
/// Persistencia de reportes guardados (ADR-0051, Ola 4): guarda/lista/obtiene/edita/archiva y EJECUTA
/// una <see cref="Ecorex.Domain.Entities.ReportDefinition"/>. El artefacto guardado es el JSON-spec
/// (mismo que edita la IA y el usuario). Todo tenant-scoped por el filtro global; ejecutar un reporte
/// pasa por el datasource tenant-safe, nunca por una cadena de conexion.
/// </summary>
public interface IReportDefinitionService
{
    Task<Guid> SaveAsync(ReportSpec spec, string? description, CancellationToken ct = default);

    /// <summary>Guarda un imprimible: genera el RDL desde el spec + el shape del resultado y lo
    /// persiste con Kind=Printable. Es el artefacto que abrira el editor/visor Bold (Ola 2).</summary>
    Task<Guid> SavePrintableAsync(ReportSpec spec, ReportDataSet ds, string? description, CancellationToken ct = default);

    Task UpdateSpecAsync(Guid id, ReportSpec spec, CancellationToken ct = default);

    Task<IReadOnlyList<ReportDefinitionSummary>> ListAsync(CancellationToken ct = default);

    Task<ReportDefinitionDetail?> GetAsync(Guid id, CancellationToken ct = default);

    Task ArchiveAsync(Guid id, CancellationToken ct = default);

    Task<ReportRunResult?> RunAsync(Guid id, CancellationToken ct = default);

    /// <summary>Devuelve el RDL de un imprimible + su dataset ya ejecutado por el datasource tenant-safe,
    /// para alimentar al visor Bold. Null si no existe o no tiene RDL. Tenant-scoped por construccion.</summary>
    Task<ReportPrintable?> GetPrintableAsync(Guid id, CancellationToken ct = default);
}

/// <summary>RDL de un imprimible + las filas ya filtradas por tenant que lo alimentan.</summary>
public sealed record ReportPrintable(string Rdl, ReportDataSet DataSet);

public sealed record ReportDefinitionSummary(
    Guid Id, string Name, ReportDefinitionKind Kind, string? SourceKey,
    ReportDefinitionStatus Status, DateTimeOffset? UpdatedAt);

public sealed record ReportDefinitionDetail(
    Guid Id, string Name, string? Description, ReportDefinitionKind Kind, ReportSpec Spec, long Version);

public sealed record ReportRunResult(ReportSpec Spec, ReportDataSet DataSet, object? Option);

public sealed class ReportDefinitionService : IReportDefinitionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IReportDataSource _dataSource;

    public ReportDefinitionService(IApplicationDbContext db, ITenantContext tenantContext, IReportDataSource dataSource)
    {
        _db = db;
        _tenantContext = tenantContext;
        _dataSource = dataSource;
    }

    public async Task<Guid> SaveAsync(ReportSpec spec, string? description, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(spec.Title) ? "Reporte sin titulo" : spec.Title.Trim(),
            Description = description,
            Kind = ReportDefinitionKind.Dashboard,
            Status = ReportDefinitionStatus.Active,
            SourceKey = spec.SourceKey,
            SpecJson = spec.ToJson()
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    public async Task<Guid> SavePrintableAsync(ReportSpec spec, ReportDataSet ds, string? description, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }

        var def = new ReportDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = string.IsNullOrWhiteSpace(spec.Title) ? "Imprimible sin titulo" : spec.Title.Trim(),
            Description = description,
            Kind = ReportDefinitionKind.Printable,
            Status = ReportDefinitionStatus.Active,
            SourceKey = spec.SourceKey,
            SpecJson = spec.ToJson(),
            Rdl = ReportSpecToRdl.ToRdl(spec, ds)
        };
        _db.ReportDefinitions.Add(def);
        await _db.SaveChangesAsync(ct);
        return def.Id;
    }

    public async Task UpdateSpecAsync(Guid id, ReportSpec spec, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException("El reporte no existe.");
        def.Name = string.IsNullOrWhiteSpace(spec.Title) ? def.Name : spec.Title.Trim();
        def.SourceKey = spec.SourceKey;
        def.SpecJson = spec.ToJson();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReportDefinitionSummary>> ListAsync(CancellationToken ct = default)
    {
        return await _db.ReportDefinitions.AsNoTracking()
            .Where(d => d.Status == ReportDefinitionStatus.Active)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Select(d => new ReportDefinitionSummary(d.Id, d.Name, d.Kind, d.SourceKey, d.Status, d.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<ReportDefinitionDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return null;
        }

        var spec = ReportSpec.FromJson(def.SpecJson) ?? new ReportSpec { Title = def.Name };
        return new ReportDefinitionDetail(def.Id, def.Name, def.Description, def.Kind, spec, def.Version);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var def = await _db.ReportDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return;
        }

        def.Status = ReportDefinitionStatus.Archived;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ReportRunResult?> RunAsync(Guid id, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var def = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null)
        {
            return null;
        }

        var spec = ReportSpec.FromJson(def.SpecJson);
        if (spec is null)
        {
            return null;
        }

        var ds = await _dataSource.QueryAsync(spec.ToQuerySpec(), new ReportContext(tenantId), ct);
        var option = ReportSpecRenderer.BuildOption(spec, ds);
        return new ReportRunResult(spec, ds, option);
    }

    public async Task<ReportPrintable?> GetPrintableAsync(Guid id, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var def = await _db.ReportDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (def is null || string.IsNullOrWhiteSpace(def.Rdl))
        {
            return null;
        }

        var spec = ReportSpec.FromJson(def.SpecJson);
        if (spec is null)
        {
            return null;
        }

        var ds = await _dataSource.QueryAsync(spec.ToQuerySpec(), new ReportContext(tenantId), ct);
        return new ReportPrintable(def.Rdl!, ds);
    }
}
