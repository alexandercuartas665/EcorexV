using Ecorex.Application.Mcp;
using Ecorex.Domain.Entities;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Servidor MCP de MAQUETAS (ADR-0182) para la IA del PC local (Claude Code abierto en la carpeta de la maqueta):
/// lee las solicitudes anotadas en el editor del sistema (con captura y selector), las comenta/mueve/resuelve y
/// obtiene el comando para publicar la version nueva. Token PERSONAL de alcance integ:maqueta: el MISMO token sirve
/// a este MCP, a la subida de versiones (POST /api/maqueta/proyectos/{slug}/versiones) y al envio de solicitudes.
/// Protocolo, auth, aislamiento por tenant y auditoria en <see cref="McpJsonRpcHost"/>.
/// </summary>
public static class McpMaquetasEndpoints
{
    public const string Route = "/api/mcp/maquetas";

    private const string AuditReason = "mcp-maquetas";

    private const string ServerInstructions =
        "Servidor de MAQUETAS de ECOREX. Actuas como el usuario dueno del token. Flujo de trabajo: listar_proyectos -> " +
        "listar_solicitudes (abiertas) -> ver_solicitud (trae la captura con el elemento resaltado, el archivo de la " +
        "maqueta y el selector CSS) -> aplica el ajuste en los archivos de la carpeta LOCAL de la maqueta (la carpeta " +
        "abierta en tu sesion; las rutas de 'archivo' son relativas a su raiz) -> comentar_solicitud con lo que cambiaste " +
        "-> cuando el usuario lo apruebe, publica la version con el comando de como_publicar_version -> resolver_solicitud " +
        "indicando la version publicada. Opcional: mover_solicitud a 'En progreso' al empezar. La maqueta es HTML puro: no " +
        "agregues el kit anotador ni scripts de ECOREX. Nunca escribas el token en archivos ni en el chat. Confirma con el " +
        "usuario antes de publicar una version (queda como actual para toda la empresa). CEREBRO DIGITAL (ADR-0183): cada " +
        "proyecto tiene notas markdown enlazadas tipo Obsidian sobre lo que se esta construyendo. Para pensar con el " +
        "usuario: grafo_cerebro y listar_notas/buscar_notas -> ver_nota (contenido + backlinks); guarda conclusiones, " +
        "decisiones y preguntas abiertas con crear_nota o editar_nota (modo agregar). Enlaza con [[Titulo de nota]], " +
        "[[pantalla.html]], [[T00046]] y #etiqueta.";

    public static WebApplication MapMcpMaquetasEndpoints(this WebApplication app)
    {
        app.MapPost(Route, (HttpContext http, IServiceScopeFactory scopes, CancellationToken ct) =>
                McpJsonRpcHost.HandleAsync(http, scopes, TenantApiToken.ScopeMaqueta, "ecorex-mcp-maquetas",
                    "mcp.maquetas", AuditReason,
                    (sp, _, _) =>
                    {
                        var toolset = sp.GetRequiredService<IMaquetaMcpToolset>();
                        toolset.PublicBaseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
                        return Task.FromResult(McpBinding.Ok(toolset,
                            new McpServerInfo("ecorex-maquetas", "ECOREX Maquetas", ServerInstructions)));
                    },
                    ct))
            .DisableAntiforgery().AllowAnonymous().WithTags("mcp-maquetas");

        // Sin stream servidor->cliente: la spec permite responder 405 al GET.
        app.MapGet(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        app.MapDelete(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        return app;
    }
}
