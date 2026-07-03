using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

/// <summary>Vista de la config SMTP para el Super Admin (sin exponer la clave en claro).</summary>
public sealed record EmailConfigDto(
    string? SmtpHost,
    int SmtpPort,
    string? SmtpUser,
    bool HasPassword,
    bool UseSsl,
    string? FromEmail,
    string? FromName,
    bool IsEnabled,
    DateTimeOffset? LastValidatedAt);

public sealed record SaveEmailConfigRequest(
    string? SmtpHost,
    int SmtpPort,
    string? SmtpUser,
    string? SmtpPassword,
    bool UseSsl,
    string? FromEmail,
    string? FromName,
    bool IsEnabled);

public interface IEmailConfigService
{
    Task<EmailConfigDto?> GetAsync(CancellationToken cancellationToken = default);
    Task<EmailConfigDto> SaveAsync(SaveEmailConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Servidor de correo saliente (singleton global). La clave SMTP se cifra con ISecretProtector;
/// solo se re-cifra si llega un valor nuevo. Nunca se devuelve ni se loggea en claro.
/// </summary>
public sealed class EmailConfigService : IEmailConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;

    public EmailConfigService(IApplicationDbContext db, ISecretProtector secretProtector, IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _audit = audit;
    }

    public async Task<EmailConfigDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.EmailConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return cfg is null ? null : Map(cfg);
    }

    public async Task<EmailConfigDto> SaveAsync(SaveEmailConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await _db.EmailConfigs.FirstOrDefaultAsync(cancellationToken);
        var isNew = cfg is null;
        if (cfg is null)
        {
            cfg = new EmailConfig();
            _db.EmailConfigs.Add(cfg);
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

        // Auditoria SIN la clave.
        _audit.Write(actorUserId, isNew ? "email.config.create" : "email.config.update",
            nameof(EmailConfig), cfg.Id,
            previousValue: null, newValue: new { cfg.SmtpHost, cfg.SmtpPort, cfg.FromEmail, cfg.IsEnabled });

        await _db.SaveChangesAsync(cancellationToken);
        return Map(cfg);
    }

    private static EmailConfigDto Map(EmailConfig c) => new(
        c.SmtpHost, c.SmtpPort, c.SmtpUser,
        !string.IsNullOrEmpty(c.SmtpPasswordEncrypted),
        c.UseSsl, c.FromEmail, c.FromName, c.IsEnabled, c.LastValidatedAt);
}
