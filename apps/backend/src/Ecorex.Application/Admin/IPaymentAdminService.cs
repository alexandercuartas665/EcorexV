using Ecorex.Domain.Enums;

namespace Ecorex.Application.Admin;

public interface IPaymentAdminService
{
    /// <summary>Devuelve null si la suscripcion no existe o no pertenece al tenant indicado.</summary>
    Task<PaymentDetail?> RegisterAsync(RegisterPaymentRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentDetail>> ListAsync(Guid? tenantId = null, PaymentStatus? status = null, CancellationToken cancellationToken = default);
}
