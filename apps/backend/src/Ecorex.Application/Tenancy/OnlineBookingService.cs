using System.Security.Cryptography;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed record OnlineBookingSettingsDto(bool Enabled, string? Token, string? Path, string? Link);

/// <summary>
/// Configuracion de reservas online por link publico del tenant activo. Genera un token opaco al
/// habilitar y arma el link completo con la base capturada desde la consola (para que el agente,
/// que corre sin request HTTP, pueda enviarlo).
/// </summary>
public interface IOnlineBookingService
{
    Task<OnlineBookingSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    /// <summary>Habilita/inhabilita. Al habilitar genera token si falta y guarda la base publica (sin barra final).</summary>
    Task<OnlineBookingSettingsDto> SetEnabledAsync(bool enabled, string? baseUrl, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Regenera el token (invalida el link anterior).</summary>
    Task<OnlineBookingSettingsDto> RegenerateTokenAsync(string? baseUrl, Guid actorUserId, CancellationToken cancellationToken = default);
}

public sealed class OnlineBookingService : IOnlineBookingService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditWriter _audit;

    public OnlineBookingService(IApplicationDbContext db, ITenantContext tenantContext, IAuditWriter audit)
    {
        _db = db; _tenantContext = tenantContext; _audit = audit;
    }

    public async Task<OnlineBookingSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new(false, null, null, null); }
        var t = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (t is null) { return new(false, null, null, null); }
        var baseUrl = await EffectiveBaseAsync(t.PublicBookingBaseUrl, cancellationToken);
        return Map(t.OnlineBookingEnabled, t.PublicBookingToken, baseUrl);
    }

    // El link que recibe el cliente DEBE ser abrible desde su telefono. En desarrollo la base configurada
    // suele ser localhost (no abrible); cuando hay una base publica activa del webhook (tunel cloudflared en
    // dev / dominio en prod) la preferimos, porque apunta al mismo app que sirve /r/{token}. Si la base
    // configurada ya es publica (un dominio real), la respetamos. EvolutionMasterConfig es global (no por tenant).
    private async Task<string?> EffectiveBaseAsync(string? configured, CancellationToken ct)
    {
        var cfg = await _db.EvolutionMasterConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        var publicBase = string.Equals(cfg?.WebhookMode, "Production", StringComparison.OrdinalIgnoreCase)
            ? cfg?.WebhookPublicUrl
            : cfg?.WebhookActiveUrl;
        publicBase = string.IsNullOrWhiteSpace(publicBase) ? null : publicBase!.TrimEnd('/');
        if (publicBase is not null && (string.IsNullOrWhiteSpace(configured) || IsLocal(configured!)))
        {
            return publicBase;
        }
        return configured;
    }

    private static bool IsLocal(string url)
        => url.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || url.Contains("127.0.0.1", StringComparison.Ordinal)
            || url.Contains("[::1]", StringComparison.Ordinal);

    public async Task<OnlineBookingSettingsDto> SetEnabledAsync(bool enabled, string? baseUrl, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new(false, null, null, null); }
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (t is null) { return new(false, null, null, null); }

        t.OnlineBookingEnabled = enabled;
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(t.PublicBookingToken)) { t.PublicBookingToken = NewToken(); }
            var b = CleanBase(baseUrl);
            if (b is not null) { t.PublicBookingBaseUrl = b; }
        }
        _audit.Write(actorUserId, "online-booking.toggle", nameof(Tenant), tenantId, null, new { enabled }, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(t.OnlineBookingEnabled, t.PublicBookingToken, t.PublicBookingBaseUrl);
    }

    public async Task<OnlineBookingSettingsDto> RegenerateTokenAsync(string? baseUrl, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId) { return new(false, null, null, null); }
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (t is null) { return new(false, null, null, null); }

        t.PublicBookingToken = NewToken();
        var b = CleanBase(baseUrl);
        if (b is not null) { t.PublicBookingBaseUrl = b; }
        _audit.Write(actorUserId, "online-booking.regenerate", nameof(Tenant), tenantId, null, null, tenantId);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(t.OnlineBookingEnabled, t.PublicBookingToken, t.PublicBookingBaseUrl);
    }

    private static OnlineBookingSettingsDto Map(bool enabled, string? token, string? baseUrl)
    {
        var path = string.IsNullOrWhiteSpace(token) ? null : $"/r/{token}";
        var link = path is not null && !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl + path : null;
        return new OnlineBookingSettingsDto(enabled, token, path, link);
    }

    // Token opaco URL-safe (~22 chars).
    private static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).Replace("+", "").Replace("/", "").Replace("=", "").ToLowerInvariant()[..16];

    private static string? CleanBase(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) { return null; }
        return baseUrl.Trim().TrimEnd('/');
    }
}
