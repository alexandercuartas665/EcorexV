using Ecorex.Application.Reporting;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Endpoints HTTP tenant-safe del Motor de Reportes (ADR-0051, Ola 1). Exponen el catalogo semantico
/// y el datasource como JSON, autenticados por la cookie del sistema (policy "TenantMember", que exige
/// el claim tenant_id). El aislamiento NO depende de estos endpoints: el <see cref="IReportDataSource"/>
/// consulta a traves del DbContext tenant-scoped (filtro global fail-closed), asi que jamas se entrega
/// una cadena de conexion ni datos de otro tenant.
///
/// Este es el "JSON/Web data source" que el visor Bold Reports consumira en la Ola 2 (en vez de una
/// connection string), y el que la UI de dashboards ECharts pedira en la Ola 3.
/// </summary>
public static class ReportingEndpoints
{
    public static void MapReportingEndpoints(this WebApplication app)
    {
        // Catalogo: que fuentes/campos son reportables para el tenant activo.
        app.MapGet("/api/reporting/catalog", async (
            IReportCatalog catalog,
            CancellationToken ct) =>
        {
            var sources = await catalog.GetSourcesAsync(ct);
            var payload = sources.Select(s => new
            {
                key = s.Key,
                displayName = s.DisplayName,
                kind = s.Kind.ToString(),
                fields = s.Fields.Select(f => new
                {
                    key = f.Key,
                    displayName = f.DisplayName,
                    type = f.Type.ToString(),
                    canFilter = f.CanFilter,
                    canGroup = f.CanGroup,
                    canAggregate = f.CanAggregate
                })
            });
            return Results.Json(payload);
        }).RequireAuthorization("TenantMember");

        // Consulta: traduce un spec declarativo a filas ya filtradas por tenant.
        app.MapPost("/api/reporting/query", async (
            ReportQuerySpec spec,
            System.Security.Claims.ClaimsPrincipal user,
            IReportDataSource dataSource,
            CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId))
            {
                return Results.BadRequest(new { error = "No hay un tenant activo en la sesion." });
            }

            if (spec is null || string.IsNullOrWhiteSpace(spec.SourceKey))
            {
                return Results.BadRequest(new { error = "Falta la fuente (SourceKey) del reporte." });
            }

            try
            {
                var ds = await dataSource.QueryAsync(spec, new ReportContext(tenantId), ct);
                return Results.Json(new
                {
                    columns = ds.Columns.Select(c => new { key = c.Key, displayName = c.DisplayName, type = c.Type.ToString() }),
                    rows = ds.Rows
                });
            }
            catch (ReportValidationException ex)
            {
                // El spec referencia algo fuera del catalogo o una operacion no admitida: 400, no 500.
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("TenantMember").DisableAntiforgery();

        // RDL de un imprimible: lo carga el diseniador Bold via openReportDefinition (client-side),
        // evitando el flujo openReport(path) que asume Report Server. Tenant-safe (GetRdlAsync).
        app.MapGet("/api/reporting/rdl/{id:guid}", async (
            Guid id,
            IReportDefinitionService defs,
            CancellationToken ct) =>
        {
            var rdl = await defs.GetRdlAsync(id, ct);
            return rdl is null ? Results.NotFound() : Results.Content(rdl, "application/xml");
        }).RequireAuthorization("TenantMember");

        // Guardado del RDL editado en el diseniador (saveReportDefinition -> POST). Tenant-safe.
        app.MapPost("/api/reporting/rdl/{id:guid}", async (
            Guid id,
            HttpRequest request,
            IReportDefinitionService defs,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var rdl = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(rdl))
            {
                return Results.BadRequest(new { error = "RDL vacio." });
            }

            var ok = await defs.UpdateRdlAsync(id, rdl, ct);
            return ok ? Results.Ok(new { ok = true }) : Results.NotFound();
        }).RequireAuthorization("TenantMember").DisableAntiforgery();
    }
}
