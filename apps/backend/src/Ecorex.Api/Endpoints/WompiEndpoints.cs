using Ecorex.Application.Admin;

namespace Ecorex.Api.Endpoints;

/// <summary>
/// Webhook de Wompi (anonimo respecto al JWT; la confianza es la firma del evento, ver ADR-0007).
/// Wompi reenvia eventos: el procesamiento es idempotente por (transaction.id + timestamp).
/// </summary>
public static class WompiEndpoints
{
    public static void MapWompiEndpoints(this WebApplication app)
    {
        app.MapPost("/webhooks/wompi", async (HttpRequest request, IWompiWebhookService svc, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var rawJson = await reader.ReadToEndAsync(ct);

            var result = await svc.ProcessAsync(rawJson, ct);
            return result switch
            {
                // 200 para que Wompi no reintente indefinidamente; el evento queda registrado.
                WompiWebhookResult.Processed => Results.Ok(new { status = "processed" }),
                WompiWebhookResult.Duplicate => Results.Ok(new { status = "duplicate" }),
                WompiWebhookResult.NoMatchingPayment => Results.Ok(new { status = "no_matching_payment" }),
                WompiWebhookResult.InvalidSignature => Results.BadRequest(new { error = "invalid_signature" }),
                _ => Results.BadRequest(new { error = "invalid_payload" })
            };
        }).AllowAnonymous();
    }
}
