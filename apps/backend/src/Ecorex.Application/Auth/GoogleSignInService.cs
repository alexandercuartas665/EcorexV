using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Auth;

/// <summary>Identidad verificada que devuelve Google tras el intercambio del code.</summary>
public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name, string? Picture);

/// <summary>Cliente que habla con Google para intercambiar el authorization code por la identidad.</summary>
public interface IGoogleOAuthClient
{
    Task<GoogleIdentity?> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri, CancellationToken cancellationToken = default);
}

/// <summary>Resultado de resolver un login con Google: datos para armar la cookie, o el error a mostrar.</summary>
public sealed record GoogleSignInResult(
    bool Success,
    string? Error = null,
    Guid UserId = default,
    string? DisplayName = null,
    string? Email = null,
    string? PlatformRole = null,
    Guid? TenantId = null,
    string? TenantRole = null);

public interface IGoogleSignInService
{
    /// <summary>Arma la URL de challenge hacia Google. Null si el login con Google no esta configurado/habilitado.</summary>
    Task<string?> BuildAuthorizeUrlAsync(string redirectUri, string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Intercambia el code y resuelve el usuario de ECOREX. Si el correo no existe y se entrega
    /// <paramref name="signupAgencyName"/>, crea una agencia nueva con ese Google como dueno (auto-registro).
    /// </summary>
    Task<GoogleSignInResult> ResolveAsync(string code, string redirectUri, string? signupAgencyName = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Login con Google (OIDC). Google confirma la identidad; ECOREX decide el acceso: el correo debe
/// corresponder a un PlatformUser existente y activo (no hay auto-registro por Google). Vincula el
/// GoogleSubject al usuario en el primer login.
/// </summary>
public sealed class GoogleSignInService : IGoogleSignInService
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    private readonly IGoogleAuthConfigService _config;
    private readonly IGoogleOAuthClient _client;
    private readonly IApplicationDbContext _db;
    private readonly IOnboardingService _onboarding;

    public GoogleSignInService(IGoogleAuthConfigService config, IGoogleOAuthClient client, IApplicationDbContext db, IOnboardingService onboarding)
    {
        _config = config;
        _client = client;
        _db = db;
        _onboarding = onboarding;
    }

    public async Task<string?> BuildAuthorizeUrlAsync(string redirectUri, string state, CancellationToken cancellationToken = default)
    {
        var creds = await _config.GetCredentialsAsync(cancellationToken);
        if (creds is null) { return null; }

        var query = new Dictionary<string, string>
        {
            ["client_id"] = creds.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["access_type"] = "online",
            ["prompt"] = "select_account",
            ["include_granted_scopes"] = "true"
        };
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{AuthEndpoint}?{qs}";
    }

    public async Task<GoogleSignInResult> ResolveAsync(string code, string redirectUri, string? signupAgencyName = null, CancellationToken cancellationToken = default)
    {
        var creds = await _config.GetCredentialsAsync(cancellationToken);
        if (creds is null)
        {
            return new GoogleSignInResult(false, "El inicio de sesion con Google no esta habilitado.");
        }

        var identity = await _client.ExchangeCodeAsync(creds.ClientId, creds.ClientSecret, code, redirectUri, cancellationToken);
        if (identity is null)
        {
            return new GoogleSignInResult(false, "No se pudo validar tu identidad con Google.");
        }
        if (!identity.EmailVerified || string.IsNullOrWhiteSpace(identity.Email))
        {
            return new GoogleSignInResult(false, "Tu correo de Google no esta verificado.");
        }

        var email = identity.Email.Trim().ToLowerInvariant();

        // Resolver el usuario existente por subject de Google o por correo verificado.
        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.GoogleSubject == identity.Subject || u.Email == email, cancellationToken);

        // Correo desconocido: si el visitante venia del formulario "Crear cuenta" con un nombre de
        // agencia, creamos su agencia y lo dejamos como Owner (auto-registro con Google).
        if (user is null && !string.IsNullOrWhiteSpace(signupAgencyName))
        {
            var outcome = await _onboarding.OnboardAsync(
                new OnboardTenantRequest(
                    TenantName: signupAgencyName.Trim(),
                    AdminEmail: email,
                    AdminPassword: string.Empty,
                    AdminDisplayName: identity.Name,
                    GoogleSubject: identity.Subject),
                actorUserId: Guid.Empty,
                cancellationToken);

            if (!outcome.Success || outcome.Result is null)
            {
                return new GoogleSignInResult(false, outcome.Error ?? "No se pudo crear la cuenta con Google.");
            }

            return new GoogleSignInResult(true, null, outcome.Result.AdminUserId,
                identity.Name ?? email, outcome.Result.AdminEmail,
                TenantId: outcome.Result.TenantId, TenantRole: TenantRole.Owner.ToString());
        }

        if (user is null || user.Status != PlatformUserStatus.Active)
        {
            return new GoogleSignInResult(false,
                "Tu cuenta de Google fue validada, pero todavia no tienes acceso a ECOREX.tareas. Solicita una invitacion al administrador.");
        }

        // Regla: el login con Google es solo para usuarios de agencia (clientes). Los operadores de
        // plataforma (Super Admin / roles internos) ingresan unicamente con correo y contrasena.
        if (user.PlatformRole is not null)
        {
            return new GoogleSignInResult(false,
                "Los operadores de plataforma ingresan con correo y contrasena, no con Google.");
        }

        // Vincular el subject de Google al usuario (primer login con Google) y marcar verificado.
        if (string.IsNullOrEmpty(user.GoogleSubject)) { user.GoogleSubject = identity.Subject; }
        user.EmailVerified = true;
        user.LastLoginAt = DateTimeOffset.UtcNow;

        // Usuario de agencia: resolver membresia activa (sin tenant aun, se ignora el filtro global).
        var membership = await _db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.PlatformUserId == user.Id && tu.Status == PlatformUserStatus.Active)
            .OrderBy(tu => tu.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return new GoogleSignInResult(false, "Tu usuario no tiene una agencia activa asignada.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new GoogleSignInResult(true, null, user.Id, user.DisplayName ?? user.Email, user.Email,
            TenantId: membership.TenantId, TenantRole: membership.TenantRole.ToString());
    }
}
