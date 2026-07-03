using System.Security.Claims;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Api.Endpoints;

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

        // --- Configuracion Evolution API del tenant (modulo 1.3) ---
        var evolution = app.MapGroup("/tenant/evolution").RequireAuthorization("TenantAdmin");

        evolution.MapGet("", async (IEvolutionConfigService svc, CancellationToken ct) =>
        {
            var config = await svc.GetAsync(ct);
            return config is null ? Results.NoContent() : Results.Ok(config);
        });

        evolution.MapPut("", async (UpsertEvolutionConfigRequest request, ClaimsPrincipal user, IEvolutionConfigService svc, CancellationToken ct) =>
        {
            var config = await svc.UpsertAsync(request, ActorId(user), ct);
            return config is null
                ? Results.BadRequest(new { error = "No hay tenant activo o falta el token en el alta." })
                : Results.Ok(config);
        });

        // --- Lineas WhatsApp del tenant (modulo 1.4) ---
        var lines = app.MapGroup("/tenant/whatsapp-lines").RequireAuthorization("TenantAdmin");

        lines.MapGet("", async (IWhatsAppLineService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        lines.MapPost("", async (CreateWhatsAppLineRequest request, ClaimsPrincipal user, IWhatsAppLineService svc, CancellationToken ct) =>
        {
            var line = await svc.CreateAsync(request, ActorId(user), ct);
            return line is null
                ? Results.BadRequest(new { error = "No hay tenant activo." })
                : Results.Created($"/tenant/whatsapp-lines/{line.Id}", line);
        });

        lines.MapPost("/{id:guid}/status", async (Guid id, ChangeLineStatusRequest request, ClaimsPrincipal user, IWhatsAppLineService svc, CancellationToken ct) =>
        {
            var line = await svc.ChangeStatusAsync(id, request.Status, ActorId(user), ct);
            return line is null ? Results.NotFound() : Results.Ok(line);
        });

        lines.MapPost("/{id:guid}/assignment", async (Guid id, AssignLineRequest request, ClaimsPrincipal user, IWhatsAppLineService svc, CancellationToken ct) =>
        {
            var line = await svc.AssignAsync(id, request.TenantUserId, ActorId(user), ct);
            return line is null ? Results.NotFound() : Results.Ok(line);
        });

        // --- Embudo: etapas (modulo 2.1) ---
        // Listar lo puede cualquier miembro; crear etapas es de administradores del tenant.
        app.MapGet("/tenant/pipeline/stages", async (IPipelineService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListStagesAsync(ct))).RequireAuthorization("TenantMember");

        app.MapPost("/tenant/pipeline/stages", async (CreatePipelineStageRequest request, ClaimsPrincipal user, IPipelineService svc, CancellationToken ct) =>
        {
            var stage = await svc.CreateStageAsync(request, ActorId(user), ct);
            return stage is null
                ? Results.BadRequest(new { error = "No hay tenant activo." })
                : Results.Created($"/tenant/pipeline/stages/{stage.Id}", stage);
        }).RequireAuthorization("TenantAdmin");

        // --- Leads (modulo 2.2) ---
        var leads = app.MapGroup("/tenant/leads").RequireAuthorization("TenantMember");

        leads.MapGet("", async (ILeadService svc, Guid? stageId, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(stageId, ct)));

        leads.MapGet("/{id:guid}", async (Guid id, ILeadService svc, CancellationToken ct) =>
        {
            var lead = await svc.GetAsync(id, ct);
            return lead is null ? Results.NotFound() : Results.Ok(lead);
        });

        leads.MapPost("", async (CreateLeadRequest request, ClaimsPrincipal user, ILeadService svc, CancellationToken ct) =>
        {
            var lead = await svc.CreateAsync(request, ActorId(user), ct);
            return lead is null
                ? Results.BadRequest(new { error = "No hay tenant activo o no existen etapas configuradas." })
                : Results.Created($"/tenant/leads/{lead.Id}", lead);
        });

        leads.MapPost("/{id:guid}/move", async (Guid id, MoveLeadRequest request, ClaimsPrincipal user, ILeadService svc, CancellationToken ct) =>
        {
            var lead = await svc.MoveAsync(id, request, ActorId(user), ct);
            return lead is null ? Results.NotFound() : Results.Ok(lead);
        });

        leads.MapPost("/{id:guid}/assignment", async (Guid id, AssignLeadRequest request, ClaimsPrincipal user, ILeadService svc, CancellationToken ct) =>
        {
            var lead = await svc.AssignAsync(id, request.TenantUserId, ActorId(user), ct);
            return lead is null ? Results.NotFound() : Results.Ok(lead);
        });

        // --- Seguimientos / tareas (modulo 2.5) ---
        var followUps = app.MapGroup("/tenant/follow-ups").RequireAuthorization("TenantMember");

        followUps.MapGet("", async (IFollowUpTaskService svc, Guid? leadId, FollowUpTaskStatus? status, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(leadId, status, ct)));

        followUps.MapPost("", async (CreateFollowUpTaskRequest request, ClaimsPrincipal user, IFollowUpTaskService svc, CancellationToken ct) =>
        {
            var task = await svc.CreateAsync(request, ActorId(user), ct);
            return task is null
                ? Results.BadRequest(new { error = "No hay tenant activo o el lead no existe." })
                : Results.Created($"/tenant/follow-ups/{task.Id}", task);
        });

        followUps.MapPost("/{id:guid}/complete", async (Guid id, ClaimsPrincipal user, IFollowUpTaskService svc, CancellationToken ct) =>
        {
            var task = await svc.CompleteAsync(id, ActorId(user), ct);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });

        followUps.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, IFollowUpTaskService svc, CancellationToken ct) =>
        {
            var task = await svc.CancelAsync(id, ActorId(user), ct);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });

        // --- Dashboard de metricas (modulo 2.6) ---
        app.MapGet("/tenant/dashboard", async (IDashboardService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct))).RequireAuthorization("TenantMember");
    }

    private static Guid ActorId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;
}
