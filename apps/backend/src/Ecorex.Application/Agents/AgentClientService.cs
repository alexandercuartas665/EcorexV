using System.Security.Cryptography;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Agents;

/// <summary>
/// Duenio del ciclo de vida de los clientes/agentes colmena (tabla <c>data_clients</c>): identidad
/// (ClientId publico + secreto cifrado con <see cref="ISecretProtector"/>), rotacion, revocacion y
/// borrado. Tenant-scoped por el filtro global. El secreto en claro solo se devuelve UNA vez (al crear
/// o rotar); el resto del tiempo vive cifrado y nunca se loggea.
///
/// Se extrajo de <c>DataImportConfigService</c> (ADR-0045) para que la conexion con la colmena sea un
/// recurso transversal con su propio modulo, reusable desde Contenedores, Extraccion y futuros modulos.
/// </summary>
public sealed class AgentClientService : IAgentClientService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _protector;

    public AgentClientService(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector protector)
    {
        _db = db;
        _tenantContext = tenantContext;
        _protector = protector;
    }

    public async Task<IReadOnlyList<AgentClientDto>> ListAsync(CancellationToken ct = default)
    {
        var clients = await _db.DataClients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        return clients.Select(Map).ToList();
    }

    public async Task<(AgentClientDto Client, AgentClientSecretDto? Secret)> SaveAsync(
        SaveAgentClientRequest req, Guid actorUserId, CancellationToken ct = default)
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
            var existing = await _db.DataClients.FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new InvalidOperationException("El cliente no existe.");
            existing.Name = name;
            existing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description!.Trim();
            existing.IsActive = req.IsActive;
            await _db.SaveChangesAsync(ct);
            return (Map(existing), null);
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
        return (Map(entity), new AgentClientSecretDto(entity.Id, entity.ClientId, secret));
    }

    public async Task<AgentClientSecretDto?> RotateSecretAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataClients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (entity is null) { return null; }
        var secret = GenerateSecret();
        entity.ClientSecretEncrypted = _protector.Protect(secret);
        await _db.SaveChangesAsync(ct);
        return new AgentClientSecretDto(entity.Id, entity.ClientId, secret);
    }

    public async Task<bool> RevokeAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataClients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (entity is null) { return false; }
        entity.IsActive = false; // se deshabilita, no se borra: la historia/bitacora siguen siendo validas.
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid clientId, Guid actorUserId, CancellationToken ct = default)
    {
        var entity = await _db.DataClients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
        if (entity is null) { return false; }
        _db.DataClients.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static AgentClientDto Map(DataClient c) =>
        new(c.Id, c.Name, c.Description, c.ClientId, c.ClientSecretEncrypted != null, c.IsActive);

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
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
