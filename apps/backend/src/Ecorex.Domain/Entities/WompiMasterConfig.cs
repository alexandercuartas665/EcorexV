using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Configuracion maestra de Wompi del dueno de la plataforma (Super Admin SaaS sec.8).
/// Es GLOBAL (no tenant-scoped) y singleton: con ella ECOREX.tareas cobra las suscripciones
/// a las agencias. La llave privada y el secret de eventos se guardan cifrados (ISecretProtector)
/// y nunca se exponen completos ni se loggean.
/// </summary>
public class WompiMasterConfig : BaseEntity
{
    public WompiEnvironment Environment { get; set; } = WompiEnvironment.Sandbox;

    /// <summary>Llave publica de Wompi (pub_...). No es secreta, se muestra completa.</summary>
    public string? PublicKey { get; set; }

    /// <summary>Llave privada (prv_...) cifrada en reposo. Nunca se devuelve en claro.</summary>
    public string? PrivateKeyEncrypted { get; set; }

    /// <summary>Secret de firma de eventos cifrado, usado para validar webhooks.</summary>
    public string? EventsSecretEncrypted { get; set; }

    /// <summary>Secret de integridad cifrado, usado para firmar transacciones de cobro (checkout).</summary>
    public string? IntegritySecretEncrypted { get; set; }

    public string? WebhookEndpoint { get; set; }
    public string Currency { get; set; } = "COP";

    /// <summary>Cantidad maxima de reintentos de cobro/confirmacion.</summary>
    public int MaxRetries { get; set; } = 3;

    public WompiIntegrationStatus Status { get; set; } = WompiIntegrationStatus.NotConfigured;
    public DateTimeOffset? LastValidatedAt { get; set; }
}
