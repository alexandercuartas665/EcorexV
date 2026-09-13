using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <inheritdoc />
public sealed class TelegramConfigService : ITelegramConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public TelegramConfigService(IApplicationDbContext db, ITenantContext tenantContext, ISecretProtector secretProtector, IAuditWriter audit, TimeProvider clock)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _audit = audit;
        _clock = clock;
    }

    public async Task<TelegramConfigDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await _db.TenantTelegramConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (cfg is null) { return new TelegramConfigDto(false, null, false); }
        return new TelegramConfigDto(!string.IsNullOrWhiteSpace(cfg.BotTokenEncrypted), cfg.BotUsername, cfg.IsEnabled);
    }

    public async Task<TelegramConfigDto> SaveAsync(string? botToken, string? botUsername, bool isEnabled, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return new TelegramConfigDto(false, null, false);
        }

        var cfg = await _db.TenantTelegramConfigs.FirstOrDefaultAsync(cancellationToken);
        var isNew = cfg is null;
        if (cfg is null)
        {
            cfg = new TenantTelegramConfig { TenantId = tenantId };
            _db.TenantTelegramConfigs.Add(cfg);
        }

        // Token vacio = conservar el existente (no re-teclear). No vacio = re-cifrar el nuevo.
        if (!string.IsNullOrWhiteSpace(botToken))
        {
            cfg.BotTokenEncrypted = _secretProtector.Protect(botToken.Trim());
        }
        cfg.BotUsername = string.IsNullOrWhiteSpace(botUsername) ? null : botUsername.Trim();
        cfg.IsEnabled = isEnabled;
        cfg.LastValidatedAt = _clock.GetUtcNow();

        _audit.Write(actorUserId, "telegram-config.save", nameof(TenantTelegramConfig), cfg.Id,
            previousValue: null,
            newValue: new { hasToken = !string.IsNullOrWhiteSpace(cfg.BotTokenEncrypted), cfg.BotUsername, cfg.IsEnabled, isNew },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return new TelegramConfigDto(!string.IsNullOrWhiteSpace(cfg.BotTokenEncrypted), cfg.BotUsername, cfg.IsEnabled);
    }
}
