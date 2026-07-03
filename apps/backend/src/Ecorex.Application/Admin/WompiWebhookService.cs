using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ecorex.Application.Common;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Admin;

/// <summary>
/// Procesa los webhooks de Wompi (Super Admin SaaS sec.8/15.4). La firma del evento se valida
/// con el secret de eventos (SHA256 de los valores de signature.properties + timestamp + secret).
/// Idempotente por (transaction.id + timestamp). El webhook ES la frontera de confianza: opera
/// con datos globales y localiza el pago por su referencia.
/// </summary>
public sealed class WompiWebhookService : IWompiWebhookService
{
    private readonly IApplicationDbContext _db;
    private readonly ISecretProtector _secretProtector;

    public WompiWebhookService(IApplicationDbContext db, ISecretProtector secretProtector)
    {
        _db = db;
        _secretProtector = secretProtector;
    }

    public async Task<WompiWebhookResult> ProcessAsync(string rawJson, CancellationToken cancellationToken = default)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawJson);
        }
        catch (JsonException)
        {
            return WompiWebhookResult.Error;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)
                || !data.TryGetProperty("transaction", out var tx)
                || !root.TryGetProperty("signature", out var signature)
                || !root.TryGetProperty("timestamp", out var timestampEl))
            {
                return WompiWebhookResult.Error;
            }

            var transactionId = tx.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var status = tx.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
            var reference = tx.TryGetProperty("reference", out var refEl) ? refEl.GetString() : null;
            var timestampRaw = timestampEl.GetRawText();

            if (string.IsNullOrEmpty(transactionId))
            {
                return WompiWebhookResult.Error;
            }

            var providerEventId = $"{transactionId}:{timestampRaw}";

            // Idempotencia: un reenvio del mismo evento no se vuelve a procesar.
            if (await _db.WompiWebhookEvents.AsNoTracking().AnyAsync(e => e.ProviderEventId == providerEventId, cancellationToken))
            {
                return WompiWebhookResult.Duplicate;
            }

            var config = await _db.WompiMasterConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            string? eventsSecret = null;
            if (config?.EventsSecretEncrypted is { } enc)
            {
                try { eventsSecret = _secretProtector.Unprotect(enc); }
                catch { eventsSecret = null; } // cifrado con una llave anterior
            }
            var signatureValid = eventsSecret is not null && VerifySignature(data, signature, timestampRaw, eventsSecret);

            var record = new WompiWebhookEvent
            {
                ProviderEventId = providerEventId,
                SignatureValid = signatureValid,
                RawPayload = rawJson,
                TransactionId = transactionId,
                Reference = reference,
                ReceivedAt = DateTimeOffset.UtcNow
            };
            _db.WompiWebhookEvents.Add(record);

            if (!signatureValid)
            {
                record.ProcessingStatus = WebhookProcessingStatus.InvalidSignature;
                record.Note = "Firma invalida o sin secret de eventos configurado.";
                await _db.SaveChangesAsync(cancellationToken);
                return WompiWebhookResult.InvalidSignature;
            }

            var payment = string.IsNullOrEmpty(reference)
                ? null
                : await _db.TenantPayments.FirstOrDefaultAsync(p => p.ProviderReference == reference, cancellationToken);

            if (payment is null)
            {
                record.ProcessingStatus = WebhookProcessingStatus.NoMatchingPayment;
                record.Note = "No se encontro un pago con esa referencia (cola de conciliacion).";
                record.ProcessedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return WompiWebhookResult.NoMatchingPayment;
            }

            var mapped = MapStatus(status);
            payment.Status = mapped;
            payment.Provider = "Wompi";
            if (mapped == PaymentStatus.Approved)
            {
                payment.ConfirmedAt = DateTimeOffset.UtcNow;
                await RenewSubscriptionAndReactivateAsync(payment, cancellationToken);
            }

            record.ProcessingStatus = WebhookProcessingStatus.Processed;
            record.ProcessedAt = DateTimeOffset.UtcNow;
            record.Note = $"Pago {payment.Id} -> {mapped}.";

            await _db.SaveChangesAsync(cancellationToken);
            return WompiWebhookResult.Processed;
        }
    }

    private async Task RenewSubscriptionAndReactivateAsync(TenantPayment payment, CancellationToken cancellationToken)
    {
        var subscription = await _db.TenantSubscriptions
            .Where(s => s.Id == payment.SubscriptionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is not null)
        {
            var basis = subscription.CurrentPeriodEndsAt > DateTimeOffset.UtcNow
                ? subscription.CurrentPeriodEndsAt
                : DateTimeOffset.UtcNow;
            subscription.CurrentPeriodEndsAt = subscription.BillingFrequency == BillingFrequency.Yearly
                ? basis.AddYears(1)
                : basis.AddMonths(1);
            subscription.Status = SubscriptionStatus.Active;
            subscription.GracePeriodEndsAt = null;
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == payment.TenantId, cancellationToken);
        if (tenant is not null && tenant.Status is TenantStatus.Suspended or TenantStatus.PastDue or TenantStatus.PendingPayment or TenantStatus.Blocked)
        {
            tenant.Status = TenantStatus.Active;
        }
    }

    private static PaymentStatus MapStatus(string? wompiStatus) => wompiStatus?.ToUpperInvariant() switch
    {
        "APPROVED" => PaymentStatus.Approved,
        "DECLINED" => PaymentStatus.Declined,
        "VOIDED" => PaymentStatus.Voided,
        "ERROR" => PaymentStatus.Error,
        "PENDING" => PaymentStatus.Pending,
        _ => PaymentStatus.NeedsReview
    };

    /// <summary>
    /// checksum = SHA256( concat(valores de signature.properties bajo "data") + timestamp + secret ).
    /// </summary>
    private static bool VerifySignature(JsonElement data, JsonElement signature, string timestampRaw, string eventsSecret)
    {
        if (!signature.TryGetProperty("checksum", out var checksumEl)
            || !signature.TryGetProperty("properties", out var propsEl)
            || propsEl.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var sb = new StringBuilder();
        foreach (var prop in propsEl.EnumerateArray())
        {
            var path = prop.GetString();
            if (path is null || !TryResolve(data, path, out var value))
            {
                return false;
            }
            sb.Append(value);
        }
        sb.Append(timestampRaw);
        sb.Append(eventsSecret);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var computed = Convert.ToHexString(hash); // mayusculas
        var expected = checksumEl.GetString();
        return expected is not null && string.Equals(computed, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resuelve una ruta tipo "transaction.amount_in_cents" relativa al objeto data.</summary>
    private static bool TryResolve(JsonElement data, string dottedPath, out string value)
    {
        value = string.Empty;
        var current = data;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }
        value = current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : current.GetRawText();
        return true;
    }
}
