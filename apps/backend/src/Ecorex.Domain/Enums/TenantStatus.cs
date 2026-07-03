namespace Ecorex.Domain.Enums;

/// <summary>Estado comercial/operativo de un tenant dentro del SaaS (Super Admin SaaS sec.4).</summary>
public enum TenantStatus
{
    Trial,
    Active,
    PendingPayment,
    PastDue,
    Suspended,
    Blocked,
    Closing,
    Archived
}
