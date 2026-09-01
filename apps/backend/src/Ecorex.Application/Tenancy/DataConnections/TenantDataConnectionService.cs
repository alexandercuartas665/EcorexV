using Ecorex.Application.Common;
using Ecorex.Application.Reporting.External;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy.DataConnections;

/// <summary>
/// Implementacion tenant-scoped de <see cref="ITenantDataConnectionService"/>. El aislamiento es EXPLICITO
/// por <see cref="ExternalDataSource.OwnerTenantId"/> == tenant activo: toda lectura/escritura/ejecucion
/// exige que la conexion (o la conexion del dataset) pertenezca al tenant actual. El secreto se cifra con
/// ISecretProtector y solo se descifra en memoria al ejecutar. La escritura se gobierna por AllowWrite.
/// </summary>
public sealed class TenantDataConnectionService : ITenantDataConnectionService
{
    private const int HardMaxRows = 5_000;

    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IAuditWriter _audit;
    private readonly IExternalQueryExecutor _executor;
    private readonly ITenantContext _tenant;

    public TenantDataConnectionService(
        IApplicationDbContext db, ISecretProtector protector, IAuditWriter audit,
        IExternalQueryExecutor executor, ITenantContext tenant)
    {
        _db = db;
        _protector = protector;
        _audit = audit;
        _executor = executor;
        _tenant = tenant;
    }

    private Guid Tenant => _tenant.TenantId ?? Guid.Empty;

    public async Task<IReadOnlyList<TenantDataConnectionSummary>> ListAsync(CancellationToken ct = default)
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty) { return Array.Empty<TenantDataConnectionSummary>(); }

        var sources = await _db.ExternalDataSources.AsNoTracking()
            .Where(s => s.OwnerTenantId == tenant).OrderBy(s => s.Name).ToListAsync(ct);
        var result = new List<TenantDataConnectionSummary>(sources.Count);
        foreach (var s in sources)
        {
            var count = await _db.ExternalDataSets.AsNoTracking().CountAsync(d => d.ExternalDataSourceId == s.Id, ct);
            result.Add(new TenantDataConnectionSummary(
                s.Id, s.Name, s.Provider, s.IsEnabled, !string.IsNullOrEmpty(s.ConnectionStringEncrypted),
                s.AllowWrite, s.LastValidatedAt, count));
        }
        return result;
    }

    public async Task<TenantDataConnectionDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await OwnedSourceAsync(id, tracking: false, ct);
        return s is null ? null : new TenantDataConnectionDetail(
            s.Id, s.Name, s.Description, s.Provider, !string.IsNullOrEmpty(s.ConnectionStringEncrypted),
            s.IsEnabled, s.AllowWrite, s.LastValidatedAt);
    }

    public async Task<Guid?> SaveAsync(Guid? id, SaveTenantDataConnectionRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty || string.IsNullOrWhiteSpace(request.Name)) { return null; }

        if (id is Guid existingId)
        {
            var s = await OwnedSourceAsync(existingId, tracking: true, ct);
            if (s is null) { return null; }
            s.Name = request.Name.Trim();
            s.Description = Trim(request.Description);
            s.Provider = request.Provider;
            s.IsEnabled = request.IsEnabled;
            s.AllowWrite = request.AllowWrite;
            s.IsReadOnly = !request.AllowWrite;
            if (!string.IsNullOrWhiteSpace(request.ConnectionString))
            {
                s.ConnectionStringEncrypted = _protector.Protect(request.ConnectionString.Trim());
                s.LastValidatedAt = null;
            }
            _audit.Write(actorUserId, "tenant-data-connection.update", nameof(ExternalDataSource), s.Id,
                previousValue: null,
                newValue: new { s.Name, s.Provider, s.IsEnabled, s.AllowWrite, HasConnectionString = s.ConnectionStringEncrypted is not null },
                tenantId: tenant);
            await _db.SaveChangesAsync(ct);
            return s.Id;
        }

        var source = new ExternalDataSource
        {
            Id = Guid.CreateVersion7(),
            OwnerTenantId = tenant,
            Name = request.Name.Trim(),
            Description = Trim(request.Description),
            Provider = request.Provider,
            IsEnabled = request.IsEnabled,
            AllowWrite = request.AllowWrite,
            IsReadOnly = !request.AllowWrite
        };
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            source.ConnectionStringEncrypted = _protector.Protect(request.ConnectionString.Trim());
        }
        _db.ExternalDataSources.Add(source);
        _audit.Write(actorUserId, "tenant-data-connection.create", nameof(ExternalDataSource), source.Id,
            previousValue: null,
            newValue: new { source.Name, source.Provider, source.IsEnabled, source.AllowWrite, HasConnectionString = source.ConnectionStringEncrypted is not null },
            tenantId: tenant);
        await _db.SaveChangesAsync(ct);
        return source.Id;
    }

    public async Task<bool> SetEnabledAsync(Guid id, bool enabled, Guid actorUserId, CancellationToken ct = default)
    {
        var s = await OwnedSourceAsync(id, tracking: true, ct);
        if (s is null) { return false; }
        if (s.IsEnabled == enabled) { return true; }
        s.IsEnabled = enabled;
        _audit.Write(actorUserId, enabled ? "tenant-data-connection.enable" : "tenant-data-connection.disable",
            nameof(ExternalDataSource), s.Id, previousValue: new { IsEnabled = !enabled }, newValue: new { IsEnabled = enabled },
            tenantId: Tenant);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var s = await OwnedSourceAsync(id, tracking: true, ct);
        if (s is null) { return false; }
        var datasets = await _db.ExternalDataSets.Where(d => d.ExternalDataSourceId == s.Id).ToListAsync(ct);
        _db.ExternalDataSets.RemoveRange(datasets);
        _db.ExternalDataSources.Remove(s);
        _audit.Write(actorUserId, "tenant-data-connection.delete", nameof(ExternalDataSource), s.Id,
            previousValue: new { s.Name }, newValue: null, tenantId: Tenant);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> TestConnectionAsync(Guid id, SaveTenantDataConnectionRequest? request, Guid actorUserId, CancellationToken ct = default)
    {
        var s = await OwnedSourceAsync(id, tracking: true, ct);
        if (s is null) { return "Conexion no encontrada."; }

        string? conn;
        if (!string.IsNullOrWhiteSpace(request?.ConnectionString))
        {
            conn = request.ConnectionString.Trim();
        }
        else if (!string.IsNullOrEmpty(s.ConnectionStringEncrypted))
        {
            try { conn = _protector.Unprotect(s.ConnectionStringEncrypted); }
            catch { return "La cadena esta cifrada con una version anterior. Vuelve a guardarla."; }
        }
        else { return "No hay cadena de conexion configurada."; }

        var provider = request?.Provider ?? s.Provider;
        var error = await _executor.TestConnectionAsync(provider, conn!, ct);
        if (error is null)
        {
            s.LastValidatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return error;
    }

    public async Task<IReadOnlyList<TenantDatasetSummary>> ListDatasetsAsync(Guid connectionId, CancellationToken ct = default)
    {
        var s = await OwnedSourceAsync(connectionId, tracking: false, ct);
        if (s is null) { return Array.Empty<TenantDatasetSummary>(); }
        var rows = await _db.ExternalDataSets.AsNoTracking()
            .Where(d => d.ExternalDataSourceId == connectionId).OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.Description, d.IsEnabled, d.ParametersJson, d.AgentEnabled })
            .ToListAsync(ct);
        return rows.Select(d => new TenantDatasetSummary(
            d.Id, d.Name, d.Description, d.IsEnabled,
            ExternalDataJson.DeserializeParameters(d.ParametersJson).Count, d.AgentEnabled)).ToList();
    }

    public async Task<TenantDatasetDetail?> GetDatasetAsync(Guid id, CancellationToken ct = default)
    {
        var d = await OwnedDatasetAsync(id, tracking: false, ct);
        return d is null ? null : new TenantDatasetDetail(
            d.Id, d.ExternalDataSourceId, d.Name, d.Description, d.CommandText, d.IsEnabled, d.ParametersJson, d.AgentEnabled);
    }

    public async Task<Guid?> SaveDatasetAsync(SaveTenantDatasetRequest request, Guid actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.CommandText)) { return null; }
        var owner = await OwnedSourceAsync(request.ConnectionId, tracking: false, ct);
        if (owner is null) { return null; }

        if (request.Id is Guid dsId)
        {
            var d = await OwnedDatasetAsync(dsId, tracking: true, ct);
            if (d is null) { return null; }
            d.Name = request.Name.Trim();
            d.Description = Trim(request.Description);
            d.CommandText = request.CommandText.Trim();
            d.ParametersJson = Trim(request.ParametersJson);
            d.IsEnabled = request.IsEnabled;
            d.AgentEnabled = request.AgentEnabled;
            _audit.Write(actorUserId, "tenant-data-dataset.update", nameof(ExternalDataSet), d.Id,
                previousValue: null, newValue: new { d.Name, d.IsEnabled }, tenantId: Tenant);
            await _db.SaveChangesAsync(ct);
            return d.Id;
        }

        var ds = new ExternalDataSet
        {
            Id = Guid.CreateVersion7(),
            ExternalDataSourceId = request.ConnectionId,
            Name = request.Name.Trim(),
            Description = Trim(request.Description),
            CommandText = request.CommandText.Trim(),
            ParametersJson = Trim(request.ParametersJson),
            IsEnabled = request.IsEnabled,
            AgentEnabled = request.AgentEnabled
        };
        _db.ExternalDataSets.Add(ds);
        _audit.Write(actorUserId, "tenant-data-dataset.create", nameof(ExternalDataSet), ds.Id,
            previousValue: null, newValue: new { ds.Name, ds.IsEnabled }, tenantId: Tenant);
        await _db.SaveChangesAsync(ct);
        return ds.Id;
    }

    public async Task<bool> DeleteDatasetAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        var d = await OwnedDatasetAsync(id, tracking: true, ct);
        if (d is null) { return false; }
        _db.ExternalDataSets.Remove(d);
        _audit.Write(actorUserId, "tenant-data-dataset.delete", nameof(ExternalDataSet), d.Id,
            previousValue: new { d.Name }, newValue: null, tenantId: Tenant);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<TenantQueryResult> RunQueryAsync(Guid connectionId, string sql, int maxRows, Guid actorUserId, CancellationToken ct = default)
    {
        var s = await OwnedSourceAsync(connectionId, tracking: false, ct);
        if (s is null) { return new TenantQueryResult(false, null, "Conexion no encontrada."); }
        return await ExecuteOnAsync(s, sql, maxRows, actorUserId, "run-query", ct);
    }

    public async Task<TenantQueryResult> RunDatasetAsync(Guid datasetId, IReadOnlyDictionary<string, string?>? inputs, int maxRows, Guid actorUserId, CancellationToken ct = default)
    {
        var d = await OwnedDatasetAsync(datasetId, tracking: false, ct);
        if (d is null) { return new TenantQueryResult(false, null, "Dataset no encontrado."); }
        var s = await OwnedSourceAsync(d.ExternalDataSourceId, tracking: false, ct);
        if (s is null) { return new TenantQueryResult(false, null, "Conexion no encontrada."); }

        // Parametros declarados -> enlazados TIPADOS con los valores provistos (o el DefaultValue).
        var declared = ExternalDataJson.DeserializeParameters(d.ParametersJson);
        var context = new ExternalRunContext(Tenant, actorUserId == Guid.Empty ? null : actorUserId);
        IReadOnlyList<ExternalBoundParameter> bound;
        try { bound = ExternalParameterBinder.Bind(declared, context, inputs); }
        catch (Exception ex) { return new TenantQueryResult(false, null, "Parametro invalido: " + ex.Message); }

        return await ExecuteOnAsync(s, d.CommandText, maxRows, actorUserId, "run-dataset:" + d.Name, ct, bound);
    }

    // ---- superficie para AGENTES (solo datasets AgentEnabled + IsEnabled) ----

    public async Task<IReadOnlyList<AgentDatasetInfo>> AgentListDatasetsAsync(CancellationToken ct = default)
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty) { return Array.Empty<AgentDatasetInfo>(); }
        var rows = await (
            from d in _db.ExternalDataSets.AsNoTracking()
            join s in _db.ExternalDataSources.AsNoTracking() on d.ExternalDataSourceId equals s.Id
            where s.OwnerTenantId == tenant && s.IsEnabled && d.IsEnabled && d.AgentEnabled
            orderby s.Name, d.Name
            select new { d.Id, d.Name, d.Description, d.ParametersJson, ConnName = s.Name })
            .ToListAsync(ct);
        return rows.Select(r => new AgentDatasetInfo(
            r.Id, r.Name, r.Description, r.ConnName,
            ExternalDataJson.DeserializeParameters(r.ParametersJson).Count)).ToList();
    }

    public async Task<AgentDatasetDetail?> AgentGetDatasetAsync(Guid datasetId, CancellationToken ct = default)
    {
        var row = await AgentDatasetRowAsync(datasetId, ct);
        if (row is null) { return null; }
        var ps = ExternalDataJson.DeserializeParameters(row.ParametersJson)
            .Select(p => new AgentDatasetParam(p.Name, p.Type.ToString(), p.Description, p.DefaultValue)).ToList();
        return new AgentDatasetDetail(row.Id, row.Name, row.Description, row.ConnName, ps);
    }

    public async Task<TenantQueryResult> AgentRunDatasetAsync(Guid datasetId, IReadOnlyDictionary<string, string?>? inputs, int maxRows, Guid actorUserId, CancellationToken ct = default)
    {
        // Gate del agente: el dataset debe estar EXPUESTO (AgentEnabled) y habilitado. Luego reusa la
        // ejecucion normal (que respeta el AllowWrite de la conexion, segun lo decidido).
        var row = await AgentDatasetRowAsync(datasetId, ct);
        if (row is null) { return new TenantQueryResult(false, null, "Dataset no disponible para agentes."); }
        return await RunDatasetAsync(datasetId, inputs, maxRows, actorUserId, ct);
    }

    private async Task<AgentDatasetRow?> AgentDatasetRowAsync(Guid datasetId, CancellationToken ct)
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty) { return null; }
        return await (
            from d in _db.ExternalDataSets.AsNoTracking()
            join s in _db.ExternalDataSources.AsNoTracking() on d.ExternalDataSourceId equals s.Id
            where d.Id == datasetId && s.OwnerTenantId == tenant && s.IsEnabled && d.IsEnabled && d.AgentEnabled
            select new AgentDatasetRow(d.Id, d.Name, d.Description, d.ParametersJson, s.Name))
            .FirstOrDefaultAsync(ct);
    }

    private sealed record AgentDatasetRow(Guid Id, string Name, string? Description, string? ParametersJson, string ConnName);

    // ---- helpers ----

    private async Task<TenantQueryResult> ExecuteOnAsync(
        ExternalDataSource s, string sql, int maxRows, Guid actorUserId, string what, CancellationToken ct,
        IReadOnlyList<ExternalBoundParameter>? boundParams = null)
    {
        if (string.IsNullOrWhiteSpace(sql)) { return new TenantQueryResult(false, null, "La consulta esta vacia."); }
        if (!s.IsEnabled) { return new TenantQueryResult(false, null, "La conexion esta deshabilitada."); }
        if (string.IsNullOrEmpty(s.ConnectionStringEncrypted)) { return new TenantQueryResult(false, null, "La conexion no tiene cadena configurada."); }

        string conn;
        try { conn = _protector.Unprotect(s.ConnectionStringEncrypted); }
        catch { return new TenantQueryResult(false, null, "La cadena esta cifrada con una version anterior. Vuelve a guardarla."); }

        var cap = Math.Clamp(maxRows <= 0 ? 500 : maxRows, 1, HardMaxRows);
        // Auditar la ejecucion (que conexion, si escribe). No se registra el SQL completo por prudencia.
        _audit.Write(actorUserId, "tenant-data-connection." + what, nameof(ExternalDataSource), s.Id,
            previousValue: null, newValue: new { s.Name, s.AllowWrite, MaxRows = cap }, tenantId: Tenant);
        await _db.SaveChangesAsync(ct);

        try
        {
            var query = new ExternalQuery(
                s.Provider, conn, sql.Trim(), boundParams ?? Array.Empty<ExternalBoundParameter>(),
                MaxRows: cap, TimeoutSeconds: 60, AllowWrite: s.AllowWrite);
            var data = await _executor.ExecuteAsync(query, ct);

            var columns = data.Columns.Select(c => string.IsNullOrWhiteSpace(c.DisplayName) ? c.Key : c.DisplayName).ToList();
            var rows = data.Rows
                .Select(r => (IReadOnlyList<string?>)r.Select(v => v?.ToString()).ToList())
                .ToList();
            var grid = new ExternalQueryGrid(columns, rows, data.RowCount, data.RowCount >= cap);
            return new TenantQueryResult(true, grid, null);
        }
        catch (Exception ex)
        {
            return new TenantQueryResult(false, null, ex.Message);
        }
    }

    private async Task<ExternalDataSource?> OwnedSourceAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty) { return null; }
        var q = tracking ? _db.ExternalDataSources : _db.ExternalDataSources.AsNoTracking();
        return await q.FirstOrDefaultAsync(s => s.Id == id && s.OwnerTenantId == tenant, ct);
    }

    private async Task<ExternalDataSet?> OwnedDatasetAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var tenant = Tenant;
        if (tenant == Guid.Empty) { return null; }
        var q = tracking ? _db.ExternalDataSets : _db.ExternalDataSets.AsNoTracking();
        // El dataset pertenece al tenant si su fuente es propiedad del tenant.
        return await q.FirstOrDefaultAsync(
            d => d.Id == id && _db.ExternalDataSources.Any(s => s.Id == d.ExternalDataSourceId && s.OwnerTenantId == tenant), ct);
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
