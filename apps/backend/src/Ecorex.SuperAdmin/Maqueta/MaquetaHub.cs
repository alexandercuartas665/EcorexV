using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

namespace Ecorex.SuperAdmin.Maqueta;

/// <summary>
/// Configuracion del hub de maquetas (ADR-0178), seccion <c>MaquetaHub</c> de appsettings. El area SOLO corre si
/// <see cref="Enabled"/> es true Y el entorno es Development (o, fuera de Development, si ademas
/// <see cref="AllowOutsideDevelopment"/> es true). En produccion no se configura: el area no existe.
/// </summary>
public sealed class MaquetaHubOptions
{
    public const string Section = "MaquetaHub";

    public bool Enabled { get; set; }

    /// <summary>Bandera explicita para correrlo fuera de Development (por defecto false).</summary>
    public bool AllowOutsideDevelopment { get; set; }

    public List<MaquetaHubProjectOptions> Projects { get; set; } = new();
}

/// <summary>Un proyecto de maqueta: carpeta en disco que se sirve bajo /maqueta/{Id}.</summary>
public sealed class MaquetaHubProjectOptions
{
    public string Id { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string RutaFisica { get; set; } = string.Empty;
}

/// <summary>Proyecto valido y publicado (la ruta fisica nunca sale de aqui hacia la UI).</summary>
public sealed record MaquetaHubProject(string Id, string Nombre, string Root)
{
    public string BasePath => MaquetaHub.PathPrefix + "/" + Id;
    public string AnotadorUrl => BasePath + "/anotador/anotador.html";
}

/// <summary>
/// Registro resuelto del hub: si esta activo y que proyectos se sirven. Singleton; se calcula una vez al arrancar.
/// </summary>
public sealed class MaquetaHubRegistry
{
    public MaquetaHubRegistry(bool isActive, IReadOnlyList<MaquetaHubProject> projects)
    {
        IsActive = isActive;
        Projects = isActive ? projects : Array.Empty<MaquetaHubProject>();
    }

    public bool IsActive { get; }

    public IReadOnlyList<MaquetaHubProject> Projects { get; }

    public MaquetaHubProject? Find(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : Projects.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Hub de maquetas (ADR-0178): hospeda el kit del anotador de cada maqueta bajo el MISMO origen de ECOREX
/// (/maqueta/{id}/...) detras del login, para que el anotador y la maqueta queden mismo-origen y el envio de
/// tarjetas (POST /api/maqueta/solicitudes) pueda usar la sesion. Solo Development (o flag explicito).
/// </summary>
public static partial class MaquetaHub
{
    public const string PathPrefix = "/maqueta";

    /// <summary>Policy de acceso al hub y a sus archivos: usuario de un tenant (las tarjetas caen en su tenant).</summary>
    public const string Policy = "MaquetaHub.Usar";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdPattern();

    /// <summary>True si el id es un segmento de URL seguro (minusculas, digitos, guion; max 40).</summary>
    public static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && IdPattern().IsMatch(id);

    /// <summary>
    /// Resuelve el registro: activo solo con Enabled + (Development o AllowOutsideDevelopment). Descarta (con aviso)
    /// proyectos con id invalido o repetido, o cuya carpeta no existe. La ruta debe ser ABSOLUTA.
    /// </summary>
    public static MaquetaHubRegistry Resolve(MaquetaHubOptions options, bool isDevelopment, ILogger? logger = null)
    {
        var active = options.Enabled && (isDevelopment || options.AllowOutsideDevelopment);
        if (!active) { return new MaquetaHubRegistry(false, Array.Empty<MaquetaHubProject>()); }

        var list = new List<MaquetaHubProject>();
        foreach (var p in options.Projects)
        {
            var id = (p.Id ?? string.Empty).Trim().ToLowerInvariant();
            if (!IsValidId(id))
            {
                logger?.LogWarning("MaquetaHub: proyecto con id invalido '{Id}' (use minusculas, digitos y guion). Se omite.", p.Id);
                continue;
            }
            if (list.Any(x => x.Id == id))
            {
                logger?.LogWarning("MaquetaHub: id repetido '{Id}'. Se omite.", id);
                continue;
            }
            var root = (p.RutaFisica ?? string.Empty).Trim();
            if (root.Length == 0 || !Path.IsPathFullyQualified(root) || !Directory.Exists(root))
            {
                logger?.LogWarning("MaquetaHub: la carpeta del proyecto '{Id}' no existe o no es una ruta absoluta. Se omite.", id);
                continue;
            }
            var nombre = string.IsNullOrWhiteSpace(p.Nombre) ? id : p.Nombre.Trim();
            list.Add(new MaquetaHubProject(id, nombre, Path.GetFullPath(root)));
        }
        return new MaquetaHubRegistry(true, list);
    }

    /// <summary>Registra opciones + registro resuelto (singleton).</summary>
    public static IServiceCollection AddMaquetaHub(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        var options = configuration.GetSection(MaquetaHubOptions.Section).Get<MaquetaHubOptions>() ?? new MaquetaHubOptions();
        services.AddSingleton(sp => Resolve(options, isDevelopment, sp.GetService<ILoggerFactory>()?.CreateLogger("MaquetaHub")));
        return services;
    }

    /// <summary>
    /// Middleware del area /maqueta/{id}/...: va DESPUES de UseAuthentication/UseAuthorization. Siempre activo para
    /// las maquetas en la NUBE (ADR-0179); las de disco local solo si el hub esta activo (ADR-0178). Exige sesion de un usuario de tenant; sirve solo
    /// archivos dentro de la carpeta del proyecto (PhysicalFileProvider rechaza '..' y rutas absolutas; se excluyen
    /// ocultos y dot-files como .git/.env), solo tipos conocidos y sin listado de directorios.
    /// </summary>
    public static WebApplication UseMaquetaHub(this WebApplication app)
    {
        var registry = app.Services.GetRequiredService<MaquetaHubRegistry>();
        if (registry.IsActive)
        {
            var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MaquetaHub");
            log.LogInformation("MaquetaHub activo: {Count} proyecto(s) en {Prefix}/{{id}}: {Ids}",
                registry.Projects.Count, PathPrefix, string.Join(", ", registry.Projects.Select(p => p.Id)));
        }

        app.UseWhen(IsHubFileRequest, branch =>
        {
            branch.Use(async (ctx, next) =>
            {
                var user = ctx.User;
                if (user.Identity?.IsAuthenticated != true)
                {
                    if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
                    {
                        // El login no maneja ReturnUrl: se vuelve a entrar por /maqueta tras iniciar sesion.
                        ctx.Response.Redirect("/login");
                        return;
                    }
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                if (user.FindFirst("tenant_id") is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next();
            });

            // 1) Maquetas de DISCO local (solo Development/bandera, ADR-0178).
            foreach (var project in registry.Projects)
            {
                branch.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(project.Root, ExclusionFilters.Sensitive),
                    RequestPath = project.BasePath,
                    ContentTypeProvider = new FileExtensionContentTypeProvider(),
                    ServeUnknownFileTypes = false,
                    OnPrepareResponse = c =>
                    {
                        c.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                        c.Context.Response.Headers["Cache-Control"] = "no-cache";
                        c.Context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                    }
                });
            }

            // 2) Maquetas en la NUBE del tenant de la sesion (ADR-0179): /maqueta/{slug}/... o /maqueta/{slug}~{n}/...
            branch.Use(ServeCloudMockupAsync);
        });
        return app;
    }

    /// <summary>
    /// Sirve un archivo de la version de maqueta del tenant de la sesion. Si el slug no existe en el tenant (o el
    /// archivo no es servible), sigue el pipeline (404 normal). Nunca cruza tenants: la resolucion corre bajo el filtro
    /// global con el tenant de la cookie.
    /// </summary>
    private static async Task ServeCloudMockupAsync(HttpContext ctx, RequestDelegate next)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)) { await next(ctx); return; }
        if (!TryParseCloudPath(ctx.Request.Path, out var slug, out var versionNumber, out var subPath)) { await next(ctx); return; }

        var mockups = ctx.RequestServices.GetRequiredService<Ecorex.Application.Integraciones.Maqueta.IMockupService>();
        var target = await mockups.ResolveForServingAsync(slug, versionNumber, ctx.RequestAborted);
        if (target is null) { await next(ctx); return; }

        var cache = ctx.RequestServices.GetRequiredService<MockupSiteCache>();
        var files = ctx.RequestServices.GetRequiredService<Ecorex.Application.Storage.IFileStorageService>();
        var root = await cache.EnsureExtractedAsync(target, files);
        if (!await cache.TryServeAsync(ctx, target.VersionId, root, subPath)) { await next(ctx); }
    }

    /// <summary>
    /// /maqueta/{slug}/{resto} -> (slug, null, /resto); /maqueta/{slug}~{n}/{resto} -> (slug, n, /resto).
    /// </summary>
    public static bool TryParseCloudPath(PathString path, out string slug, out int? versionNumber, out PathString subPath)
    {
        slug = string.Empty;
        versionNumber = null;
        subPath = PathString.Empty;
        if (!path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase, out var rest) || !rest.HasValue) { return false; }
        var value = rest.Value!;
        var slash = value.IndexOf('/', 1);
        if (slash < 0) { return false; }
        var segment = value[1..slash].ToLowerInvariant();
        subPath = new PathString(value[slash..]);
        var tilde = segment.IndexOf('~');
        if (tilde >= 0)
        {
            if (!int.TryParse(segment[(tilde + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0)
            {
                return false;
            }
            versionNumber = n;
            segment = segment[..tilde];
        }
        if (!IsValidId(segment)) { return false; }
        slug = segment;
        return true;
    }

    /// <summary>/maqueta/{algo}... (la pagina launcher /maqueta exacta NO entra: la atiende Blazor).</summary>
    private static bool IsHubFileRequest(HttpContext ctx)
        => ctx.Request.Path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase, out var rest)
           && rest.HasValue && rest.Value!.Length > 1;
}
