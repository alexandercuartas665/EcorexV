using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Application.Scheduling;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Exportar/importar la DEFINICION de un contenedor a JSON (ADR-0132). No mueve datos (filas/celdas) ni
/// secretos (credenciales cifradas). Las referencias viajan por claves locales del paquete y por nombre de
/// columna; al importar se crean ids nuevos y se remapea todo. Tenant-scoped por el filtro global.
/// </summary>
public sealed class DataContainerPortabilityService : IDataContainerPortabilityService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public DataContainerPortabilityService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ---------------- EXPORT ----------------

    public async Task<DataExportResult?> ExportAsync(Guid modelId, CancellationToken ct = default)
    {
        var model = await _db.DataModels.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId, ct);
        if (model is null) { return null; }

        var tables = await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId == modelId)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
        var tableIds = tables.Select(t => t.Id).ToList();

        var columns = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => tableIds.Contains(c.ContainerId))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        var colsByTable = columns.GroupBy(c => c.ContainerId).ToDictionary(g => g.Key, g => g.ToList());

        var relations = await _db.DataModelRelations.AsNoTracking()
            .Where(r => r.ModelId == modelId).ToListAsync(ct);

        var connectors = await _db.DataConnectors.AsNoTracking()
            .Where(c => c.ModelId == modelId)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var processes = await _db.ImportProcesses.AsNoTracking()
            .Where(p => p.ModelId == modelId)
            .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var destination = await _db.DataDestinations.AsNoTracking().FirstOrDefaultAsync(d => d.ModelId == modelId, ct);

        // Claves locales estables.
        var tableKey = new Dictionary<Guid, string>();
        for (var i = 0; i < tables.Count; i++) { tableKey[tables[i].Id] = $"t{i + 1}"; }
        var connectorKey = new Dictionary<Guid, string>();
        for (var i = 0; i < connectors.Count; i++) { connectorKey[connectors[i].Id] = $"c{i + 1}"; }

        // columnId -> nombre, para traducir las referencias GUID de las derivadas a nombres.
        var colName = columns.ToDictionary(c => c.Id, c => c.Name);

        var portTables = new List<PortableTable>();
        foreach (var t in tables)
        {
            var cols = (colsByTable.TryGetValue(t.Id, out var cc) ? cc : new List<DataContainerColumn>())
                .Select(c => new PortableColumn(
                    c.Name, c.Description, c.Type, c.SortOrder, c.IsRequired,
                    c.Type == DataContainerColumnType.Submodel && c.ChildContainerId is Guid ch && tableKey.TryGetValue(ch, out var chk) ? chk : null))
                .ToList();

            string? parentFieldColumn = null;
            if (t.ParentFieldId is Guid pf) { colName.TryGetValue(pf, out parentFieldColumn); }

            string? derivedKey = null, derivedExtTable = null, derivedExtModel = null;
            if (t.DerivedFromContainerId is Guid src)
            {
                if (tableKey.TryGetValue(src, out var sk)) { derivedKey = sk; }
                else
                {
                    var ext = await _db.DataContainers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == src, ct);
                    derivedExtTable = ext?.Name;
                    if (ext?.ModelId is Guid em)
                    {
                        derivedExtModel = await _db.DataModels.AsNoTracking().Where(m => m.Id == em).Select(m => m.Name).FirstOrDefaultAsync(ct);
                    }
                }
            }

            portTables.Add(new PortableTable(
                tableKey[t.Id], t.Name, t.Description, t.CanvasX, t.CanvasY, cols,
                t.ParentContainerId is Guid pc && tableKey.TryGetValue(pc, out var pk) ? pk : null,
                parentFieldColumn, derivedKey, derivedExtTable, derivedExtModel));
        }

        var portRelations = relations
            .Where(r => tableKey.ContainsKey(r.FromTableId) && tableKey.ContainsKey(r.ToTableId))
            .Select(r => new PortableRelation(tableKey[r.FromTableId], tableKey[r.ToTableId], r.Kind, r.Name))
            .ToList();

        var portConnectors = new List<PortableConnector>();
        foreach (var con in connectors)
        {
            PortableDerivation? derivation = null;
            string? mappingJson = con.MappingJson;
            if (con.Kind == ConnectorKind.FromTable)
            {
                mappingJson = null; // la receta viaja en 'derivation', no en MappingJson (que lleva GUIDs).
                derivation = await BuildPortableDerivationAsync(con.MappingJson, tableKey, ct);
            }

            portConnectors.Add(new PortableConnector(
                connectorKey[con.Id], con.Name, con.Kind,
                con.EndpointUrl, con.HttpMethod, con.AuthKind,
                con.DbEngine, con.Host, con.Port, con.DatabaseName, con.Username,
                con.Query, con.HeadersJson, con.TokenExchangeJson,
                con.IsActive, con.CredentialsEncrypted is not null,
                con.ContainerId is Guid cid && tableKey.TryGetValue(cid, out var ck) ? ck : null,
                mappingJson, derivation));
        }

        // Clientes referenciados por los procesos: su id PUBLICO (no el secreto).
        var clientIds = processes.Where(p => p.ClientId is not null).Select(p => p.ClientId!.Value).Distinct().ToList();
        var clients = clientIds.Count == 0
            ? new List<DataClient>()
            : await _db.DataClients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToListAsync(ct);
        var clientById = clients.ToDictionary(c => c.Id);

        var portProcesses = processes.Select(p => new PortableProcess(
            p.Name,
            p.ConnectorId is Guid pcid && connectorKey.TryGetValue(pcid, out var pck) ? pck : null,
            p.ClientId is Guid clid && clientById.TryGetValue(clid, out var cl) ? cl.ClientId : null,
            p.ClientId is Guid clid2 && clientById.TryGetValue(clid2, out var cl2) ? cl2.Name : null,
            p.ScheduleKind, p.IntervalMinutes, p.CronExpression, p.IsActive, (ApiImportMode)p.Mode, p.KeyColumn))
            .ToList();

        PortableDestination? portDestination = destination is null ? null : new PortableDestination(
            destination.Kind, destination.DbEngine, destination.Host, destination.Port,
            destination.DatabaseName, destination.Username, destination.CredentialsEncrypted is not null);

        var package = new ContainerPackage(
            ContainerPackage.CurrentFormatVersion, model.Name, model.Description,
            portTables, portRelations, portConnectors, portProcesses, portDestination, DateTimeOffset.UtcNow);

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(package, Json));
        var safe = Slug(model.Name);
        return new DataExportResult($"contenedor-{safe}.json", bytes);
    }

    private async Task<PortableDerivation?> BuildPortableDerivationAsync(string? mappingJson, IReadOnlyDictionary<Guid, string> tableKey, CancellationToken ct)
    {
        var spec = DerivationSpec.FromJson(mappingJson);
        if (spec is null) { return null; }

        // Columnas de la tabla origen: id -> nombre (puede estar dentro o fuera del paquete).
        var srcCols = await _db.DataContainerColumns.AsNoTracking()
            .Where(c => c.ContainerId == spec.SourceTableId)
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var nameById = srcCols.ToDictionary(c => c.Id, c => c.Name);

        string? sourceKey = tableKey.TryGetValue(spec.SourceTableId, out var sk) ? sk : null;
        string? sourceExternal = null;
        if (sourceKey is null)
        {
            sourceExternal = await _db.DataContainers.AsNoTracking()
                .Where(c => c.Id == spec.SourceTableId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        }

        var groupBy = spec.GroupBy.Where(id => nameById.ContainsKey(id)).Select(id => nameById[id]).ToList();
        var aggs = spec.Aggregates.Select(a => new PortableAggregate(
            a.SourceColumnId is Guid g && nameById.TryGetValue(g, out var n) ? n : null, a.Fn, a.TargetColumnName)).ToList();
        var filters = spec.Filter?
            .Where(f => nameById.ContainsKey(f.ColumnId))
            .Select(f => new PortableFilter(nameById[f.ColumnId], f.Op, f.Value)).ToList();

        return new PortableDerivation(sourceKey, sourceExternal, groupBy, aggs,
            filters is { Count: > 0 } ? filters : null, spec.KeyColumnName);
    }

    // ---------------- IMPORT ----------------

    public async Task<ContainerImportResult> ImportAsync(string json, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { throw new InvalidOperationException("Sin tenant en contexto."); }

        ContainerPackage? package;
        try { package = JsonSerializer.Deserialize<ContainerPackage>(json, Json); }
        catch (JsonException ex) { throw new InvalidOperationException($"El archivo no es un paquete de contenedor valido: {ex.Message}"); }
        if (package is null || string.IsNullOrWhiteSpace(package.Name) || package.Tables is null)
        {
            throw new InvalidOperationException("El archivo no es un paquete de contenedor valido.");
        }

        var warnings = new List<string>();
        var name = await UniqueModelNameAsync(package.Name.Trim(), ct);

        var tz = ScheduledJobRecurrence.ResolveTimeZone(await _db.Tenants
            .Where(t => t.Id == tenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(ct));

        var joinTx = _db.HasActiveTransaction;
        var tx = joinTx ? null : await _db.BeginTransactionAsync(ct);
        try
        {
            var model = new DataModel { TenantId = tenantId, Name = name, Description = package.Description };
            _db.DataModels.Add(model);

            // 1) Tablas (id nuevo por clave). Parent/DerivedFrom se resuelven despues.
            var tableIdByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var tableByKey = new Dictionary<string, DataContainer>(StringComparer.Ordinal);
            foreach (var pt in package.Tables)
            {
                var table = new DataContainer
                {
                    TenantId = tenantId,
                    ModelId = model.Id,
                    Name = pt.Name,
                    Description = pt.Description,
                    CanvasX = pt.CanvasX,
                    CanvasY = pt.CanvasY
                };
                _db.DataContainers.Add(table);
                tableIdByKey[pt.Key] = table.Id;
                tableByKey[pt.Key] = table;
            }

            // 2) Columnas (Submodel resuelve su tabla hija por clave, ya conocida).
            var colIdByTableKeyName = new Dictionary<(string TableKey, string Col), Guid>();
            foreach (var pt in package.Tables)
            {
                var order = 0;
                foreach (var pc in pt.Columns)
                {
                    Guid? child = null;
                    if (pc.Type == DataContainerColumnType.Submodel && pc.ChildTableKey is { } chk && tableIdByKey.TryGetValue(chk, out var chid))
                    {
                        child = chid;
                    }
                    var col = new DataContainerColumn
                    {
                        TenantId = tenantId,
                        ContainerId = tableIdByKey[pt.Key],
                        Name = pc.Name,
                        Description = pc.Description,
                        Type = pc.Type,
                        SortOrder = pc.SortOrder == 0 ? order : pc.SortOrder,
                        IsRequired = pc.IsRequired,
                        ChildContainerId = child
                    };
                    _db.DataContainerColumns.Add(col);
                    colIdByTableKeyName[(pt.Key, pc.Name)] = col.Id;
                    order++;
                }
            }

            // 3) Parent (submodelo) y DerivedFrom (derivada) por referencia.
            foreach (var pt in package.Tables)
            {
                var table = tableByKey[pt.Key];
                if (pt.ParentKey is { } pk && tableIdByKey.TryGetValue(pk, out var parentId))
                {
                    table.ParentContainerId = parentId;
                    if (pt.ParentFieldColumn is { } pfc && colIdByTableKeyName.TryGetValue((pk, pfc), out var pfid))
                    {
                        table.ParentFieldId = pfid;
                    }
                }
                if (pt.DerivedFromKey is { } dk && tableIdByKey.TryGetValue(dk, out var srcId))
                {
                    table.DerivedFromContainerId = srcId;
                }
                else if (pt.DerivedFromExternalTable is { } ext)
                {
                    var match = await _db.DataContainers
                        .Where(c => c.Name == ext && c.ParentContainerId == null && c.DerivedFromContainerId == null)
                        .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
                    if (match is Guid mid) { table.DerivedFromContainerId = mid; }
                    else { warnings.Add($"La tabla derivada '{pt.Name}' apunta a un origen externo '{ext}' que no existe en este tenant: quedo sin origen."); }
                }
            }

            // 4) Relaciones.
            foreach (var pr in package.Relations)
            {
                if (tableIdByKey.TryGetValue(pr.FromKey, out var fromId) && tableIdByKey.TryGetValue(pr.ToKey, out var toId))
                {
                    _db.DataModelRelations.Add(new DataModelRelation
                    {
                        TenantId = tenantId,
                        ModelId = model.Id,
                        FromTableId = fromId,
                        ToTableId = toId,
                        Kind = pr.Kind,
                        Name = pr.Name
                    });
                }
            }

            // 5) Conectores (sin secretos; la receta de las derivadas se reconstruye con ids nuevos).
            var connectorIdByKey = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var pc in package.Connectors)
            {
                string? mappingJson = pc.MappingJson;
                var isActive = pc.IsActive;
                if (pc.Kind == ConnectorKind.FromTable && pc.Derivation is { } d)
                {
                    var (specJson, ok) = await RebuildDerivationJsonAsync(d, tableIdByKey, colIdByTableKeyName, ct);
                    mappingJson = specJson;
                    if (!ok)
                    {
                        isActive = false;
                        warnings.Add($"La derivada del conector '{pc.Name}' no pudo resolver su origen; quedo inactiva.");
                    }
                }

                var connector = new DataConnector
                {
                    TenantId = tenantId,
                    ModelId = model.Id,
                    Name = pc.Name,
                    Kind = pc.Kind,
                    EndpointUrl = pc.EndpointUrl,
                    HttpMethod = pc.HttpMethod,
                    AuthKind = pc.AuthKind,
                    DbEngine = pc.DbEngine,
                    Host = pc.Host,
                    Port = pc.Port,
                    DatabaseName = pc.DatabaseName,
                    Username = pc.Username,
                    Query = pc.Query,
                    HeadersJson = pc.HeadersJson,
                    TokenExchangeJson = pc.TokenExchangeJson,
                    MappingJson = mappingJson,
                    CredentialsEncrypted = null,
                    ContainerId = pc.ContainerKey is { } ck && tableIdByKey.TryGetValue(ck, out var tcid) ? tcid : null,
                    IsActive = isActive
                };
                _db.DataConnectors.Add(connector);
                connectorIdByKey[pc.Key] = connector.Id;

                if (pc.HadCredentials)
                {
                    warnings.Add($"El conector '{pc.Name}' usaba credenciales: vuelve a capturarlas (no se exportan por seguridad).");
                }
            }

            // 6) Clientes remotos referenciados: se casan por id publico; nunca se crea el secreto.
            var clientIdByPublic = new Dictionary<string, Guid>(StringComparer.Ordinal);

            // 7) Procesos (programacion). Un proceso cuya fuente necesita credenciales que no estan arranca inactivo.
            var connectorNeedsSecret = package.Connectors
                .Where(c => c.HadCredentials).Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var pp in package.Processes)
            {
                Guid? clientId = null;
                if (!string.IsNullOrWhiteSpace(pp.ClientPublicId))
                {
                    if (!clientIdByPublic.TryGetValue(pp.ClientPublicId, out var cid))
                    {
                        cid = await _db.DataClients.Where(c => c.ClientId == pp.ClientPublicId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct) ?? Guid.Empty;
                        clientIdByPublic[pp.ClientPublicId] = cid;
                    }
                    if (cid != Guid.Empty) { clientId = cid; }
                    else { warnings.Add($"El proceso '{pp.Name}' referenciaba el cliente remoto '{pp.ClientName ?? pp.ClientPublicId}', que no existe aqui: quedo sin cliente."); }
                }

                var active = pp.IsActive;
                if (pp.ConnectorKey is { } prk && connectorNeedsSecret.Contains(prk))
                {
                    active = false; // no disparar un horario que fallara por falta de credenciales.
                }

                var process = new ImportProcess
                {
                    TenantId = tenantId,
                    ModelId = model.Id,
                    ConnectorId = pp.ConnectorKey is { } pck2 && connectorIdByKey.TryGetValue(pck2, out var conId) ? conId : null,
                    ClientId = clientId,
                    Name = pp.Name,
                    ScheduleKind = pp.ScheduleKind,
                    IntervalMinutes = pp.ScheduleKind == ImportScheduleKind.Interval ? pp.IntervalMinutes : null,
                    CronExpression = pp.ScheduleKind == ImportScheduleKind.Cron ? pp.CronExpression : null,
                    Mode = (ImportRunMode)pp.Mode,
                    KeyColumn = pp.KeyColumn,
                    IsActive = active
                };
                if (active)
                {
                    var next = ImportRecurrence.ComputeNextRun(process, DateTimeOffset.UtcNow, tz);
                    process.NextRunAt = next.Problem == ImportScheduleProblem.Invalid ? null : next.NextRunAt;
                    if (next.Problem == ImportScheduleProblem.Invalid)
                    {
                        process.IsActive = false;
                        process.DisabledReason = next.Reason;
                        warnings.Add($"El proceso '{pp.Name}' tenia un horario invalido y quedo inactivo.");
                    }
                }
                _db.ImportProcesses.Add(process);
            }

            // 8) Destino (sin secreto).
            if (package.Destination is { } pd)
            {
                _db.DataDestinations.Add(new DataDestination
                {
                    TenantId = tenantId,
                    ModelId = model.Id,
                    Kind = pd.Kind,
                    DbEngine = pd.DbEngine,
                    Host = pd.Host,
                    Port = pd.Port,
                    DatabaseName = pd.DatabaseName,
                    Username = pd.Username,
                    CredentialsEncrypted = null
                });
                if (pd.HadCredentials)
                {
                    warnings.Add("El destino usaba credenciales: vuelve a capturarlas (no se exportan por seguridad).");
                }
            }

            await _db.SaveChangesAsync(ct);
            if (tx is not null) { await tx.CommitAsync(ct); }

            var dto = new DataModelDto(model.Id, model.Name, model.Description,
                package.Tables.Count, package.Relations.Count, DateTimeOffset.UtcNow);
            return new ContainerImportResult(dto, warnings);
        }
        catch
        {
            if (tx is not null) { await tx.RollbackAsync(ct); }
            throw;
        }
        finally
        {
            if (tx is not null) { await tx.DisposeAsync(); }
        }
    }

    /// <summary>Reconstruye el MappingJson (DerivationSpec) de una derivada con ids NUEVOS. Devuelve ok=false
    /// si no se pudo resolver la tabla origen (dentro del paquete o existente en el tenant por nombre).</summary>
    private async Task<(string? Json, bool Ok)> RebuildDerivationJsonAsync(
        PortableDerivation d,
        IReadOnlyDictionary<string, Guid> tableIdByKey,
        IReadOnlyDictionary<(string, string), Guid> colIdByTableKeyName,
        CancellationToken ct)
    {
        Guid sourceId;
        Func<string, Guid?> colByName;

        if (d.SourceKey is { } sk && tableIdByKey.TryGetValue(sk, out var inId))
        {
            sourceId = inId;
            colByName = n => colIdByTableKeyName.TryGetValue((sk, n), out var id) ? id : null;
        }
        else if (d.SourceExternalTable is { } ext)
        {
            var src = await _db.DataContainers
                .Where(c => c.Name == ext && c.DerivedFromContainerId == null)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (src is not Guid eid) { return (null, false); }
            sourceId = eid;
            var cols = await _db.DataContainerColumns.AsNoTracking()
                .Where(c => c.ContainerId == eid).Select(c => new { c.Id, c.Name }).ToListAsync(ct);
            var map = cols.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
            colByName = n => map.TryGetValue(n, out var id) ? id : null;
        }
        else
        {
            return (null, false);
        }

        var groupBy = d.GroupByColumns.Select(colByName).Where(x => x is not null).Select(x => x!.Value).ToList();
        var aggs = d.Aggregates.Select(a => new DerivationAggregate(
            a.SourceColumn is { } sc ? colByName(sc) : null, a.Fn, a.TargetColumnName)).ToList();
        var filters = d.Filter?
            .Select(f => (col: colByName(f.Column), f.Op, f.Value))
            .Where(x => x.col is not null)
            .Select(x => new DerivationFilter(x.col!.Value, x.Op, x.Value)).ToList();

        var spec = new DerivationSpec(sourceId, groupBy, aggs,
            filters is { Count: > 0 } ? filters : null, d.KeyColumnName);
        return (spec.ToJson(), true);
    }

    private async Task<string> UniqueModelNameAsync(string baseName, CancellationToken ct)
    {
        var name = baseName;
        if (!await _db.DataModels.AnyAsync(m => m.Name == name, ct)) { return name; }
        name = $"{baseName} (importado)";
        var n = 2;
        while (await _db.DataModels.AnyAsync(m => m.Name == name, ct))
        {
            name = $"{baseName} (importado {n})";
            n++;
        }
        return name;
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(ch); }
            else if (ch is ' ' or '-' or '_') { sb.Append('-'); }
        }
        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "contenedor" : slug;
    }
}
