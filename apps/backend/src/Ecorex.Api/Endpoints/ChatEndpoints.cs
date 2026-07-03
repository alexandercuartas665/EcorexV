using System.Security.Claims;
using Ecorex.Application.Tenancy;

namespace Ecorex.Api.Endpoints;

/// <summary>
/// Chat: webhook de ingesta de Evolution (anonimo, token por tenant; ver ADR-0006) y
/// lectura/envio autenticado para asesores del tenant (politica TenantMember).
/// </summary>
public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        // Webhook entrante: el tenant va en la ruta y se valida con X-Webhook-Token.
        app.MapPost("/webhooks/evolution/{tenantId:guid}", async (
            Guid tenantId,
            IngestMessageRequest payload,
            HttpRequest request,
            IChatIngestService svc,
            CancellationToken ct) =>
        {
            var token = request.Headers["X-Webhook-Token"].ToString();
            var result = await svc.IngestAsync(tenantId, token, payload, ct);
            return result switch
            {
                ChatIngestResult.Unauthorized => Results.Unauthorized(),
                ChatIngestResult.Duplicate => Results.Ok(new { status = "duplicate" }),
                _ => Results.Accepted()
            };
        }).AllowAnonymous();

        var conversations = app.MapGroup("/tenant/conversations").RequireAuthorization("TenantMember");

        conversations.MapGet("", async (IChatService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListConversationsAsync(ct)));

        conversations.MapGet("/{id:guid}/messages", async (Guid id, IChatService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListMessagesAsync(id, ct)));

        conversations.MapPost("/{id:guid}/messages", async (Guid id, SendMessageRequest request, ClaimsPrincipal user, IChatService svc, CancellationToken ct) =>
        {
            var message = await svc.SendAsync(id, request.Body, ActorId(user), ct);
            return message is null ? Results.NotFound() : Results.Created($"/tenant/conversations/{id}/messages/{message.Id}", message);
        });
    }

    private static Guid ActorId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
}
