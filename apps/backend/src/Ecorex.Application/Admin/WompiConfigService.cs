using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

/// <summary>
/// Configuracion maestra de Wompi del dueno de la plataforma (Super Admin SaaS sec.8).
/// Singleton global. Las llaves privada y de eventos se cifran con ISecretProtector y
/// nunca se devuelven en claro ni se escriben en auditoria.
/// </summary>
public sealed class WompiConfigService : IWompiConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IAuditWriter _audit;

    public WompiConfigService(IApplicationDbContext db, ISecretProtector secretProtector, IAuditWriter audit)
    {
        _db = db;
        _secretProtector = secretProtector;
        _audit = audit;
    }

    public async Task<WompiConfigDto?> GetAsync(CancellationToken cancellationToken = default)
    {
        var config = await _db.WompiMasterConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return config is null ? null : Map(config);
    }

    public async Task<WompiConfigDto> SaveAsync(SaveWompiConfigRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var config = await _db.WompiMasterConfigs.FirstOrDefaultAsync(cancellationToken);
        var isNew = config is null;

        if (config is null)
        {
            config = new WompiMasterConfig();
            _db.WompiMasterConfigs.Add(config);
        }

        config.Environment = request.Environment;
        config.PublicKey = request.PublicKey?.Trim();
        config.WebhookEndpoint = request.WebhookEndpoint?.Trim();
        config.Currency = string.IsNullOrWhiteSpace(request.Currency) ? "COP" : request.Currency.Trim().ToUpperInvariant();
        config.MaxRetries = request.MaxRetries < 0 ? 0 : request.MaxRetries;

        // Las llaves solo se re-cifran si llega un valor nuevo; si vienen vacias se conserva el actual.
        if (!string.IsNullOrWhiteSpace(request.PrivateKey))
        {
            config.PrivateKeyEncrypted = _secretProtector.Protect(request.PrivateKey.Trim());
        }
        if (!string.IsNullOrWhiteSpace(request.EventsSecret))
        {
            config.EventsSecretEncrypted = _secretProtector.Protect(request.EventsSecret.Trim());
        }
        if (!string.IsNullOrWhiteSpace(request.IntegritySecret))
        {
            config.IntegritySecretEncrypted = _secretProtector.Protect(request.IntegritySecret.Trim());
        }

        // Guardar invalida la validacion previa: hay que volver a validar.
        config.Status = HasCredentials(config) ? WompiIntegrationStatus.Configured : WompiIntegrationStatus.NotConfigured;
        config.LastValidatedAt = null;

        // Auditoria SIN llaves (nunca se loggean secretos).
        _audit.Write(actorUserId, isNew ? "wompi.config.create" : "wompi.config.update",
            nameof(WompiMasterConfig), config.Id,
            previousValue: null,
            newValue: new { Environment = config.Environment.ToString(), config.Currency, config.WebhookEndpoint, config.MaxRetries });

        await _db.SaveChangesAsync(cancellationToken);
        return Map(config);
    }

    public async Task<WompiValidationResult?> ValidateAsync(Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var config = await _db.WompiMasterConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config is null)
        {
            return null;
        }

        var (ok, message) = ValidateStructure(config);
        config.Status = ok ? WompiIntegrationStatus.Validated : WompiIntegrationStatus.Error;
        config.LastValidatedAt = DateTimeOffset.UtcNow;

        _audit.Write(actorUserId, "wompi.config.validate", nameof(WompiMasterConfig), config.Id,
            previousValue: null,
            newValue: new { Result = ok ? "Validated" : "Error", message });

        await _db.SaveChangesAsync(cancellationToken);
        return new WompiValidationResult(ok, message);
    }

    private (bool ok, string message) ValidateStructure(WompiMasterConfig c)
    {
        if (string.IsNullOrWhiteSpace(c.PublicKey) || string.IsNullOrWhiteSpace(c.PrivateKeyEncrypted))
        {
            return (false, "Faltan la llave publica y/o la llave privada.");
        }
        if (string.IsNullOrWhiteSpace(c.EventsSecretEncrypted))
        {
            return (false, "Falta el secret de eventos para validar webhooks.");
        }
        if (string.IsNullOrWhiteSpace(c.IntegritySecretEncrypted))
        {
            return (false, "Falta el secret de integridad para firmar los cobros.");
        }

        string privateKey;
        try
        {
            privateKey = _secretProtector.Unprotect(c.PrivateKeyEncrypted);
        }
        catch
        {
            return (false, "Las llaves estan cifradas con una version anterior. Vuelve a guardarlas.");
        }
        if (!c.PublicKey.StartsWith("pub_", StringComparison.Ordinal))
        {
            return (false, "La llave publica deberia empezar con 'pub_'.");
        }
        if (!privateKey.StartsWith("prv_", StringComparison.Ordinal))
        {
            return (false, "La llave privada deberia empezar con 'prv_'.");
        }

        // Coherencia de ambiente: las llaves de Wompi incluyen 'test' (sandbox) o 'prod' (produccion).
        var token = c.Environment == WompiEnvironment.Production ? "prod" : "test";
        if (!c.PublicKey.Contains(token, StringComparison.Ordinal) || !privateKey.Contains(token, StringComparison.Ordinal))
        {
            return (false, $"Las llaves no corresponden al ambiente {c.Environment} (se espera '{token}' en la llave).");
        }

        return (true, $"Configuracion coherente para ambiente {c.Environment} en {c.Currency}. (Validacion estructural; sin cobro real.)");
    }

    private static bool HasCredentials(WompiMasterConfig c) =>
        !string.IsNullOrWhiteSpace(c.PublicKey) && !string.IsNullOrWhiteSpace(c.PrivateKeyEncrypted);

    private WompiConfigDto Map(WompiMasterConfig c) =>
        new(c.Environment,
            c.PublicKey,
            c.PrivateKeyEncrypted is null ? null : Mask(c.PrivateKeyEncrypted),
            c.EventsSecretEncrypted is null ? null : Mask(c.EventsSecretEncrypted),
            c.IntegritySecretEncrypted is null ? null : Mask(c.IntegritySecretEncrypted),
            c.WebhookEndpoint,
            c.Currency,
            c.MaxRetries,
            c.Status,
            c.LastValidatedAt,
            c.PrivateKeyEncrypted is not null,
            c.EventsSecretEncrypted is not null,
            c.IntegritySecretEncrypted is not null);

    private string Mask(string encrypted)
    {
        string value;
        try
        {
            value = _secretProtector.Unprotect(encrypted);
        }
        catch
        {
            // Secreto cifrado con una llave anterior (p.ej. tras cambiar el key ring): re-ingresar.
            return "(re-ingresar)";
        }
        return value.Length <= 4 ? "****" : $"{new string('*', Math.Min(value.Length - 4, 8))}{value[^4..]}";
    }
}
