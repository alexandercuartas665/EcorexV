using Ecorex.Application.Common;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

public sealed class PaymentReceiptService : IPaymentReceiptService
{
    private readonly IApplicationDbContext _db;
    private readonly IReceiptPdfRenderer _renderer;

    public PaymentReceiptService(IApplicationDbContext db, IReceiptPdfRenderer renderer)
    {
        _db = db;
        _renderer = renderer;
    }

    public async Task<PaymentReceiptFile?> GenerateAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        // Se ignora el filtro de tenant: la propiedad la valida el endpoint con los claims del usuario.
        var payment = await _db.TenantPayments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Approved)
        {
            return null; // solo se emite comprobante de pagos aprobados
        }

        var tenant = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == payment.TenantId, cancellationToken);

        var planName = await _db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == payment.SubscriptionId)
            .Join(_db.SaasPlans.IgnoreQueryFilters().AsNoTracking(), s => s.PlanId, p => p.Id, (s, p) => p.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Plan";

        var receiptNumber = string.IsNullOrWhiteSpace(payment.ProviderReference)
            ? $"CMP-{payment.Id.ToString()[..8].ToUpperInvariant()}"
            : payment.ProviderReference!;

        var data = new ReceiptData(
            ReceiptNumber: receiptNumber,
            IssuedAt: payment.ConfirmedAt ?? payment.CreatedAt,
            TenantName: tenant?.Name ?? "Agencia",
            LegalName: tenant?.LegalName,
            TaxId: tenant?.TaxId,
            Country: tenant?.Country,
            PlanName: planName,
            PeriodStart: payment.BillingPeriodStart,
            PeriodEnd: payment.BillingPeriodEnd,
            Amount: payment.Amount,
            Currency: string.IsNullOrWhiteSpace(payment.Currency) ? "COP" : payment.Currency,
            StatusLabel: "Aprobado",
            Provider: payment.Provider,
            ProviderReference: payment.ProviderReference);

        var pdf = _renderer.Render(data);
        var fileName = $"comprobante-{receiptNumber}.pdf";
        return new PaymentReceiptFile(pdf, fileName, payment.TenantId);
    }
}
