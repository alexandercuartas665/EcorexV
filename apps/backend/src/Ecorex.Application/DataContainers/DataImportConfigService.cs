using Ecorex.Application.Common;
using Ecorex.Application.Scheduling;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.DataContainers;

/// <summary>
/// Servicio de configuracion de importacion de un contenedor: conectores (con credenciales
/// cifradas), clientes remotos (ClientId publico + secreto cifrado) y procesos (horarios).
/// Solo CONFIGURACION en esta fase (no hay motor de ejecucion). Tenant-scoped por el filtro
/// global. Las credenciales y secretos NUNCA se devuelven en claro salvo el secreto del cliente,
/// que se muestra UNA sola vez al crearlo o rotarlo.
/// </summary>
public sealed class DataImportConfigService : IDataImportConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _protector;
    private readonly Agents.IAgentClientService _agentClients;

    public DataImportConfigService(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector protector,
        Agents.IAgentClientService agentClients)
    {
        _db = db;
        _tenantContext = tenantContext;
        _protector = protector;
        _agentClients = agentClients;
    }

    // ---- Conectores (por contenedor/modelo) ----

    public async Task<IReadOnlyList<DataConnectorDto>> ListConnectorsAsync(Guid modelId, CancellationToken ct = default)
    {
        var connectors = await _db.DataConnectors.AsNoTracking()
            .Where(c => c.ModelId == modelId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        var tableNames = await TableNamesAsync(modelId, ct);
        return connectors.Select(c => MapConnector(c, tableNames)).ToList();
    }

    /// <summary>Nombre de cada tabla del contenedor, para pintar a donde alimenta cada conector.</summary>
    private async Task<Dictionary<Guid, string>> TableNamesAsync(Guid modelId, CancellationToken ct) =>
        await _db.DataContainers.AsNoTracking()
            .Where(c => c.ModelId == modelId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    public async Task<DataConnectorDto?> SaveConnectorAsync(SaveDataConnectorRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { return null; }

        DataConnector entity;
        if (req.Id is { } id)
        {
            var existing = await _db.DataConnectors.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            // Validar que el contenedor (modelo) exista antes de anclar el conector.
            if (!await _db.DataModels.AnyAsync(m => m.Id == req.ModelId, ct)) { return null; }
            entity = new DataConnector
            {
                TenantId = tenantId,
                ModelId = req.ModelId
            };
            _db.DataConnectors.Add(entity);
        }

        entity.ModelId = req.ModelId;
        entity.Name = name;
        entity.Kind = req.Kind;
        entity.MappingJson = string.IsNullOrWhiteSpace(req.MappingJson) ? null : req.MappingJson;
        entity.IsActive = req.IsActive;

        // Tabla destino: hasta ahora el conector no decia a donde alimentaba (se elegia al pulsar
        // "Importar"), asi que su ficha no podia mostrarlo. Se valida que la tabla sea DE ESTE
        // contenedor: apuntar a la tabla de otro seria una fuga entre contenedores.
        if (req.ContainerId is Guid targetId)
        {
            var belongs = await _db.DataContainers.AnyAsync(c => c.Id == targetId && c.ModelId == req.ModelId, ct);
            if (!belongs) { return null; }
            entity.ContainerId = targetId;
        }
        else
        {
            entity.ContainerId = null;
        }

        // Campos segun el esquema de alimentacion; se limpian los del resto de esquemas.
        switch (req.Kind)
        {
            case ConnectorKind.RestApi:
                entity.EndpointUrl = string.IsNullOrWhiteSpace(req.EndpointUrl) ? null : req.EndpointUrl!.Trim();
                entity.HttpMethod = string.IsNullOrWhiteSpace(req.HttpMethod) ? null : req.HttpMethod!.Trim();
                entity.AuthKind = req.AuthKind;
                entity.DbEngine = null;
                entity.Host = null;
                entity.Port = null;
                entity.DatabaseName = null;
                entity.Username = null;
                entity.Query = null;
                break;
            case ConnectorKind.Database:
                entity.DbEngine = req.DbEngine;
                entity.Host = string.IsNullOrWhiteSpace(req.Host) ? null : req.Host!.Trim();
                entity.Port = req.Port;
                entity.DatabaseName = string.IsNullOrWhiteSpace(req.DatabaseName) ? null : req.DatabaseName!.Trim();
                entity.Username = string.IsNullOrWhiteSpace(req.Username) ? null : req.Username!.Trim();
                entity.Query = string.IsNullOrWhiteSpace(req.Query) ? null : req.Query!.Trim();
                entity.EndpointUrl = null;
                entity.HttpMethod = null;
                entity.AuthKind = ConnectorAuthKind.None;
                break;
            case ConnectorKind.Excel:
            default:
                entity.EndpointUrl = null;
                entity.HttpMethod = null;
                entity.AuthKind = ConnectorAuthKind.None;
                entity.DbEngine = null;
                entity.Host = null;
                entity.Port = null;
                entity.DatabaseName = null;
                entity.Username = null;
                entity.Query = null;
                break;
        }

        // Credenciales: si vienen en claro se cifran; si es edicion y llegan vacias, se conservan.
        if (!string.IsNullOrWhiteSpace(req.Credentials))
        {
            entity.CredentialsEncrypted = _protector.Protect(req.Credentials!);
        }

        await _db.SaveChangesAsync(ct);
        return MapConnector(entity, await TableNamesAsync(req.ModelId, ct));
    }

    public async Task<bool> DeleteConnectorAsync(Guid connectorId, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataConnectors.FirstOrDefaultAsync(c => c.Id == connectorId, ct);
        if (entity is null) { return false; }
        _db.DataConnectors.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Destino (1:1 con el contenedor/modelo) ----

    public async Task<DataDestinationDto?> GetDestinationAsync(Guid modelId, CancellationToken ct = default)
    {
        var d = await _db.DataDestinations.AsNoTracking().FirstOrDefaultAsync(x => x.ModelId == modelId, ct);
        return d is null ? null : MapDestination(d);
    }

    public async Task<DataDestinationDto?> SaveDestinationAsync(SaveDataDestinationRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }

        var entity = await _db.DataDestinations.FirstOrDefaultAsync(x => x.ModelId == req.ModelId, ct);
        if (entity is null)
        {
            // Validar que el contenedor (modelo) exista antes de crear su destino 1:1.
            if (!await _db.DataModels.AnyAsync(m => m.Id == req.ModelId, ct)) { return null; }
            entity = new DataDestination
            {
                TenantId = tenantId,
                ModelId = req.ModelId
            };
            _db.DataDestinations.Add(entity);
        }

        entity.Kind = req.Kind;
        if (req.Kind == DestinationKind.AlliedDatabase)
        {
            entity.DbEngine = req.DbEngine;
            entity.Host = string.IsNullOrWhiteSpace(req.Host) ? null : req.Host!.Trim();
            entity.Port = req.Port;
            entity.DatabaseName = string.IsNullOrWhiteSpace(req.DatabaseName) ? null : req.DatabaseName!.Trim();
            entity.Username = string.IsNullOrWhiteSpace(req.Username) ? null : req.Username!.Trim();
            // Credenciales: si vienen en claro se cifran; si es edicion y llegan vacias, se conservan.
            if (!string.IsNullOrWhiteSpace(req.Credentials))
            {
                entity.CredentialsEncrypted = _protector.Protect(req.Credentials!);
            }
        }
        else
        {
            // Sistema: no se guarda BD aliada.
            entity.DbEngine = null;
            entity.Host = null;
            entity.Port = null;
            entity.DatabaseName = null;
            entity.Username = null;
            entity.CredentialsEncrypted = null;
        }

        await _db.SaveChangesAsync(ct);
        return MapDestination(entity);
    }

    // ---- Clientes (por tenant) ----
    // El ciclo de vida de los clientes/agentes colmena vive ahora en su propio modulo
    // (Agents.IAgentClientService, ADR-0045). Estos metodos DELEGAN y mapean a los DTOs de este modulo,
    // para no romper a los consumidores actuales (Contenedores/Extraccion) mientras migran al selector.

    public async Task<IReadOnlyList<DataClientDto>> ListClientsAsync(CancellationToken ct = default)
        => (await _agentClients.ListAsync(ct)).Select(ToDataClientDto).ToList();

    public async Task<(DataClientDto Client, DataClientSecretDto? Secret)> SaveClientAsync(SaveDataClientRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var (client, secret) = await _agentClients.SaveAsync(
            new Agents.SaveAgentClientRequest(req.Id, req.Name, req.Description, req.IsActive), actorUserId, ct);
        return (ToDataClientDto(client), secret is null ? null : new DataClientSecretDto(secret.Id, secret.ClientId, secret.ClientSecret));
    }

    public async Task<DataClientSecretDto?> RotateClientSecretAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
    {
        var secret = await _agentClients.RotateSecretAsync(clientId, actorUserId, ct);
        return secret is null ? null : new DataClientSecretDto(secret.Id, secret.ClientId, secret.ClientSecret);
    }

    public Task<bool> DeleteClientAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
        => _agentClients.DeleteAsync(clientId, actorUserId, ct);

    private static DataClientDto ToDataClientDto(Agents.AgentClientDto c) =>
        new(c.Id, c.Name, c.Description, c.ClientId, c.HasSecret, c.IsActive);

    // ---- Procesos de importacion (por contenedor/modelo) ----

    public async Task<IReadOnlyList<ImportProcessDto>> ListProcessesAsync(Guid modelId, CancellationToken ct = default)
    {
        var processes = await _db.ImportProcesses.AsNoTracking()
            .Where(p => p.ModelId == modelId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        if (processes.Count == 0) { return Array.Empty<ImportProcessDto>(); }

        var connectorIds = processes.Where(p => p.ConnectorId is not null).Select(p => p.ConnectorId!.Value).Distinct().ToList();
        var clientIds = processes.Where(p => p.ClientId is not null).Select(p => p.ClientId!.Value).Distinct().ToList();

        var connectorNames = connectorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.DataConnectors.AsNoTracking()
                .Where(c => connectorIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var clientNames = clientIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.DataClients.AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return processes.Select(p => MapProcess(p, connectorNames, clientNames)).ToList();
    }

    public async Task<ImportProcessDto?> SaveProcessAsync(SaveImportProcessRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return null; }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { return null; }

        ImportProcess entity;
        if (req.Id is { } id)
        {
            var existing = await _db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (existing is null) { return null; }
            entity = existing;
        }
        else
        {
            if (!await _db.DataModels.AnyAsync(m => m.Id == req.ModelId, ct)) { return null; }
            entity = new ImportProcess
            {
                TenantId = tenantId,
                ModelId = req.ModelId
            };
            _db.ImportProcesses.Add(entity);
        }

        entity.ModelId = req.ModelId;
        entity.ConnectorId = req.ConnectorId;
        entity.ClientId = req.ClientId;
        entity.Name = name;
        entity.ScheduleKind = req.ScheduleKind;
        entity.IntervalMinutes = req.IntervalMinutes;
        entity.CronExpression = string.IsNullOrWhiteSpace(req.CronExpression) ? null : req.CronExpression!.Trim();
        entity.IsActive = req.IsActive;

        // La proxima ventana se calcula AL GUARDAR y no se deja para el worker: si no, el operador
        // guarda "cada 15 minutos" y la ficha no le dice cuando corre: tendria que esperar un ciclo
        // para verlo. Ademas un cron invalido se rechaza AQUI, mientras esta mirando, en vez de
        // desactivarse solo mas tarde.
        var tz = ScheduledJobRecurrence.ResolveTimeZone(await _db.Tenants
            .Where(t => t.Id == tenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(ct));
        var next = ImportRecurrence.ComputeNextRun(entity, DateTimeOffset.UtcNow, tz);
        if (next.Problem == ImportScheduleProblem.Invalid)
        {
            throw new InvalidOperationException(next.Reason ?? "El horario no es valido.");
        }
        entity.NextRunAt = next.NextRunAt;
        entity.DisabledReason = null;

        await _db.SaveChangesAsync(ct);

        var connectorNames = new Dictionary<Guid, string>();
        if (entity.ConnectorId is { } cn)
        {
            var nm = await _db.DataConnectors.AsNoTracking().Where(c => c.Id == cn).Select(c => c.Name).FirstOrDefaultAsync(ct);
            if (nm is not null) { connectorNames[cn] = nm; }
        }
        var clientNames = new Dictionary<Guid, string>();
        if (entity.ClientId is { } cl)
        {
            var nm = await _db.DataClients.AsNoTracking().Where(c => c.Id == cl).Select(c => c.Name).FirstOrDefaultAsync(ct);
            if (nm is not null) { clientNames[cl] = nm; }
        }
        return MapProcess(entity, connectorNames, clientNames);
    }

    public async Task<bool> DeleteProcessAsync(Guid processId, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.ImportProcesses.FirstOrDefaultAsync(p => p.Id == processId, ct);
        if (entity is null) { return false; }
        // La bitacora apunta al proceso con FK Restrict (su historia sobrevive al proceso). Borrar el
        // proceso sin quitar sus corridas fallaria contra esa FK: primero la historia, luego el dueno.
        var runs = await _db.ImportRuns.Where(r => r.ProcessId == processId).ToListAsync(ct);
        if (runs.Count > 0) { _db.ImportRuns.RemoveRange(runs); }
        _db.ImportProcesses.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ImportRunDto>> ListRunsAsync(Guid processId, int take = 10, CancellationToken ct = default)
        => await _db.ImportRuns.AsNoTracking()
            .Where(r => r.ProcessId == processId)
            .OrderByDescending(r => r.FiredAt)
            .Take(take)
            .Select(r => new ImportRunDto(r.Id, r.FiredAt, r.Trigger, r.Result,
                r.Inserted, r.Updated, r.Deleted, r.Detail, r.FinishedAt))
            .ToListAsync(ct);

    // ---- Helpers ----

    /// <param name="tableNames">
    /// Nombres de las tablas del contenedor, para que la ficha diga a donde alimenta sin otra
    /// consulta. Mismo patron que <see cref="MapProcess"/> con sus diccionarios.
    /// </param>
    private static DataConnectorDto MapConnector(DataConnector c, IReadOnlyDictionary<Guid, string>? tableNames = null) =>
        new(c.Id, c.ModelId ?? Guid.Empty, c.Name, c.Kind,
            c.EndpointUrl, c.HttpMethod, c.AuthKind,
            c.DbEngine, c.Host, c.Port, c.DatabaseName, c.Username, c.Query,
            c.CredentialsEncrypted != null, c.MappingJson, c.IsActive,
            c.ContainerId,
            c.ContainerId is Guid t && tableNames is not null && tableNames.TryGetValue(t, out var n) ? n : null);

    private static DataDestinationDto MapDestination(DataDestination d) =>
        new(d.ModelId, d.Kind, d.DbEngine, d.Host, d.Port, d.DatabaseName, d.Username,
            d.CredentialsEncrypted != null);

    private static ImportProcessDto MapProcess(
        ImportProcess p,
        IReadOnlyDictionary<Guid, string> connectorNames,
        IReadOnlyDictionary<Guid, string> clientNames)
    {
        string? connectorName = null;
        if (p.ConnectorId is { } cn && connectorNames.TryGetValue(cn, out var conNm)) { connectorName = conNm; }
        string? clientName = null;
        if (p.ClientId is { } cl && clientNames.TryGetValue(cl, out var cliNm)) { clientName = cliNm; }
        return new ImportProcessDto(
            p.Id, p.ModelId ?? Guid.Empty, p.ConnectorId, connectorName, p.ClientId, clientName,
            p.Name, p.ScheduleKind, p.IntervalMinutes, p.CronExpression, p.IsActive, p.LastRunAt,
            p.NextRunAt, p.DisabledReason, p.PendingSince);
    }

}
