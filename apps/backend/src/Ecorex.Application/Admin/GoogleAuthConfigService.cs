using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

/// <summary>Vista de la config de Google para el Super Admin (sin exponer el secret).</summary>
public sealed record GoogleAuthConfigDto(string? ClientId, bool HasSecret, bool IsEnabled);

public sealed record SaveGoogleAuthConfigRequest(string? ClientId, string? ClientSecret, bool IsEnabled);

/// <summary>Credenciales en claro para el flujo OAuth (uso interno; nunca se devuelve al cliente).</summary>
public sealed record GoogleAuthCredentials(string ClientId, string ClientSecret, bool IsEnabled);

public interface IGoogleAuthConfigService
{
    Task<GoogleAuthConfigDto?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SaveGoogleAuthConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    /// <summary>Devuelve ClientId + secret descifrado para el flujo. Null si no esta configurado/habilitado.</summary>
    Task<GoogleAuthCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Config de "Iniciar sesion con Google" (singleton global). El secret se cifra con ISecretProtector;
/// solo se re-cifra si llega un valor nuevo. Nunca se devuelve ni se loggea en claro.
/// </summary>
public sealed class GoogleAuthConfigService : IGoogleAuthConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;

    public GoogleAuthConfigService(IApplicationDbContext db, ISecretProtector secretProtector, IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _audit = audit;
    }

    public async Task<GoogleAuthConfigDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.GoogleAuthConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return cfg is null ? null : new GoogleAuthConfigDto(cfg.ClientId, !string.IsNullOrEmpty(cfg.ClientSecretEncrypted), cfg.IsEnabled);
    }

    public async Task SaveAsync(SaveGoogleAuthConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.GoogleAuthConfigs.FirstOrDefaultAsync(cancellationToken);
        var isNew = cfg is null;
        if (cfg is null)
        {
            cfg = new GoogleAuthConfig();
            _db.GoogleAuthConfigs.Add(cfg);
        }

        cfg.ClientId = request.ClientId?.Trim();
        cfg.IsEnabled = request.IsEnabled;
        if (!string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            cfg.ClientSecretEncrypted = _secretProtector.Protect(request.ClientSecret.Trim());
        }

        _audit.Write(actorUserId, isNew ? "google.auth.create" : "google.auth.update",
            nameof(GoogleAuthConfig), cfg.Id, previousValue: null, newValue: new { cfg.ClientId, cfg.IsEnabled });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GoogleAuthCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.GoogleAuthConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (cfg is null || !cfg.IsEnabled || string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrEmpty(cfg.ClientSecretEncrypted))
        {
            return null;
        }
        string secret;
        try { secret = _secretProtector.Unprotect(cfg.ClientSecretEncrypted); }
        catch { return null; }
        return new GoogleAuthCredentials(cfg.ClientId!, secret, cfg.IsEnabled);
    }
}
