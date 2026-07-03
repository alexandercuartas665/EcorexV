using Ecorex.Domain.Common;
using Ecorex.Domain.Enums;

namespace Ecorex.Domain.Entities;

/// <summary>
/// Suscripcion de un tenant a un plan. Tiene columna TenantId pero es ENTIDAD GLOBAL:
/// la administra el Super Admin sobre todos los tenants, por eso NO es ITenantScoped.
/// </summary>
public class TenantSubscription : BaseEntity
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid PlanId { get; set; }
    public SaasPlan? Plan { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.Monthly;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? GracePeriodEndsAt { get; set; }

    // --- Debito automatico (Fase 3c) ---
    /// <summary>Si esta activo, el worker cobra automaticamente en cada corte.</summary>
    public bool AutoRenew { get; set; }

    /// <summary>Id de la fuente de pago de Wompi (tarjeta tokenizada) para cobros recurrentes.</summary>
    public long? WompiPaymentSourceId { get; set; }

    /// <summary>Etiqueta visible del metodo guardado, p.ej. "VISA ****4242". Nunca el numero completo.</summary>
    public string? PaymentMethodLabel { get; set; }

    /// <summary>Intentos de cobro fallidos consecutivos (para reintentos/mora).</summary>
    public int FailedAttempts { get; set; }
}
