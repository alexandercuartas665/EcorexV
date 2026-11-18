using System.Text;
using Ecorex.Application.Gestor;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Webhook publico de eventos de SendGrid (Event Webhook, ADR-0188): POST /webhooks/sendgrid/{token}.
/// Flujo: el token secreto de la URL dice de quien es (SMTP propio de un tenant, o el servidor GLOBAL) ->
/// se parsea el lote -> por cada tenant se abre su scope AMBIENTE y se aplican los eventos (estado real de
/// entrega de las ejecuciones del motor de acciones + bajas por rebote/spam).
///
/// Token desconocido = 404 (no se revela nada). Un lote vacio o sin eventos nuestros = 200, para que SendGrid
/// no reintente. Si aplicar falla se responde 500 y SendGrid reintenta (la aplicacion es idempotente).
/// </summary>
public static class SendGridWebhookEndpoints
{
    /// <summary>Tope del cuerpo (SendGrid agrupa hasta ~1000 eventos; unos pocos MB como mucho).</summary>
    private const int MaxBodyChars = 8 * 1024 * 1024;

    public static void MapSendGridWebhook(this WebApplication app)
    {
        app.MapPost(EmailDeliveryEventService.WebhookPath + "{token}", async (
            string token, HttpRequest req, IEmailDeliveryEventService events, ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("SendGridWebhook");
            var scope = await events.ResolveScopeAsync(token, ct);
            if (!scope.Found)
            {
                log.LogWarning("Webhook SendGrid RECHAZADO: token desconocido.");
                return Results.NotFound();
            }

            if (req.ContentLength is long len && len > MaxBodyChars)
            {
                log.LogWarning("Webhook SendGrid RECHAZADO: cuerpo demasiado grande.");
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }
            var sb = new StringBuilder();
            using (var reader = new StreamReader(req.Body, Encoding.UTF8))
            {
                var chunk = new char[16 * 1024];
                int read;
                while ((read = await reader.ReadAsync(chunk.AsMemory(), ct)) > 0)
                {
                    sb.Append(chunk, 0, read);
                    if (sb.Length > MaxBodyChars)
                    {
                        log.LogWarning("Webhook SendGrid RECHAZADO: cuerpo demasiado grande.");
                        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                    }
                }
            }
            var body = sb.ToString();

            var parsed = SendGridEvents.Parse(body);
            if (parsed.Count == 0) { return Results.Ok(); }

            try
            {
                if (scope.TenantId is Guid tenantId)
                {
                    // URL del SMTP propio del tenant: todo el lote es suyo (eventos de otro tenant se descartan).
                    using (AmbientTenantContext.Begin(tenantId))
                    {
                        await events.ApplyAsync(parsed, ownAccount: true, ct);
                    }
                }
                else
                {
                    // URL del servidor GLOBAL: la cuenta envia por todos los tenants; cada evento trae el suyo.
                    foreach (var group in parsed.Where(e => e.TenantId is not null).GroupBy(e => e.TenantId!.Value))
                    {
                        using (AmbientTenantContext.Begin(group.Key))
                        {
                            await events.ApplyAsync(group.ToList(), ownAccount: false, ct);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Webhook SendGrid: no se pudo aplicar el lote de {Count} eventos.", parsed.Count);
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
            return Results.Ok();
        }).AllowAnonymous().DisableAntiforgery();
    }
}
