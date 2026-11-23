using Ecorex.Application.Formacion;
using Ecorex.Application.Integraciones.Maqueta;
using Ecorex.Application.Storage;
using Ecorex.SuperAdmin.Maqueta;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Entrega de contenido del modulo de Formacion (ADR-0202).
/// - /formacion/recurso/{leccionId}: video, PDF, imagen o archivo de una leccion, por STREAMING con rangos HTTP
///   (adelantar un video sin cargarlo en memoria). Requiere sesion y permiso Ver de "formacion"; un curso no
///   publicado solo lo ve quien puede editar.
/// - /formacion/w/{token}/...: archivos del WIZARD HTML. El reproductor lo muestra en un iframe con sandbox SIN
///   allow-same-origin (origen opaco: el HTML subido no puede actuar como el usuario dentro de ECOREX); como ese
///   iframe no envia la cookie, la autorizacion va en un token firmado y con vencimiento (Data Protection) que la
///   pagina emite solo a quien ya pudo ver la leccion. Ademas se responde con CSP sandbox para que el wizard quede
///   aislado aunque se abra en una pestana aparte.
/// </summary>
public static class FormacionEndpoints
{
    public const string ViewPolicy = "Perm:formacion:View";
    public const string EditPolicy = "Perm:formacion:Edit";

    private const string WizardPrefix = "/formacion/w";
    private static readonly TimeSpan WizardTokenLifetime = TimeSpan.FromHours(12);
    private const string WizardSandbox = "sandbox allow-scripts allow-forms allow-popups allow-modals allow-downloads";

    public static void MapFormacionEndpoints(WebApplication app)
    {
        app.MapGet("/formacion/recurso/{leccionId:guid}", async (Guid leccionId, HttpContext http, IFormacionService formacion,
            IFileStorageService files, IAuthorizationService auth, CancellationToken ct) =>
        {
            var puedeEditar = (await auth.AuthorizeAsync(http.User, EditPolicy)).Succeeded;
            var fileId = await formacion.ResolverRecursoAsync(leccionId, puedeEditar, ct);
            if (fileId is not Guid id) { return Results.NotFound(); }
            var file = await files.OpenReadAsync(id, ct);
            if (file is null) { return Results.NotFound(); }
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers.CacheControl = "private, max-age=3600";
            var dl = http.Request.Query.ContainsKey("dl");
            // SVG subido por un usuario: se fuerza descarga para que no corra script en el origen de la app.
            var esSvg = file.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase);
            return Results.File(file.Content, file.ContentType, fileDownloadName: dl || esSvg ? file.FileName : null,
                enableRangeProcessing: true);
        }).RequireAuthorization(ViewPolicy);

        app.MapMethods(WizardPrefix + "/{token}/{**path}", new[] { HttpMethods.Get, HttpMethods.Head },
            async (string token, string? path, HttpContext http, IDataProtectionProvider dp, IFormacionService formacion,
                MockupSiteCache cache, IFileStorageService files, CancellationToken ct) =>
        {
            if (!TryReadToken(dp, token, out var leccionId, out var tenantId)) { return Results.NotFound(); }
            var target = await formacion.ResolverWizardParaTenantAsync(leccionId, tenantId, ct);
            if (target is null) { return Results.NotFound(); }
            var sub = string.IsNullOrEmpty(path) ? target.EntryPath : path;
            var mock = new MockupServeTarget(target.TenantId, target.ZipFileId, target.ZipFileId, target.RootPrefix, target.EntryPath);
            var root = await cache.EnsureExtractedAsync(mock, files);
            http.Response.Headers["Content-Security-Policy"] = WizardSandbox;
            http.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            var subPath = new PathString("/" + sub.TrimStart('/'));
            if (!http.Request.Path.Value!.EndsWith(subPath.Value!, StringComparison.Ordinal))
            {
                // /formacion/w/{token}/ sin archivo: se redirige a la entrada para que las rutas relativas resuelvan.
                return Results.Redirect($"{WizardPrefix}/{token}/{target.EntryPath}");
            }
            // StaticFileMiddleware se salta la peticion si ya hay un endpoint con delegado seleccionado (este mismo):
            // se limpia para que sirva el archivo del cache como en las maquetas.
            http.SetEndpoint(null);
            return await cache.TryServeAsync(http, target.ZipFileId, root, subPath) ? Results.Empty : Results.NotFound();
        }).AllowAnonymous();
    }

    /// <summary>URL firmada de la entrada del wizard para el iframe (vence en 12 h).</summary>
    public static string WizardUrl(IDataProtectionProvider dp, WizardTarget target)
    {
        var protector = dp.CreateProtector("formacion.wizard.v1").ToTimeLimitedDataProtector();
        var token = protector.Protect($"{target.LeccionId:N}.{target.TenantId:N}", WizardTokenLifetime);
        return $"{WizardPrefix}/{token}/{target.EntryPath}";
    }

    private static bool TryReadToken(IDataProtectionProvider dp, string token, out Guid leccionId, out Guid tenantId)
    {
        leccionId = Guid.Empty;
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 600) { return false; }
        try
        {
            var protector = dp.CreateProtector("formacion.wizard.v1").ToTimeLimitedDataProtector();
            var raw = protector.Unprotect(token);
            var parts = raw.Split('.');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out leccionId) && Guid.TryParseExact(parts[1], "N", out tenantId);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }
}
