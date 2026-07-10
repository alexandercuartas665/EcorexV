using System.Security.Cryptography;
using Ecorex.Application.Common;
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

    public DataImportConfigService(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector protector)
    {
        _db = db;
        _tenantContext = tenantContext;
        _protector = protector;
    }

    // ---- Conectores (por contenedor/modelo) ----

    public async Task<IReadOnlyList<DataConnectorDto>> ListConnectorsAsync(Guid modelId, CancellationToken ct = default)
    {
        var connectors = await _db.DataConnectors.AsNoTracking()
            .Where(c => c.ModelId == modelId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return connectors.Select(MapConnector).ToList();
    }

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
                break;
            case ConnectorKind.Database:
                entity.DbEngine = req.DbEngine;
                entity.Host = string.IsNullOrWhiteSpace(req.Host) ? null : req.Host!.Trim();
                entity.Port = req.Port;
                entity.DatabaseName = string.IsNullOrWhiteSpace(req.DatabaseName) ? null : req.DatabaseName!.Trim();
                entity.Username = string.IsNullOrWhiteSpace(req.Username) ? null : req.Username!.Trim();
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
                break;
        }

        // Credenciales: si vienen en claro se cifran; si es edicion y llegan vacias, se conservan.
        if (!string.IsNullOrWhiteSpace(req.Credentials))
        {
            entity.CredentialsEncrypted = _protector.Protect(req.Credentials!);
        }

        await _db.SaveChangesAsync(ct);
        return MapConnector(entity);
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

    public async Task<IReadOnlyList<DataClientDto>> ListClientsAsync(CancellationToken ct = default)
    {
        var clients = await _db.DataClients.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return clients.Select(MapClient).ToList();
    }

    public async Task<(DataClientDto Client, DataClientSecretDto? Secret)> SaveClientAsync(SaveDataClientRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("No hay tenant activo.");
        }
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("El nombre del cliente es obligatorio.");
        }

        if (req.Id is { } id)
        {
            // Edicion: no se regenera identidad ni secreto.
            var existing = await _db.DataClients.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (existing is null)
            {
                throw new InvalidOperationException("El cliente no existe.");
            }
            existing.Name = name;
            existing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim();
            existing.IsActive = req.IsActive;
            await _db.SaveChangesAsync(ct);
            return (MapClient(existing), null);
        }

        // Alta: genera ClientId publico unico por tenant + secreto fuerte (mostrado una vez).
        var clientId = await GenerateUniqueClientIdAsync(ct);
        var secret = GenerateSecret();
        var entity = new DataClient
        {
            TenantId = tenantId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim(),
            ClientId = clientId,
            ClientSecretEncrypted = _protector.Protect(secret),
            IsActive = req.IsActive
        };
        _db.DataClients.Add(entity);
        await _db.SaveChangesAsync(ct);

        return (MapClient(entity), new DataClientSecretDto(entity.Id, entity.ClientId, secret));
    }

    public async Task<DataClientSecretDto?> RotateClientSecretAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataClients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (entity is null) { return null; }
        var secret = GenerateSecret();
        entity.ClientSecretEncrypted = _protector.Protect(secret);
        await _db.SaveChangesAsync(ct);
        return new DataClientSecretDto(entity.Id, entity.ClientId, secret);
    }

    public async Task<bool> DeleteClientAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataClients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (entity is null) { return false; }
        _db.DataClients.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

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
        _db.ImportProcesses.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Helpers ----

    private static DataConnectorDto MapConnector(DataConnector c) =>
        new(c.Id, c.ModelId ?? Guid.Empty, c.Name, c.Kind,
            c.EndpointUrl, c.HttpMethod, c.AuthKind,
            c.DbEngine, c.Host, c.Port, c.DatabaseName, c.Username,
            c.CredentialsEncrypted != null, c.MappingJson, c.IsActive);

    private static DataDestinationDto MapDestination(DataDestination d) =>
        new(d.ModelId, d.Kind, d.DbEngine, d.Host, d.Port, d.DatabaseName, d.Username,
            d.CredentialsEncrypted != null);

    private static DataClientDto MapClient(DataClient c) =>
        new(c.Id, c.Name, c.Description, c.ClientId, c.ClientSecretEncrypted != null, c.IsActive);

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
            p.Name, p.ScheduleKind, p.IntervalMinutes, p.CronExpression, p.IsActive, p.LastRunAt);
    }

    private async Task<string> GenerateUniqueClientIdAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = "cli_" + Guid.NewGuid().ToString("N")[..12];
            if (!await _db.DataClients.AnyAsync(c => c.ClientId == candidate, ct))
            {
                return candidate;
            }
        }
        // Fallback practicamente imposible: usa el Guid completo.
        return "cli_" + Guid.NewGuid().ToString("N");
    }

    private static string GenerateSecret()
    {
        // 32 bytes aleatorios -> base64url sin padding.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
