using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// Identidad con la que se ejecuta una llamada MCP (ADR-0159): el USUARIO dueno del token personal.
/// Toda herramienta actua como el: visibilidad de tableros/tareas, bitacora y auditoria a su nombre.
/// </summary>
/// <param name="TenantId">Tenant del token (el ambiente ya quedo fijado con este tenant).</param>
/// <param name="TenantUserId">TenantUser dueno del token (asignaciones, alcance "mis tareas", pasos de flujo).</param>
/// <param name="PlatformUserId">PlatformUser (es el actorUserId que usan los servicios y la auditoria).</param>
/// <param name="DisplayName">Nombre para la bitacora; las herramientas le agregan el sufijo "(via IA)".</param>
/// <param name="IsOwnerOrAdmin">Owner/Admin del tenant: ve todos los tableros (igual que en la UI).</param>
/// <param name="TokenId">Token usado (para la auditoria).</param>
public sealed record McpActor(
    Guid TenantId, Guid TenantUserId, Guid PlatformUserId, string DisplayName, bool IsOwnerOrAdmin, Guid TokenId);

/// <summary>Metadatos de un token personal (nunca el valor).</summary>
public sealed record PersonalTokenDto(
    Guid Id, string Name, string Scope, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt)
{
    public bool IsActive => RevokedAt is null;
}

/// <summary>Resultado de emitir un token: el valor EN CLARO viaja solo aqui, una unica vez.</summary>
public sealed record IssuedPersonalToken(PersonalTokenDto Token, string PlainToken);

/// <summary>
/// Tokens PERSONALES para conectar una IA externa (Claude, ChatGPT, Gemini...) al servidor MCP de
/// tableros (ADR-0159). Cada usuario emite/revoca SOLO los suyos. Se guarda unicamente el hash SHA-256.
/// </summary>
public interface IPersonalAccessTokenService
{
    /// <summary>Tokens personales del usuario (activos y revocados), mas recientes primero.</summary>
    Task<IReadOnlyList<PersonalTokenDto>> ListMineAsync(Guid tenantUserId, CancellationToken cancellationToken = default);

    /// <summary>Emite un token personal para el usuario. Devuelve el valor en claro UNA sola vez.</summary>
    Task<IssuedPersonalToken> IssueAsync(Guid tenantUserId, Guid actorPlatformUserId, string name, CancellationToken cancellationToken = default);

    /// <summary>Revoca un token del usuario (idempotente). false si no existe o no es suyo.</summary>
    Task<bool> RevokeAsync(Guid tenantUserId, Guid actorPlatformUserId, Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve un Bearer en claro a su actor. Es la UNICA lectura cross-tenant (por hash opaco). Exige
    /// token activo, del alcance pedido, personal (con TenantUserId), y usuario + tenant ACTIVOS.
    /// Marca LastUsedAt. null = no autorizado (401).
    /// </summary>
    Task<McpActor?> ResolveAsync(string plainToken, string scope, CancellationToken cancellationToken = default);
}

public sealed class PersonalAccessTokenService : IPersonalAccessTokenService
{
    /// <summary>Tope de tokens activos por usuario (evita acumulacion de llaves olvidadas).</summary>
    public const int MaxActivePerUser = 10;

    private const string AuditReason = "mcp-token";

    private readonly IApplicationDbContext _db;
    private readonly IAuditWriter _audit;

    public PersonalAccessTokenService(IApplicationDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PersonalTokenDto>> ListMineAsync(Guid tenantUserId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.TenantApiTokens.AsNoTracking()
            .Where(t => t.TenantUserId == tenantUserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IssuedPersonalToken> IssueAsync(Guid tenantUserId, Guid actorPlatformUserId, string name, CancellationToken cancellationToken = default)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0) { clean = "Conector IA"; }
        if (clean.Length > 150) { clean = clean[..150]; }

        var user = await _db.TenantUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == tenantUserId, cancellationToken)
            ?? throw new InvalidOperationException("Usuario no encontrado en el tenant.");
        if (user.Status != PlatformUserStatus.Active)
        {
            throw new InvalidOperationException("El usuario no esta activo.");
        }

        var active = await _db.TenantApiTokens.CountAsync(t => t.TenantUserId == tenantUserId && t.RevokedAt == null, cancellationToken);
        if (active >= MaxActivePerUser)
        {
            throw new InvalidOperationException($"Ya tienes {MaxActivePerUser} conectores activos. Revoca alguno antes de crear otro.");
        }

        var plain = ApiTokenHasher.Generate();
        var entity = new TenantApiToken
        {
            TenantId = user.TenantId,
            TenantUserId = tenantUserId,
            Name = clean,
            TokenHash = ApiTokenHasher.Hash(plain),
            Scope = TenantApiToken.ScopeMcpTableros
        };
        _db.TenantApiTokens.Add(entity);
        _audit.Write(actorPlatformUserId, "mcp.token.issue", nameof(TenantApiToken), entity.Id,
            previousValue: null, newValue: new { entity.Name, entity.Scope, entity.TenantUserId },
            tenantId: user.TenantId, reason: AuditReason);
        await _db.SaveChangesAsync(cancellationToken);
        return new IssuedPersonalToken(ToDto(entity), plain);
    }

    public async Task<bool> RevokeAsync(Guid tenantUserId, Guid actorPlatformUserId, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var tok = await _db.TenantApiTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.TenantUserId == tenantUserId, cancellationToken);
        if (tok is null) { return false; }
        if (tok.RevokedAt is null)
        {
            tok.RevokedAt = DateTimeOffset.UtcNow;
            _audit.Write(actorPlatformUserId, "mcp.token.revoke", nameof(TenantApiToken), tok.Id,
                previousValue: null, newValue: new { tok.Name }, tenantId: tok.TenantId, reason: AuditReason);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    public async Task<McpActor?> ResolveAsync(string plainToken, string scope, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainToken)) { return null; }
        var hash = ApiTokenHasher.Hash(plainToken.Trim());

        // Unica lectura cross-tenant: por hash opaco (indice unico). Todo lo demas corre ya con el filtro global.
        var tok = await _db.TenantApiTokens.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null && t.Scope == scope && t.TenantUserId != null, cancellationToken);
        if (tok is null) { return null; }

        var user = await _db.TenantUsers.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == tok.TenantUserId && u.TenantId == tok.TenantId)
            .Select(u => new
            {
                u.Id,
                u.TenantRole,
                u.Status,
                u.PlatformUserId,
                u.Email,
                Display = u.PlatformUser != null ? u.PlatformUser.DisplayName : null
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null || user.Status != PlatformUserStatus.Active) { return null; }

        var tenantActive = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(t => t.Id == tok.TenantId
                && t.Status != TenantStatus.Suspended && t.Status != TenantStatus.Blocked
                && t.Status != TenantStatus.Closing && t.Status != TenantStatus.Archived, cancellationToken);
        if (!tenantActive) { return null; }

        tok.LastUsedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var display = string.IsNullOrWhiteSpace(user.Display) ? user.Email : user.Display!;
        return new McpActor(tok.TenantId, user.Id, user.PlatformUserId, display,
            user.TenantRole is TenantRole.Owner or TenantRole.Admin, tok.Id);
    }

    private static PersonalTokenDto ToDto(TenantApiToken t)
        => new(t.Id, t.Name, t.Scope, t.CreatedAt, t.LastUsedAt, t.RevokedAt);
}
