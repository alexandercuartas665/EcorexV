using System.Security.Cryptography;
using Ecorex.Application.Admin;
using Ecorex.Application.Common;
using Ecorex.Application.Common.Auth;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Auth;

public sealed record PasswordResetResult(bool Success, string? Error);

public interface IPasswordResetService
{
    /// <summary>
    /// Solicita un reseteo: si el correo existe y esta activo, genera un token, lo envia por correo
    /// con el enlace {baseUrl}/restablecer?token=... y devuelve Success. Por seguridad, NO revela si
    /// el correo existe (siempre Success salvo error de envio real cuando el correo si existe).
    /// </summary>
    Task<PasswordResetResult> RequestAsync(string email, string baseUrl, CancellationToken cancellationToken = default);

    /// <summary>Aplica la nueva contrasena si el token es valido, no usado y no expirado.</summary>
    Task<PasswordResetResult> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reseteo de contrasena por correo (autogestion). Guarda el HASH del token (no el valor),
/// con un solo uso y expiracion de 1 hora. No filtra si un correo existe o no.
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IEmailSender _email;
    private readonly IPlatformBrandingService _branding;

    public PasswordResetService(IApplicationDbContext db, IPasswordHasher hasher, IEmailSender email, IPlatformBrandingService branding)
    {
        _db = db;
        _hasher = hasher;
        _email = email;
        _branding = branding;
    }

    public async Task<PasswordResetResult> RequestAsync(string email, string baseUrl, CancellationToken cancellationToken = default)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('@'))
        {
            return new PasswordResetResult(false, "Escribe un correo valido.");
        }

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);

        // No revelar si el correo existe: si no existe (o esta inactivo), responder exito sin enviar nada.
        if (user is null || user.Status != PlatformUserStatus.Active)
        {
            return new PasswordResetResult(true, null);
        }

        // Genera token seguro; guarda solo su hash.
        var token = GenerateToken();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            PlatformUserId = user.Id,
            TokenHash = Hash(token),
            ExpiresAt = DateTimeOffset.UtcNow.Add(TokenLifetime)
        });
        await _db.SaveChangesAsync(cancellationToken);

        var brand = await _branding.GetAsync(cancellationToken);
        var link = $"{baseUrl.TrimEnd('/')}/restablecer?token={Uri.EscapeDataString(token)}";
        var html = BuildEmailHtml(brand.PlatformName, link);

        var sent = await _email.SendAsync(normalized, $"Restablece tu contrasena - {brand.PlatformName}", html, cancellationToken);
        if (!sent.Ok)
        {
            return new PasswordResetResult(false, sent.Error ?? "No se pudo enviar el correo de reseteo.");
        }

        return new PasswordResetResult(true, null);
    }

    public async Task<PasswordResetResult> ResetAsync(string token, string newPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PasswordResetResult(false, "Enlace de reseteo invalido.");
        }
        if ((newPassword ?? string.Empty).Length < 8)
        {
            return new PasswordResetResult(false, "La clave debe tener al menos 8 caracteres.");
        }

        var hash = Hash(token);
        var entry = await _db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (entry is null || entry.UsedAt is not null || entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return new PasswordResetResult(false, "El enlace de reseteo es invalido o expiro. Solicita uno nuevo.");
        }

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == entry.PlatformUserId, cancellationToken);
        if (user is null)
        {
            return new PasswordResetResult(false, "El usuario ya no existe.");
        }

        user.PasswordHash = _hasher.Hash(newPassword!);
        entry.UsedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new PasswordResetResult(true, null);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Hash(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildEmailHtml(string platformName, string link) =>
        $@"<div style=""font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:0 auto;color:#1f2937;"">
  <h2 style=""color:#4f46e5;"">{platformName}</h2>
  <p>Recibimos una solicitud para restablecer tu contrasena.</p>
  <p>Haz clic en el siguiente boton para crear una nueva. El enlace vence en 1 hora.</p>
  <p style=""text-align:center;margin:28px 0;"">
    <a href=""{link}"" style=""background:#4f46e5;color:#fff;text-decoration:none;padding:12px 24px;border-radius:10px;font-weight:bold;display:inline-block;"">Restablecer contrasena</a>
  </p>
  <p style=""font-size:12px;color:#6b7280;"">Si no solicitaste esto, ignora este correo; tu contrasena seguira igual.</p>
  <p style=""font-size:12px;color:#6b7280;word-break:break-all;"">O copia este enlace: {link}</p>
</div>";
}
