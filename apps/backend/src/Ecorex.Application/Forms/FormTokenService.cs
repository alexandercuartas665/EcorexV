using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Forms;

/// <summary>
/// Implementacion de IFormTokenService (ADR-0015). El token opaco son 32 bytes aleatorios
/// en base64url; se persiste SOLO su SHA-256 (hex, 64). IMPORTANTE tenant scoping:
/// ValidateAsync usa IgnoreQueryFilters porque el visor anonimo NO tiene tenant en contexto;
/// es el unico cross-tenant permitido del modulo, acotado a la igualdad exacta del hash
/// (imposible de enumerar), y devuelve el TenantId del token para que el visor fije el
/// ambient (AmbientTenantContext.Begin) antes de tocar cualquier otro servicio.
/// El resto de operaciones (emitir, revocar, marcar usado, listar) son tenant-scoped.
/// </summary>
public sealed class FormTokenService : IFormTokenService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;

    public FormTokenService(IApplicationDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<FormResult<EmitFormTokenResult>> EmitAsync(Guid definitionId, EmitFormTokenRequest options, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return FormResult<EmitFormTokenResult>.Invalid("No hay tenant activo.");
        }
        var definition = await _db.FormDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == definitionId, cancellationToken);
        if (definition is null)
        {
            return FormResult<EmitFormTokenResult>.NotFound("Formulario no encontrado.");
        }
        if (definition.Status != FormStatus.Active || definition.IsArchived)
        {
            return FormResult<EmitFormTokenResult>.Invalid("Solo un formulario activo puede publicarse por URL.");
        }
        if (options.ExpirationHours is <= 0 or > 24 * 365)
        {
            return FormResult<EmitFormTokenResult>.Invalid("La expiracion debe estar entre 1 hora y 1 ano.");
        }

        // Token opaco: 32 bytes CSPRNG -> base64url (43 chars). Solo se guarda el hash.
        var clearToken = GenerateToken();
        var entity = new FormToken
        {
            TenantId = tenantId,
            TokenHash = HashToken(clearToken),
            DefinitionId = definitionId,
            Reference = string.IsNullOrWhiteSpace(options.Reference) ? null : options.Reference.Trim(),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(options.ExpirationHours),
            SingleUse = options.SingleUse,
            AllowAnonymous = options.AllowAnonymous
        };
        _db.FormTokens.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        // El token en claro sale UNA sola vez; no vuelve a ser recuperable.
        return FormResult<EmitFormTokenResult>.Ok(new EmitFormTokenResult(entity.Id, clearToken, entity.ExpiresAt));
    }

    public async Task<FormTokenValidation> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return FormTokenValidation.Invalid;
        }
        var hash = HashToken(token.Trim());

        // UNICO punto cross-tenant permitido (ADR-0015): el visor anonimo no tiene tenant
        // en contexto, asi que la busqueda ignora el filtro global PERO solo por el hash
        // exacto del token presentado. El TenantId devuelto es el que el visor debe fijar
        // como ambient para el resto del pipeline.
        var entity = await _db.FormTokens.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        // Las 4 verificaciones. La razon del fallo NO se expone (mensaje neutro en el visor).
        if (entity is null
            || entity.ExpiresAt <= DateTimeOffset.UtcNow
            || (entity.SingleUse && entity.UsedAt is not null)
            || entity.RevokedAt is not null)
        {
            return FormTokenValidation.Invalid;
        }

        return new FormTokenValidation(
            true, entity.Id, entity.TenantId, entity.DefinitionId,
            entity.Reference, entity.SingleUse, entity.AllowAnonymous);
    }

    public async Task<FormResult<bool>> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped: el filtro global impide revocar tokens de otro tenant.
        var entity = await _db.FormTokens.FirstOrDefaultAsync(t => t.Id == tokenId, cancellationToken);
        if (entity is null)
        {
            return FormResult<bool>.NotFound("Token no encontrado.");
        }
        if (entity.RevokedAt is null)
        {
            entity.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        return FormResult<bool>.Ok(true);
    }

    public async Task MarkUsedAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped: el visor publico ya fijo el ambient del tenant del token.
        var entity = await _db.FormTokens.FirstOrDefaultAsync(t => t.Id == tokenId, cancellationToken);
        if (entity is not null && entity.UsedAt is null)
        {
            entity.UsedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<FormTokenDto>> ListAsync(Guid definitionId, CancellationToken cancellationToken = default)
        => await _db.FormTokens.AsNoTracking()
            .Where(t => t.DefinitionId == definitionId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new FormTokenDto(
                t.Id, t.DefinitionId, t.Reference, t.ExpiresAt, t.SingleUse,
                t.UsedAt, t.RevokedAt, t.AllowAnonymous, t.CreatedAt))
            .ToListAsync(cancellationToken);

    // ---- Helpers ----

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>SHA-256 hex minusculas (64 chars) del token en claro.</summary>
    public static string HashToken(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
