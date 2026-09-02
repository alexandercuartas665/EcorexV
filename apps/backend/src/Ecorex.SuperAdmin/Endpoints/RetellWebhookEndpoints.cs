using System.Text;
using Ecorex.Application.Voice;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Webhook publico de Retell (eventos call_started/ended/analyzed). Flujo: leer el RAW body -> resolver el
/// tenant por call_id (cross-tenant) -> abrir el scope AMBIENTE del tenant -> el procesador verifica la firma
/// (HMAC con la key del tenant) y actualiza. Si la llamada es desconocida se responde 200 (para que Retell no
/// reintente indefinidamente). La firma invalida responde 401.
/// </summary>
public static class RetellWebhookEndpoints
{
    public static void MapRetellWebhook(this WebApplication app)
    {
        app.MapPost("/api/voice/retell/webhook", async (HttpRequest req, IRetellWebhookProcessor processor, CancellationToken ct) =>
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync(ct);
            var signature = req.Headers["x-retell-signature"].ToString();

            var (_, callId) = processor.Peek(rawBody);
            if (string.IsNullOrWhiteSpace(callId))
            {
                return Results.BadRequest();
            }

            var tenantId = await processor.ResolveTenantByCallIdAsync(callId, ct);
            if (tenantId is null)
            {
                // Llamada desconocida (no es nuestra o ya purgada): 200 para no forzar reintentos en Retell.
                return Results.Ok();
            }

            using (AmbientTenantContext.Begin(tenantId.Value))
            {
                var outcome = await processor.ProcessAsync(rawBody, signature, ct);
                return outcome switch
                {
                    RetellWebhookOutcome.Ok => Results.Ok(),
                    RetellWebhookOutcome.Unauthorized => Results.Unauthorized(),
                    RetellWebhookOutcome.NotFound => Results.Ok(),
                    _ => Results.BadRequest()
                };
            }
        }).AllowAnonymous();
    }
}
