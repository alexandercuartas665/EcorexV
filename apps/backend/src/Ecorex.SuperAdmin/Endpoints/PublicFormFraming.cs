namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Permite EMBEBER el formulario publico /f/{token} como iframe en CUALQUIER dominio (p. ej. el sitio web de un
/// cliente que capta leads). Por defecto el app manda en toda respuesta HTML dos cabeceras anti-framing que no
/// vienen de un middleware propio sino del framework: <c>X-Frame-Options: SAMEORIGIN</c> (UseAntiforgery, al
/// emitir el token del formulario) y <c>Content-Security-Policy: frame-ancestors 'self'</c> (Blazor Interactive
/// Server, ContentSecurityFrameAncestorsPolicy). SOLO para /f/... se quitan en OnStarting (justo antes de enviar
/// las cabeceras, despues de que el framework las puso): se omite X-Frame-Options (no tiene valor "permitir
/// cualquiera") y se fija <c>frame-ancestors *</c>. El resto del app (consola) conserva las cabeceras estrictas.
/// El circuito del visor (/_blazor) es del mismo origen que el iframe y no usa cookie de sesion.
/// </summary>
public static class PublicFormFraming
{
    public const string FrameAncestorsAny = "frame-ancestors *";

    /// <summary>True para la ruta del formulario publico (/f/{token}).</summary>
    public static bool IsPublicFormPath(PathString path)
        => path.StartsWithSegments("/f", StringComparison.OrdinalIgnoreCase, out var rest) && rest.HasValue && rest.Value!.Length > 1;

    /// <summary>Quita X-Frame-Options y abre frame-ancestors a cualquier origen (conservando otras directivas CSP).</summary>
    public static void AllowAnyAncestor(IHeaderDictionary headers)
    {
        headers.Remove("X-Frame-Options");
        var csp = headers.ContentSecurityPolicy.ToString();
        var others = csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => !d.StartsWith("frame-ancestors", StringComparison.OrdinalIgnoreCase))
            .ToList();
        others.Add(FrameAncestorsAny);
        headers.ContentSecurityPolicy = string.Join("; ", others);
    }

    /// <summary>Middleware: registrar ANTES de UseAntiforgery / MapRazorComponents.</summary>
    public static IApplicationBuilder UsePublicFormFraming(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            if (IsPublicFormPath(context.Request.Path))
            {
                var response = context.Response;
                response.OnStarting(() =>
                {
                    AllowAnyAncestor(response.Headers);
                    return Task.CompletedTask;
                });
            }
            await next();
        });
}
