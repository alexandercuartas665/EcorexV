using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>Vista del SMTP propio del tenant para "Mi cuenta" (sin exponer la clave en claro).</summary>
public sealed record TenantEmailConfigDto(
    string? SmtpHost,
    int SmtpPort,
    string? SmtpUser,
    bool HasPassword,
    bool UseSsl,
    string? FromEmail,
    string? FromName,
    bool IsEnabled,
    DateTimeOffset? LastValidatedAt);

public sealed record SaveTenantEmailConfigRequest(
    string? SmtpHost,
    int SmtpPort,
    string? SmtpUser,
    string? SmtpPassword,
    bool UseSsl,
    string? FromEmail,
    string? FromName,
    bool IsEnabled);

public interface ITenantEmailConfigService
{
    Task<TenantEmailConfigDto?> GetAsync(CancellationToken cancellationToken = default);
    Task<TenantEmailConfigDto> SaveAsync(SaveTenantEmailConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Envia un correo de prueba (usa la config del tenant via IEmailSender). Devuelve el
    /// error legible si fallo, o null si se envio.</summary>
    Task<string?> SendTestAsync(string toEmail, CancellationToken cancellationToken = default);
}

/// <summary>
/// Servidor SMTP PROPIO del tenant (uno por tenant). La clave se cifra con ISecretProtector; solo se
/// re-cifra si llega un valor nuevo (vacia conserva la actual). Tenant-scoped por el filtro global.
/// </summary>
public sealed class TenantEmailConfigService : ITenantEmailConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IEmailSender _emailSender;

    public TenantEmailConfigService(
        IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector secretProtector, IEmailSender emailSender)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _emailSender = emailSender;
    }

    public async Task<TenantEmailConfigDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantEmailConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return cfg is null ? null : Map(cfg);
    }

    public async Task<TenantEmailConfigDto> SaveAsync(SaveTenantEmailConfigRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }

        var cfg = await _db.TenantEmailConfigs.FirstOrDefaultAsync(cancellationToken);
        if (cfg is null)
        {
            cfg = new TenantEmailConfig { TenantId = tenantId };
            _db.TenantEmailConfigs.Add(cfg);
        }

        cfg.SmtpHost = request.SmtpHost?.Trim();
        cfg.SmtpPort = request.SmtpPort <= 0 ? 587 : request.SmtpPort;
        cfg.SmtpUser = request.SmtpUser?.Trim();
        cfg.UseSsl = request.UseSsl;
        cfg.FromEmail = request.FromEmail?.Trim();
        cfg.FromName = request.FromName?.Trim();
        cfg.IsEnabled = request.IsEnabled;

        // La clave solo se re-cifra si llega un valor nuevo; vacia conserva la actual.
        if (!string.IsNullOrWhiteSpace(request.SmtpPassword))
        {
            cfg.SmtpPasswordEncrypted = _secretProtector.Protect(request.SmtpPassword.Trim());
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Map(cfg);
    }

    public async Task<string?> SendTestAsync(string toEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return "Indica un correo destino para la prueba.";
        }
        // IEmailSender ya prefiere la config del tenant activo; asi la prueba valida exactamente lo
        // que se enviara en produccion (o el fallback global si el tenant aun no la habilito).
        var result = await _emailSender.SendAsync(
            toEmail.Trim(),
            "Correo de prueba - ECOREX.tareas",
            "<p>Este es un correo de prueba del servidor SMTP configurado para tu empresa en ECOREX.tareas.</p>"
            + "<p>Si lo recibiste, la configuracion funciona.</p>",
            cancellationToken);

        if (result.Ok)
        {
            var cfg = await _db.TenantEmailConfigs.FirstOrDefaultAsync(cancellationToken);
            if (cfg is not null)
            {
                cfg.LastValidatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        return result.Ok ? null : result.Error;
    }

    private static TenantEmailConfigDto Map(TenantEmailConfig c) => new(
        c.SmtpHost, c.SmtpPort, c.SmtpUser,
        !string.IsNullOrEmpty(c.SmtpPasswordEncrypted),
        c.UseSsl, c.FromEmail, c.FromName, c.IsEnabled, c.LastValidatedAt);
}
