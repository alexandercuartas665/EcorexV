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

        // Ola 2: certificado PDF del usuario actual (solo si termino el curso).
        app.MapGet("/formacion/certificado/{cursoId:guid}", async (Guid cursoId, HttpContext http, IFormacionEvaluacionService eval,
            Ecorex.Application.Common.IQuotePdfRenderer pdf, CancellationToken ct) =>
        {
            var cert = await eval.CertificadoAsync(cursoId, ct);
            if (cert is null) { return Results.NotFound(); }
            var verificar = $"{http.Request.Scheme}://{http.Request.Host}/formacion/verificar/{cert.Codigo}";
            var bytes = await pdf.RenderHtmlToPdfAsync(CertificadoHtml(cert, verificar), ct);
            return Results.File(bytes, "application/pdf", $"certificado-{cert.Codigo}.pdf");
        }).RequireAuthorization(ViewPolicy);

        // Ola 2: verificacion PUBLICA del certificado por su codigo (datos minimos: persona, curso, empresa, fecha).
        app.MapGet("/formacion/verificar/{codigo}", async (string codigo, IFormacionEvaluacionService eval, CancellationToken ct) =>
        {
            var cert = await eval.VerificarCertificadoAsync(codigo, ct);
            return Results.Content(VerificacionHtml(cert, codigo), "text/html; charset=utf-8");
        }).AllowAnonymous();

        // Ola 2: reporte de avance de un curso en Excel.
        app.MapGet("/formacion/admin/{cursoId:guid}/avance.xlsx", async (Guid cursoId, IFormacionEvaluacionService eval, CancellationToken ct) =>
        {
            var bytes = await eval.ExportarAvanceAsync(cursoId, ct);
            return bytes is null ? Results.NotFound()
                : Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"avance-curso-{cursoId:N}.xlsx");
        }).RequireAuthorization(EditPolicy);
    }

    /// <summary>HTML del certificado (A4 vertical) para el renderizador de PDF. Todo texto va codificado.</summary>
    public static string CertificadoHtml(CertificadoDto c, string urlVerificacion)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
        var nota = c.Nota is int n ? $"<p class=\"meta\">Calificacion: <strong>{n}/100</strong></p>" : string.Empty;
        var horas = c.DuracionMinutos is int m && m > 0 ? $"<p class=\"meta\">Intensidad: {(m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min")}</p>" : string.Empty;
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><style>
            body{margin:0;font-family:Georgia,'Times New Roman',serif;color:#1b1b1e}
            .frame{border:10px double #7c3aed;padding:56px 48px;min-height:240mm;box-sizing:border-box;text-align:center;position:relative}
            .brand{font-family:Arial,Helvetica,sans-serif;letter-spacing:.2em;text-transform:uppercase;color:#7c3aed;font-size:13px}
            h1{font-size:44px;margin:28px 0 8px;font-weight:normal}
            .sub{font-size:16px;color:#4b5563;margin:0 0 42px}
            .name{font-size:34px;font-weight:bold;border-bottom:1px solid #d1d5db;display:inline-block;padding:0 24px 8px;margin:6px 0 30px}
            .course{font-size:24px;font-style:italic;margin:8px 0 26px}
            .meta{font-family:Arial,Helvetica,sans-serif;font-size:13px;color:#374151;margin:4px 0}
            .foot{position:absolute;left:48px;right:48px;bottom:40px;font-family:Arial,Helvetica,sans-serif;font-size:11px;color:#6b7280}
            .code{font-family:'Courier New',monospace;font-size:14px;color:#1b1b1e;letter-spacing:.1em}
            </style></head><body><div class="frame">
            <div class="brand">{{E(c.Empresa)}}</div>
            <h1>Certificado</h1>
            <p class="sub">de finalizacion de curso</p>
            <p class="meta">Se certifica que</p>
            <div class="name">{{E(c.Persona)}}</div>
            <p class="meta">completo satisfactoriamente el curso</p>
            <div class="course">{{E(c.Curso)}}</div>
            <p class="meta">Fecha: <strong>{{c.Fecha:dd/MM/yyyy}}</strong></p>
            {{nota}}{{horas}}
            <div class="foot">Codigo de verificacion: <span class="code">{{E(c.Codigo)}}</span><br>Verificalo en {{E(urlVerificacion)}}</div>
            </div></body></html>
            """;
    }

    /// <summary>Pagina publica de verificacion de un certificado.</summary>
    public static string VerificacionHtml(CertificadoDto? c, string codigo)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
        var cuerpo = c is null
            ? $"<h1 style=\"color:#b91c1c\">Certificado no encontrado</h1><p>El codigo <code>{E(codigo)}</code> no corresponde a ningun certificado emitido.</p>"
            : $"<h1 style=\"color:#166534\">Certificado valido</h1><p><strong>{E(c.Persona)}</strong> completo el curso <strong>{E(c.Curso)}</strong> de <strong>{E(c.Empresa)}</strong> el {c.Fecha:dd/MM/yyyy}.</p><p>Codigo: <code>{E(c.Codigo)}</code></p>";
        return $"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"robots\" content=\"noindex\"><title>Verificacion de certificado</title></head><body style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;max-width:560px;margin:60px auto;padding:0 16px;color:#1b1b1e\">{cuerpo}<p style=\"color:#6b7280;font-size:13px\">ECOREX - Formacion</p></body></html>";
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
