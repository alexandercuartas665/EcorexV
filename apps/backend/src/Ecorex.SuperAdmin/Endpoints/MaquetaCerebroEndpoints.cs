using System.Security.Claims;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.SuperAdmin.Auth;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Descarga del cerebro de una maqueta como vault de Obsidian (ADR-0183, v0.16.352): GET con la SESION del usuario
/// (cookie), igual que <c>/archivo/{id}</c>. Un endpoint y no JS interop porque el ZIP puede pesar decenas de MB
/// (adjuntos) y no debe viajar por el circuito de Blazor. El proyecto se busca con el filtro global del tenant de la
/// sesion, asi que nadie descarga el cerebro de otra empresa.
/// </summary>
public static class MaquetaCerebroEndpoints
{
    public const string ExportRoute = "/maqueta-cerebro/{slug}/exportar";

    /// <summary>URL de descarga del vault del proyecto.</summary>
    public static string ExportUrl(string slug) => $"/maqueta-cerebro/{Uri.EscapeDataString(slug)}/exportar";

    public static void MapMaquetaCerebroEndpoints(WebApplication app)
    {
        app.MapGet(ExportRoute, async (string slug, HttpContext http, IServiceScopeFactory scopes, ILoggerFactory logs, CancellationToken ct) =>
        {
            if (!Guid.TryParse(http.User.FindFirst("tenant_id")?.Value, out var tenantId)
                || !Guid.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                return Results.Forbid();
            }
            using var ambient = AmbientTenantContext.Begin(tenantId, userId);
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var actor = await sp.GetRequiredService<IMaquetaSolicitudService>().ResolveSessionActorAsync(userId, ct);
            if (actor is null) { return Results.Forbid(); }

            try
            {
                var res = await sp.GetRequiredService<IMockupBrainService>().ExportVaultAsync(slug, actor, ct);
                if (!res.IsOk || res.Value is null) { return Results.NotFound(res.Error); }
                var name = $"{slug}-cerebro.zip";
                http.Response.Headers.CacheControl = "no-store";
                return Results.File(res.Value.Zip, "application/zip", fileDownloadName: name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logs.CreateLogger("Ecorex.Maqueta").LogError(ex, "Fallo al exportar el cerebro de la maqueta {Slug} del tenant {TenantId}", slug, tenantId);
                return Results.Problem("No se pudo exportar el cerebro.");
            }
        }).RequireAuthorization();
    }
}
