using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Confidentiality;

/// <summary>
/// Implementacion de IConfidentialityPolicyService (ADR-0160). Hay UNA politica por tenant.
/// LECTURAS por contexto EFIMERO (IApplicationDbContextFactory, ADR-0157/0138) para no chocar con
/// el DbContext del circuito; ESCRITURAS por el _db scoped (unidad de trabajo del circuito).
/// ResolveByTokenAsync es el UNICO punto cross-tenant: busca por PublicToken exacto con
/// IgnoreQueryFilters (el visor anonimo no tiene tenant) y devuelve el TenantId para que la pagina
/// fije el ambient antes de AcceptAsync. El link NO vence.
/// </summary>
public sealed class ConfidentialityPolicyService : IConfidentialityPolicyService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IApplicationDbContextFactory? _dbFactory;

    public ConfidentialityPolicyService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        IApplicationDbContextFactory? dbFactory = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _dbFactory = dbFactory;
    }

    public async Task<ConfidentialityPolicyDto?> GetForTenantAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = _dbFactory?.Create();
        var db = lease ?? _db;

        // Tenant-scoped por el filtro global; hay a lo sumo una por tenant.
        var p = await db.ConfidentialityPolicies.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (p is null) { return null; }

        var total = await db.ConfidentialityPolicyAcceptances.AsNoTracking()
            .CountAsync(a => a.PolicyId == p.Id, cancellationToken);
        return Map(p, total);
    }

    public async Task<ConfidentialityPolicyDto> SaveAsync(ConfidentialityPolicySaveRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("No hay tenant activo para guardar la politica.");
        }

        var p = await _db.ConfidentialityPolicies.FirstOrDefaultAsync(cancellationToken);
        if (p is null)
        {
            p = new ConfidentialityPolicy { TenantId = tenantId, PublicToken = GenerateToken() };
            _db.ConfidentialityPolicies.Add(p);
        }

        var titulo = request.Titulo?.Trim();
        p.Titulo = string.IsNullOrWhiteSpace(titulo) ? "Politica de confidencialidad" : Truncate(titulo, 200);
        p.Contenido = ConfidentialityHtmlSanitizer.Sanitize(request.Contenido);
        p.Activa = request.Activa;

        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.ConfidentialityPolicyAcceptances
            .CountAsync(a => a.PolicyId == p.Id, cancellationToken);
        return Map(p, total);
    }

    public async Task<ConfidentialityPolicyDto?> RegenerateLinkAsync(CancellationToken cancellationToken = default)
    {
        var p = await _db.ConfidentialityPolicies.FirstOrDefaultAsync(cancellationToken);
        if (p is null) { return null; }

        p.PublicToken = GenerateToken();
        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.ConfidentialityPolicyAcceptances
            .CountAsync(a => a.PolicyId == p.Id, cancellationToken);
        return Map(p, total);
    }

    public async Task<IReadOnlyList<ConfidentialityAcceptanceDto>> ListAcceptancesAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = _dbFactory?.Create();
        var db = lease ?? _db;

        return await db.ConfidentialityPolicyAcceptances.AsNoTracking()
            .OrderByDescending(a => a.AceptadoAt)
            .Select(a => new ConfidentialityAcceptanceDto(
                a.Id, a.Nombre, a.Identificacion, a.Correo, a.AceptadoAt, a.PolicyVersionSnapshot))
            .ToListAsync(cancellationToken);
    }

    public async Task<ConfidentialityPolicyPublicDto?> ResolveByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) { return null; }
        var clean = token.Trim();

        await using var lease = _dbFactory?.Create();
        var db = lease ?? _db;

        // UNICO punto cross-tenant (ADR-0160): el visor anonimo no tiene tenant en contexto, asi
        // que se ignora el filtro global PERO solo por igualdad exacta del token (inenumerable).
        var p = await db.ConfidentialityPolicies.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicToken == clean && !x.IsDeleted && x.Activa, cancellationToken);
        if (p is null) { return null; }

        return new ConfidentialityPolicyPublicDto(
            p.Id, p.TenantId, p.Titulo, p.Contenido, p.Version, HashContent(p.Contenido));
    }

    public async Task<ConfidentialityAcceptStatus> AcceptAsync(string token, ConfidentialityAcceptRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) { return ConfidentialityAcceptStatus.Unavailable; }
        var nombre = request.Nombre?.Trim();
        var identificacion = request.Identificacion?.Trim();
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(identificacion))
        {
            return ConfidentialityAcceptStatus.MissingData;
        }

        var clean = token.Trim();
        // El llamador (pagina publica) ya fijo el ambient del tenant del token; la lectura por
        // IgnoreQueryFilters confirma la politica y aporta TenantId/Version/hash para la constancia.
        var p = await _db.ConfidentialityPolicies.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.PublicToken == clean && !x.IsDeleted && x.Activa, cancellationToken);
        if (p is null) { return ConfidentialityAcceptStatus.Unavailable; }

        var acceptance = new ConfidentialityPolicyAcceptance
        {
            TenantId = p.TenantId,
            PolicyId = p.Id,
            Nombre = Truncate(nombre, 200),
            Identificacion = Truncate(identificacion, 80),
            Correo = string.IsNullOrWhiteSpace(request.Correo) ? null : Truncate(request.Correo.Trim(), 200),
            AceptadoAt = DateTimeOffset.UtcNow,
            Ip = string.IsNullOrWhiteSpace(request.Ip) ? null : Truncate(request.Ip.Trim(), 64),
            UserAgent = string.IsNullOrWhiteSpace(request.UserAgent) ? null : Truncate(request.UserAgent.Trim(), 400),
            PolicyVersionSnapshot = p.Version,
            ContenidoHashSnapshot = HashContent(p.Contenido)
        };
        _db.ConfidentialityPolicyAcceptances.Add(acceptance);
        await _db.SaveChangesAsync(cancellationToken);

        return ConfidentialityAcceptStatus.Ok;
    }

    // ---- Helpers ----

    private static ConfidentialityPolicyDto Map(ConfidentialityPolicy p, int total)
        => new(p.Id, p.Titulo, p.Contenido, p.Activa, p.PublicToken, p.Version, p.UpdatedAt, total);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    /// <summary>Token opaco: 32 bytes CSPRNG -> base64url (43 chars). Recurso publico (no secreto).</summary>
    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>SHA-256 hex (64) del contenido aceptado, para el snapshot de la constancia.</summary>
    private static string HashContent(string? content)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));
}
