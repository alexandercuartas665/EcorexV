using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Auth;

/// <summary>Datos que un visitante envia para crear su propia agencia (autogestion, sin Super Admin).</summary>
public sealed record SelfSignupRequest(
    string AgencyName,
    string DisplayName,
    string Email,
    string Password);

/// <summary>
/// Resultado del auto-registro. La cuenta queda en PendingActivation hasta que el usuario ingrese
/// el codigo enviado por correo. Si Success=true pero EmailDeliveryWarning != null, la cuenta SI
/// se creo y el codigo SI quedo emitido en BD, pero el envio del correo fallo (ej. SMTP mal
/// configurado): el visitante debe ir a /activar y usar "Reenviar codigo" cuando el correo este
/// disponible. Solo Success=false significa que la cuenta NO se creo.
/// </summary>
public sealed record SelfSignupResult(bool Success, Guid TenantId, Guid AdminUserId, string Email, string? Error, string? EmailDeliveryWarning = null);

public interface ISelfSignupService
{
    Task<SelfSignupResult> SignUpAsync(SelfSignupRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Auto-registro publico de una agencia (autogestion). Valida los datos y delega en el
/// onboarding integral (tenant + usuario Owner). La agencia queda activa sin plan; el dueno
/// elige plan despues en "Mi cuenta". El usuario admin se crea en estado PendingActivation y
/// recibe un codigo de 6 digitos por correo para activar la cuenta antes de poder iniciar sesion.
/// </summary>
public sealed class SelfSignupService : ISelfSignupService
{
    private readonly IOnboardingService _onboarding;
    private readonly IAccountActivationService _activation;
    private readonly IApplicationDbContext _db;
    private readonly IEmailSender _email;
    private readonly IPlatformBrandingService _branding;

    public SelfSignupService(
        IOnboardingService onboarding,
        IAccountActivationService activation,
        IApplicationDbContext db,
        IEmailSender email,
        IPlatformBrandingService branding)
    {
        _onboarding = onboarding;
        _activation = activation;
        _db = db;
        _email = email;
        _branding = branding;
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

        // Marca al usuario como pendiente de activacion: el OnboardingService lo dejo como Active.
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == outcome.Result.AdminUserId, cancellationToken);
        if (user is not null)
        {
            user.Status = PlatformUserStatus.PendingActivation;
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Emite el codigo de activacion (6 digitos, 24h, un solo uso). Hash en BD, valor en claro al correo.
        var issued = await _activation.IssueCodeAsync(outcome.Result.AdminUserId, cancellationToken);
        if (!issued.Ok || string.IsNullOrEmpty(issued.Code))
        {
            return new SelfSignupResult(false, Guid.Empty, Guid.Empty, email, issued.Error ?? "No se pudo emitir el codigo de activacion.");
        }

        var brand = await _branding.GetAsync(cancellationToken);
        var html = BuildActivationEmailHtml(brand.PlatformName, issued.Code);
        var sent = await _email.SendAsync(outcome.Result.AdminEmail, $"Activa tu cuenta en {brand.PlatformName}", html, cancellationToken);
        if (!sent.Ok)
        {
            // La cuenta SI se creo y el codigo SI quedo emitido. Solo fallo el envio del correo.
            // Devolvemos Success=true con un warning para que el endpoint redirija a /activar y el
            // visitante pueda pedir reenvio en lugar de quedar atrapado en /login.
            return new SelfSignupResult(
                true,
                outcome.Result.TenantId,
                outcome.Result.AdminUserId,
                outcome.Result.AdminEmail,
                null,
                sent.Error ?? "No se pudo enviar el correo de activacion. Vuelve a pedir el codigo en unos minutos.");
        }

        return new SelfSignupResult(true, outcome.Result.TenantId, outcome.Result.AdminUserId, outcome.Result.AdminEmail, null);
    }

    private static string BuildActivationEmailHtml(string platformName, string code) =>
        $@"<div style=""font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:0 auto;color:#1f2937;"">
  <h2 style=""color:#4f46e5;"">{platformName}</h2>
  <p>Tu cuenta esta casi lista. Para activarla, ingresa el siguiente codigo en la pagina de activacion:</p>
  <p style=""text-align:center;margin:28px 0;"">
    <span style=""display:inline-block;background:#eef2ff;color:#1e1b4b;font-size:26px;letter-spacing:6px;font-weight:bold;padding:14px 24px;border-radius:10px;border:1px solid #c7d2fe;"">{code}</span>
  </p>
  <p>Este codigo vence en 24 horas y solo puede usarse una vez.</p>
  <p style=""font-size:12px;color:#6b7280;"">Si no creaste esta cuenta, ignora este correo.</p>
</div>";
}
