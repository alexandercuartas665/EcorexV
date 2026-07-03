using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

public sealed class EvolutionConfigService : IEvolutionConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;

    public EvolutionConfigService(
        IApplicationDbContext db,
        ITenantContext tenantContext,
        ISecretProtector secretProtector,
        IAuditWriter audit)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
        _audit = audit;
    }

    public async Task<EvolutionConfigDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var config = await _db.TenantEvolutionConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return config is null ? null : Map(config);
    }

    public async Task<EvolutionConfigDto?> UpsertAsync(UpsertEvolutionConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return null;
        }

        var config = await _db.TenantEvolutionConfigs.FirstOrDefaultAsync(cancellationToken);
        var isNew = config is null;

        if (config is null)
        {
            if (string.IsNullOrWhiteSpace(request.ApiToken))
            {
                return null; // un alta requiere token
            }

            config = new TenantEvolutionConfig
            {
                TenantId = tenantId,
                ApiTokenEncrypted = _secretProtector.Protect(request.ApiToken)
            };
            _db.TenantEvolutionConfigs.Add(config);
        }
        else if (!string.IsNullOrWhiteSpace(request.ApiToken))
        {
            // Solo re-cifra si se envia un token nuevo; si viene vacio se conserva el actual.
            config.ApiTokenEncrypted = _secretProtector.Protect(request.ApiToken);
        }

        config.BaseUrl = request.BaseUrl.Trim();
        config.InstanceName = request.InstanceName.Trim();
        config.WebhookUrl = request.WebhookUrl?.Trim();
        config.IsActive = true;
        config.UseMasterServer = false; // configurar URL/token propios implica servidor propio

        // Auditoria SIN el token (nunca se loggea el secreto).
        _audit.Write(actorUserId, isNew ? "evolution.config.create" : "evolution.config.update",
            nameof(TenantEvolutionConfig), config.Id,
            previousValue: null,
            newValue: new { config.BaseUrl, config.InstanceName, config.WebhookUrl },
            tenantId: tenantId);

        await _db.SaveChangesAsync(cancellationToken);
        return Map(config);
    }

    private EvolutionConfigDto Map(TenantEvolutionConfig c) =>
        new(c.BaseUrl ?? "", c.InstanceName ?? "", c.ApiTokenEncrypted is null ? "" : Mask(c.ApiTokenEncrypted), c.WebhookUrl, c.IsActive, c.LastValidatedAt);

    private string Mask(string encryptedToken)
    {
        // Descifra solo para mostrar los ultimos 4 caracteres; nunca se expone completo.
        var token = _secretProtector.Unprotect(encryptedToken);
        return token.Length <= 4 ? "****" : $"{new string('*', Math.Min(token.Length - 4, 8))}{token[^4..]}";
    }
}
