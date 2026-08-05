using Ecorex.Application.Common;
using Ecorex.Application.Reporting.External;
using Ecorex.Application.Reporting.Sources;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting;

/// <summary>
/// Catalogo semantico: publica QUE es reportable con nombres de negocio. Dos origenes:
/// - Entidades NATIVAS: registro curado por el dev (cada <see cref="IReportableSource"/>).
/// - CONTENEDORES: se derivan solos de los DataContainer del tenant (tablas raiz).
///
/// Es TAMBIEN el limite de seguridad: si una fuente/campo no esta aqui, no es reportable. El listado
/// de contenedores sale del DbContext tenant-scoped, asi que cada tenant solo ve su propio catalogo
/// de contenedores.
/// </summary>
public interface IReportCatalog
{
    Task<IReadOnlyList<ReportSourceDescriptor>> GetSourcesAsync(CancellationToken ct = default);

    Task<ReportSourceDescriptor?> GetSourceAsync(string sourceKey, CancellationToken ct = default);
}

public sealed class ReportCatalog : IReportCatalog
{
    private readonly IEnumerable<IReportableSource> _nativeSources;
    private readonly ContainerReportReader _containers;
    private readonly ExternalReportReader _external;
    private readonly ITenantContext _tenantContext;
    private readonly IApplicationDbContext _db;

    public ReportCatalog(
        IEnumerable<IReportableSource> nativeSources,
        ContainerReportReader containers,
        ExternalReportReader external,
        ITenantContext tenantContext,
        IApplicationDbContext db)
    {
        _nativeSources = nativeSources;
        _containers = containers;
        _external = external;
        _tenantContext = tenantContext;
        _db = db;
    }

    public async Task<IReadOnlyList<ReportSourceDescriptor>> GetSourcesAsync(CancellationToken ct = default)
    {
        var result = new List<ReportSourceDescriptor>();

        foreach (var s in _nativeSources)
        {
            result.Add(s.Describe());
        }

        // Contenedores RAIZ del tenant activo (el filtro global limita a lo suyo).
        var rootContainers = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ParentContainerId == null)
            .OrderBy(c => c.Name)
            .Select(c => c.Id)
            .ToListAsync(ct);

        foreach (var id in rootContainers)
        {
            var descriptor = await _containers.DescribeAsync(id, ct);
            if (descriptor is not null)
            {
                result.Add(descriptor);
            }
        }

        // Fuentes EXTERNAS concedidas al tenant activo (ADR-0063). Un tenant sin concesion no ve ninguna.
        if (_tenantContext.TenantId is Guid tenantId)
        {
            result.AddRange(await _external.ListGrantedAsync(tenantId, ct));
        }

        return result;
    }

    public async Task<ReportSourceDescriptor?> GetSourceAsync(string sourceKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return null;
        }

        var native = _nativeSources
            .Select(s => s.Describe())
            .FirstOrDefault(d => string.Equals(d.Key, sourceKey, StringComparison.OrdinalIgnoreCase));
        if (native is not null)
        {
            return native;
        }

        if (ContainerReportReader.Handles(sourceKey) && ContainerReportReader.ParseId(sourceKey) is Guid id)
        {
            return await _containers.DescribeAsync(id, ct);
        }

        // Fuente EXTERNA: solo se describe si el tenant activo tiene concesion (el reader lo verifica).
        if (ExternalReportReader.Handles(sourceKey)
            && ExternalReportReader.ParseId(sourceKey) is Guid externalId
            && _tenantContext.TenantId is Guid activeTenant)
        {
            return await _external.DescribeAsync(externalId, activeTenant, ct);
        }

        return null;
    }
}
