namespace Ecorex.Domain.Enums;

/// <summary>Estado de la suscripcion de un tenant (Super Admin SaaS sec.7).</summary>
public enum SubscriptionStatus
{
    Trialing,
    Active,
    PendingPayment,
    PastDue,
    GracePeriod,
    Suspended,
    Cancelled,
    AdminException
}
