using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

/// <summary>Resultado de un cambio de plan en autoservicio.</summary>
public sealed record ChangePlanResult(
    SubscriptionDetail Subscription,
    bool IsUpgrade,
    bool ChargedNow,
    bool RequiresPayment,
    string? Message);

public interface ISubscriptionAdminService
{
    /// <summary>Devuelve null si el tenant o el plan no existen.</summary>
    Task<SubscriptionDetail?> AssignAsync(AssignSubscriptionRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambio de plan en autoservicio (lo solicita el cliente). Aplica de inmediato.
    /// Modelo de cobro (sin prorrateo): si es un plan MAYOR (upgrade) se cobra el plan nuevo
    /// completo de inmediato y se reinicia la fecha de corte a hoy; si es un plan MENOR o igual
    /// (downgrade) no se cobra nada ahora y se conserva la fecha de corte actual (el plan menor
    /// se cobra en la siguiente renovacion). Devuelve null si el tenant o el plan no existen.
    /// </summary>
    Task<ChangePlanResult?> ChangePlanAsync(Guid tenantId, Guid planId, BillingFrequency frequency, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetail>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
