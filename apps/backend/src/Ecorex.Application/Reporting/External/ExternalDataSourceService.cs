using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Reporting.External;

/// <summary>
/// CRUD auditado del catalogo de fuentes externas (ADR-0064). Cross-tenant (metadato de plataforma) ->
/// PlatformAdmin + SuperAdminAuditLog dentro de la transaccion. El secreto se cifra con ISecretProtector
/// y NUNCA se devuelve ni se audita en claro (solo se registra si HAY o no cadena). La prueba de
/// conexion delega en el executor de solo lectura (Infrastructure), que tiene los drivers.
/// </summary>
public sealed class ExternalDataSourceService : IExternalDataSourceService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IAuditWriter _audit;
    private readonly IExternalQueryExecutor _executor;

    public ExternalDataSourceService(
        IApplicationDbContext db, ISecretProtector protector, IAuditWriter audit, IExternalQueryExecutor executor)
    {
        _db = db;
        _protector = protector;
        _audit = audit;
        _executor = executor;
    }

    public async Task<IReadOnlyList<ExternalDataSourceSummary>> ListSourcesAsync(CancellationToken ct = default)
    {
        var sources = await _db.ExternalDataSources.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var result = new List<ExternalDataSourceSummary>(sources.Count);
        foreach (var s in sources)
        {
            var dataSetCount = await _db.ExternalDataSets.AsNoTracking().CountAsync(d => d.ExternalDataSourceId == s.Id, ct);
            var grantCount = await _db.ExternalDataSourceGrants.AsNoTracking().CountAsync(g => g.ExternalDataSourceId == s.Id && g.IsEnabled, ct);
            result.Add(new ExternalDataSourceSummary(
                s.Id, s.Name, s.Provider, s.IsEnabled,
                !string.IsNullOrEmpty(s.ConnectionStringEncrypted), s.LastValidatedAt, dataSetCount, grantCount));
        }

        return result;
    }

    public async Task<ExternalDataSourceDetail?> GetSourceAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _db.ExternalDataSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? null : new ExternalDataSourceDetail(
            s.Id, s.Name, s.Description, s.Provider,
            !string.IsNullOrEmpty(s.ConnectionStringEncrypted), s.IsReadOnly, s.IsEnabled, s.LastValidatedAt);
    }

    public async Task<Guid> CreateSourceAsync(SaveExternalDataSourceRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var source = new ExternalDataSource
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            Description = Trim(request.Description),
            Provider = request.Provider,
            IsReadOnly = true,
            IsEnabled = request.IsEnabled
        };
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            source.ConnectionStringEncrypted = _protector.Protect(request.ConnectionString.Trim());
        }

        _db.ExternalDataSources.Add(source);
        _audit.Write(actorUserId, "external-source.create", nameof(ExternalDataSource), source.Id,
            previousValue: null,
            newValue: new { source.Name, source.Provider, source.IsEnabled, HasConnectionString = source.ConnectionStringEncrypted is not null });
        await _db.SaveChangesAsync(ct);
        return source.Id;
    }

    public async Task<bool> UpdateSourceAsync(Guid id, SaveExternalDataSourceRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var source = await _db.ExternalDataSources.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return false;
        }

        var previous = new { source.Name, source.Provider, source.IsEnabled, HasConnectionString = source.ConnectionStringEncrypted is not null };
        source.Name = request.Name.Trim();
        source.Description = Trim(request.Description);
        source.Provider = request.Provider;
        source.IsEnabled = request.IsEnabled;
        source.IsReadOnly = true;
        // La cadena solo se re-cifra si llega un valor nuevo; vacia conserva la actual.
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            source.ConnectionStringEncrypted = _protector.Protect(request.ConnectionString.Trim());
            source.LastValidatedAt = null;
        }

        _audit.Write(actorUserId, "external-source.update", nameof(ExternalDataSource), source.Id,
            previousValue: previous,
            newValue: new { source.Name, source.Provider, source.IsEnabled, HasConnectionString = source.ConnectionStringEncrypted is not null });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetSourceEnabledAsync(Guid id, bool enabled, Guid actorUserId, CancellationToken ct = default)
    {
        var source = await _db.ExternalDataSources.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return false;
        }
        if (source.IsEnabled == enabled)
        {
            return true;
        }

        source.IsEnabled = enabled;
        _audit.Write(actorUserId, enabled ? "external-source.enable" : "external-source.disable",
            nameof(ExternalDataSource), source.Id,
            previousValue: new { IsEnabled = !enabled }, newValue: new { IsEnabled = enabled });
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> TestConnectionAsync(Guid id, SaveExternalDataSourceRequest? request, Guid actorUserId, CancellationToken ct = default)
    {
        var source = await _db.ExternalDataSources.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
        {
            return "La fuente externa no existe.";
        }

        var provider = request?.Provider ?? source.Provider;
        string? conn = request?.ConnectionString?.Trim();
        if (string.IsNullOrWhiteSpace(conn))
        {
            if (string.IsNullOrWhiteSpace(source.ConnectionStringEncrypted))
            {
                return "Falta la cadena de conexion.";
            }

            try { conn = _protector.Unprotect(source.ConnectionStringEncrypted); }
            catch { return "La cadena guardada esta cifrada con una version anterior. Vuelve a pegarla."; }
        }

        var error = await _executor.TestConnectionAsync(provider, conn!, ct);
        if (error is not null)
        {
            return error;
        }

        source.LastValidatedAt = DateTimeOffset.UtcNow;
        _audit.Write(actorUserId, "external-source.test", nameof(ExternalDataSource), source.Id,
            previousValue: null, newValue: new { Provider = provider, Ok = true });
        await _db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<IReadOnlyList<ExternalDataSetSummary>> ListDataSetsAsync(Guid sourceId, CancellationToken ct = default)
    {
        var datasets = await _db.ExternalDataSets.AsNoTracking()
            .Where(d => d.ExternalDataSourceId == sourceId)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        return datasets.Select(d => new ExternalDataSetSummary(
            d.Id, d.ExternalDataSourceId, d.Name, d.IsEnabled,
            ExternalDataJson.DeserializeParameters(d.ParametersJson).Count,
            ExternalDataJson.DeserializeFields(d.FieldsJson).Count)).ToList();
    }

    public async Task<ExternalDataSetDetail?> GetDataSetAsync(Guid id, CancellationToken ct = default)
    {
        var d = await _db.ExternalDataSets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? null : new ExternalDataSetDetail(
            d.Id, d.ExternalDataSourceId, d.Name, d.Description, d.CommandText,
            ExternalDataJson.DeserializeParameters(d.ParametersJson),
            ExternalDataJson.DeserializeFields(d.FieldsJson), d.IsEnabled);
    }

    public async Task<Guid> SaveDataSetAsync(SaveExternalDataSetRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        ExternalDataSet dataset;
        string action;
        if (request.Id is Guid existingId && await _db.ExternalDataSets.FirstOrDefaultAsync(x => x.Id == existingId, ct) is { } existing)
        {
            dataset = existing;
            action = "external-dataset.update";
        }
        else
        {
            dataset = new ExternalDataSet { Id = Guid.CreateVersion7(), ExternalDataSourceId = request.ExternalDataSourceId };
            _db.ExternalDataSets.Add(dataset);
            action = "external-dataset.create";
        }

        dataset.Name = request.Name.Trim();
        dataset.Description = Trim(request.Description);
        dataset.CommandText = request.CommandText.Trim();
        dataset.ParametersJson = ExternalDataJson.SerializeParameters(request.Parameters);
        dataset.FieldsJson = ExternalDataJson.SerializeFields(request.Fields);
        dataset.IsEnabled = request.IsEnabled;

        _audit.Write(actorUserId, action, nameof(ExternalDataSet), dataset.Id,
            previousValue: null,
            newValue: new { dataset.Name, dataset.ExternalDataSourceId, ParameterCount = request.Parameters.Count });
        await _db.SaveChangesAsync(ct);
        return dataset.Id;
    }

    public async Task<bool> DeleteDataSetAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var dataset = await _db.ExternalDataSets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (dataset is null)
        {
            return false;
        }

        _db.ExternalDataSets.Remove(dataset);
        _audit.Write(actorUserId, "external-dataset.delete", nameof(ExternalDataSet), dataset.Id,
            previousValue: new { dataset.Name, dataset.ExternalDataSourceId }, newValue: null);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ExternalGrantDto>> ListGrantsAsync(Guid sourceId, CancellationToken ct = default)
    {
        var query =
            from g in _db.ExternalDataSourceGrants.AsNoTracking()
            where g.ExternalDataSourceId == sourceId
            join t in _db.Tenants.AsNoTracking() on g.TenantId equals t.Id into tenants
            from t in tenants.DefaultIfEmpty()
            orderby t != null ? t.Name : ""
            select new ExternalGrantDto(g.Id, g.ExternalDataSourceId, g.TenantId, t != null ? t.Name : null, g.RolId, g.IsEnabled);

        return await query.ToListAsync(ct);
    }

    public async Task<Guid> GrantAsync(Guid sourceId, Guid tenantId, Guid? rolId, Guid actorUserId, CancellationToken ct = default)
    {
        // Idempotente: si ya existe la concesion (misma fuente+tenant+rol), la reactiva en vez de duplicar.
        var existing = await _db.ExternalDataSourceGrants
            .FirstOrDefaultAsync(g => g.ExternalDataSourceId == sourceId && g.TenantId == tenantId && g.RolId == rolId, ct);
        if (existing is not null)
        {
            if (!existing.IsEnabled)
            {
                existing.IsEnabled = true;
                _audit.Write(actorUserId, "external-grant.enable", nameof(ExternalDataSourceGrant), existing.Id,
                    previousValue: new { IsEnabled = false }, newValue: new { existing.ExternalDataSourceId, existing.TenantId, IsEnabled = true }, tenantId: tenantId);
                await _db.SaveChangesAsync(ct);
            }

            return existing.Id;
        }

        var grant = new ExternalDataSourceGrant
        {
            Id = Guid.CreateVersion7(),
            ExternalDataSourceId = sourceId,
            TenantId = tenantId,
            RolId = rolId,
            IsEnabled = true
        };
        _db.ExternalDataSourceGrants.Add(grant);
        _audit.Write(actorUserId, "external-grant.create", nameof(ExternalDataSourceGrant), grant.Id,
            previousValue: null, newValue: new { grant.ExternalDataSourceId, grant.TenantId, grant.RolId }, tenantId: tenantId);
        await _db.SaveChangesAsync(ct);
        return grant.Id;
    }

    public async Task<bool> RevokeAsync(Guid grantId, Guid actorUserId, CancellationToken ct = default)
    {
        var grant = await _db.ExternalDataSourceGrants.FirstOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null)
        {
            return false;
        }

        _db.ExternalDataSourceGrants.Remove(grant);
        _audit.Write(actorUserId, "external-grant.revoke", nameof(ExternalDataSourceGrant), grant.Id,
            previousValue: new { grant.ExternalDataSourceId, grant.TenantId, grant.RolId }, newValue: null, tenantId: grant.TenantId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
