using System.Security.Cryptography;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class WebhookAdminService : IWebhookAdminService
{
    // Puerto LOCAL al que cloudflared reenvia el trafico del tunel. Debe ser el puerto donde escucha la app
    // en dev (launchSettings http = 5232), NO el de produccion (8080 en Railway). Si apunta a 8080 en local,
    // el tunel reenvia a un puerto vacio y los entrantes de Evolution nunca llegan al webhook. Configurable
    // por WEBHOOK_LOCAL_PORT para hosts que corran en otro puerto.
    private const int DefaultAppPort = 5232;

    private readonly IApplicationDbContext _db;
    private readonly IDevTunnel _tunnel;
    private readonly IWhatsAppConnectorService _connector;
    private readonly int _appPort;

    public WebhookAdminService(IApplicationDbContext db, IDevTunnel tunnel, IWhatsAppConnectorService connector)
    {
        _db = db;
        _tunnel = tunnel;
        _connector = connector;

        var envPort = Environment.GetEnvironmentVariable("WEBHOOK_LOCAL_PORT");
        _appPort = int.TryParse(envPort, out var p) && p > 0 ? p : DefaultAppPort;
    }

    public async Task<WebhookConfigDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var cfg = await GetOrCreateAsync(cancellationToken);
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> SaveAsync(string mode, string? publicUrl, string? token, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await GetOrCreateAsync(cancellationToken);
        cfg.WebhookMode = string.Equals(mode, "Production", StringComparison.OrdinalIgnoreCase) ? "Production" : "Development";
        cfg.WebhookPublicUrl = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(token))
        {
            cfg.WebhookToken ??= GenerateToken();
        }
        else
        {
            cfg.WebhookToken = token.Trim();
        }
        await _db.SaveChangesAsync(cancellationToken);

        // Re-registrar el webhook en las instancias conectadas (igual que al iniciar el tunel en
        // modo desarrollo). Sin esto, cambiar a Produccion solo actualizaba la BD y Evolution seguia
        // entregando los entrantes a la URL anterior; las lineas no recibian en el nuevo destino.
        await _connector.ApplyWebhookToConnectedLinesAsync(actorUserId, cancellationToken);
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> StartTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var cfg = await GetOrCreateAsync(cancellationToken);
        cfg.WebhookToken ??= GenerateToken();
        cfg.WebhookMode = "Development";

        var url = await _tunnel.StartAsync(_appPort, cancellationToken);
        cfg.WebhookActiveUrl = url;
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(url))
        {
            await _connector.ApplyWebhookToConnectedLinesAsync(actorUserId, cancellationToken);
        }
        return Map(cfg);
    }

    public async Task<WebhookConfigDto> StopTunnelAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        _tunnel.Stop();
        var cfg = await GetOrCreateAsync(cancellationToken);
        cfg.WebhookActiveUrl = null;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(cfg);
    }

    private async Task<EvolutionMasterConfig> GetOrCreateAsync(CancellationToken ct)
    {
        var cfg = await _db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
        var dirty = false;
        if (cfg is null)
        {
            cfg = new EvolutionMasterConfig { WebhookMode = "Development" };
            _db.EvolutionMasterConfigs.Add(cfg);
            dirty = true;
        }
        // Verify token del webhook de Meta: se genera una vez y se reutiliza (handshake GET).
        if (string.IsNullOrWhiteSpace(cfg.MetaWebhookVerifyToken))
        {
            cfg.MetaWebhookVerifyToken = GenerateToken();
            dirty = true;
        }
        if (dirty) { await _db.SaveChangesAsync(ct); }
        return cfg;
    }

    private WebhookConfigDto Map(EvolutionMasterConfig c)
    {
        var effectiveBase = string.Equals(c.WebhookMode, "Production", StringComparison.OrdinalIgnoreCase)
            ? c.WebhookPublicUrl
            : c.WebhookActiveUrl;
        var effective = string.IsNullOrWhiteSpace(effectiveBase) ? null : $"{effectiveBase!.TrimEnd('/')}/webhooks/evolution";
        var metaCallback = string.IsNullOrWhiteSpace(effectiveBase) ? null : $"{effectiveBase!.TrimEnd('/')}/webhooks/meta";
        return new WebhookConfigDto(c.WebhookMode, c.WebhookPublicUrl, c.WebhookToken, c.WebhookActiveUrl, _tunnel.IsRunning, effective, c.MetaWebhookVerifyToken, metaCallback);
    }

    private static string GenerateToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
