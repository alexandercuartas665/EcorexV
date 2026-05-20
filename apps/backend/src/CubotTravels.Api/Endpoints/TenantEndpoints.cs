using System.Security.Claims;
using CubotTravels.Application.Tenancy;

namespace CubotTravels.Api.Endpoints;

/// <summary>
/// Endpoints de administracion interna del tenant (modulo 1.2). Exigen la politica
/// TenantAdmin (rol Owner o Admin dentro de la agencia activa). El aislamiento por
/// tenant lo garantiza el filtro global del DbContext mas el claim tenant_id.
/// </summary>
public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        var users = app.MapGroup("/tenant/users").RequireAuthorization("TenantAdmin");

        users.MapGet("", async (ITenantUserService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        users.MapPost("", async (InviteTenantUserRequest request, ClaimsPrincipal user, ITenantUserService svc, CancellationToken ct) =>
        {
            var result = await svc.InviteAsync(request, ActorId(user), ct);
            return result is null
                ? Results.BadRequest(new { error = "El usuario ya pertenece al tenant o no hay tenant activo." })
                : Results.Created($"/tenant/users/{result.Id}", result);
        });

        users.MapPost("/{id:guid}/role", async (Guid id, ChangeTenantUserRoleRequest request, ClaimsPrincipal user, ITenantUserService svc, CancellationToken ct) =>
        {
            var result = await svc.ChangeRoleAsync(id, request.Role, ActorId(user), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        users.MapPost("/{id:guid}/status", async (Guid id, ChangeTenantUserStatusRequest request, ClaimsPrincipal user, ITenantUserService svc, CancellationToken ct) =>
        {
            var result = await svc.SetStatusAsync(id, request.Status, ActorId(user), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
    }

    private static Guid ActorId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
}
