using CubotTravels.Application.Admin;

namespace CubotTravels.Application.Auth;

/// <summary>Datos que un visitante envia para crear su propia agencia (autogestion, sin Super Admin).</summary>
public sealed record SelfSignupRequest(
    string AgencyName,
    string DisplayName,
    string Email,
    string Password);

/// <summary>Resultado del auto-registro: ok + ids para iniciar sesion, o el error a mostrar.</summary>
public sealed record SelfSignupResult(bool Success, Guid TenantId, Guid AdminUserId, string Email, string? Error);

public interface ISelfSignupService
{
    Task<SelfSignupResult> SignUpAsync(SelfSignupRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Auto-registro publico de una agencia (autogestion). Valida los datos y delega en el
/// onboarding integral (tenant + usuario Owner). La agencia queda activa sin plan; el dueno
/// elige plan despues en "Mi cuenta". No requiere un operador de plataforma.
/// </summary>
public sealed class SelfSignupService : ISelfSignupService
{
    private readonly IOnboardingService _onboarding;

    public SelfSignupService(IOnboardingService onboarding)
    {
        _onboarding = onboarding;
    }

    public async Task<SelfSignupResult> SignUpAsync(SelfSignupRequest request, CancellationToken cancellationToken = default)
    {
        var agency = (request.AgencyName ?? string.Empty).Trim();
        var name = (request.DisplayName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(agency))
        {
            return new SelfSignupResult(false, Guid.Empty, Guid.Empty, email, "Escribe el nombre de tu agencia.");
        }
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return new SelfSignupResult(false, Guid.Empty, Guid.Empty, email, "Escribe un correo valido.");
        }
        if (password.Length < 8)
        {
            return new SelfSignupResult(false, Guid.Empty, Guid.Empty, email, "La clave debe tener al menos 8 caracteres.");
        }

        // actorUserId vacio = registro hecho por el propio visitante (no hay operador de plataforma).
        var outcome = await _onboarding.OnboardAsync(
            new OnboardTenantRequest(
                TenantName: agency,
                AdminEmail: email,
                AdminPassword: password,
                AdminDisplayName: string.IsNullOrWhiteSpace(name) ? null : name),
            actorUserId: Guid.Empty,
            cancellationToken);

        if (!outcome.Success || outcome.Result is null)
        {
            return new SelfSignupResult(false, Guid.Empty, Guid.Empty, email, outcome.Error ?? "No se pudo crear la cuenta.");
        }

        return new SelfSignupResult(true, outcome.Result.TenantId, outcome.Result.AdminUserId, outcome.Result.AdminEmail, null);
    }
}
