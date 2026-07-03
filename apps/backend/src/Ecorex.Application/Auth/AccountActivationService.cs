using System.Security.Cryptography;
using System.Text;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Auth;

/// <summary>Resultado de generar un codigo de activacion (el codigo en claro solo viaja por correo).</summary>
public sealed record IssueActivationResult(bool Ok, string? Code, string? Error);

/// <summary>Resultado de activar la cuenta con un codigo.</summary>
public sealed record ActivateAccountResult(bool Ok, Guid? PlatformUserId, Guid? TenantId, string? Email, string? Error);

public interface IAccountActivationService
{
    /// <summary>Genera un codigo nuevo, guarda su hash y devuelve el valor en claro (para enviar por correo).</summary>
    Task<IssueActivationResult> IssueCodeAsync(Guid platformUserId, CancellationToken cancellationToken = default);

    /// <summary>Valida el codigo (hash + expiracion + un solo uso), activa al usuario y resuelve su tenant.</summary>
    Task<ActivateAccountResult> ActivateAsync(string email, string code, CancellationToken cancellationToken = default);

    /// <summary>Reenvio del codigo: invalida los anteriores y emite uno nuevo. Devuelve el codigo en claro.</summary>
    Task<IssueActivationResult> ResendAsync(string email, CancellationToken cancellationToken = default);
}

/// <summary>
/// Codigos de activacion de cuenta por correo (auto-registro). El codigo es de 6 digitos, expira
/// en 24h, es de un solo uso y se guarda como SHA-256 (nunca en claro). Mismo patron que
/// PasswordResetToken.
/// </summary>
public sealed class AccountActivationService : IAccountActivationService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AccountActivationService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<IssueActivationResult> IssueCodeAsync(Guid platformUserId, CancellationToken cancellationToken = default)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == platformUserId, cancellationToken);
        if (user is null) { return new IssueActivationResult(false, null, "Usuario no encontrado."); }

        // Codigo de 6 digitos (000000..999999). El espacio (1M) es pequeno -> se mitiga con expiracion
        // de 24h, un solo uso e invalidacion de codigos previos al pedir reenvio.
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _db.AccountActivationCodes.Add(new AccountActivationCode
        {
            PlatformUserId = platformUserId,
            CodeHash = Hash(code),
            ExpiresAt = _timeProvider.GetUtcNow().Add(CodeLifetime)
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new IssueActivationResult(true, code, null);
    }

    public async Task<ActivateAccountResult> ActivateAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        var clean = (code ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized) || clean.Length < 4)
        {
            return new ActivateAccountResult(false, null, null, null, "Indica tu correo y el codigo de activacion.");
        }

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
        if (user is null) { return new ActivateAccountResult(false, null, null, null, "Codigo o correo invalido."); }
        if (user.Status == PlatformUserStatus.Active)
        {
            // Cuenta ya activada: caso idempotente. Resolvemos el tenant y dejamos pasar.
            var tid = await ResolveTenantAsync(user.Id, cancellationToken);
            return new ActivateAccountResult(true, user.Id, tid, user.Email, null);
        }
        if (user.Status != PlatformUserStatus.PendingActivation)
        {
            return new ActivateAccountResult(false, null, null, null, "La cuenta no esta disponible.");
        }

        var hash = Hash(clean);
        var now = _timeProvider.GetUtcNow();
        var token = await _db.AccountActivationCodes
            .Where(c => c.PlatformUserId == user.Id && c.CodeHash == hash && c.UsedAt == null && c.ExpiresAt > now)
            .FirstOrDefaultAsync(cancellationToken);
        if (token is null)
        {
            return new ActivateAccountResult(false, null, null, null, "Codigo invalido o expirado.");
        }

        token.UsedAt = now;
        user.Status = PlatformUserStatus.Active;
        await _db.SaveChangesAsync(cancellationToken);

        var tenantId = await ResolveTenantAsync(user.Id, cancellationToken);
        return new ActivateAccountResult(true, user.Id, tenantId, user.Email, null);
    }

    public async Task<IssueActivationResult> ResendAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
        // Respuesta uniforme para no revelar si el correo existe.
        if (user is null || user.Status != PlatformUserStatus.PendingActivation)
        {
            return new IssueActivationResult(true, null, null);
        }

        // Invalida los codigos previos del usuario antes de emitir uno nuevo.
        var now = _timeProvider.GetUtcNow();
        var previos = await _db.AccountActivationCodes
            .Where(c => c.PlatformUserId == user.Id && c.UsedAt == null && c.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var p in previos) { p.UsedAt = now; }
        await _db.SaveChangesAsync(cancellationToken);

        return await IssueCodeAsync(user.Id, cancellationToken);
    }

    private async Task<Guid?> ResolveTenantAsync(Guid platformUserId, CancellationToken ct)
    {
        return await _db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == platformUserId && tu.Status == PlatformUserStatus.Active)
            .OrderBy(tu => tu.CreatedAt)
            .Select(tu => (Guid?)tu.TenantId)
            .FirstOrDefaultAsync(ct);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
