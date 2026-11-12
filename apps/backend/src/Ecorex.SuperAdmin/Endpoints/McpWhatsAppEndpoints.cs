using Ecorex.Application.Mcp;
using Ecorex.Domain.Entities;
using Ecorex.Domain.Enums;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Servidor MCP de WhatsApp POR LINEA Evolution API (ADR-0164): POST /api/mcp/whatsapp/{lineId}. Cada linea es un
/// servidor MCP propio (en el cliente: "whatsapp-ventas", "whatsapp-soporte"...), configurado desde /lineas.
/// Token PERSONAL de alcance mcp:whatsapp (un token de tableros no abre WhatsApp). La linea se busca con el filtro
/// global: una linea de otro tenant responde 404 aunque se cambie el id en la URL. Protocolo, auth y auditoria en
/// <see cref="McpJsonRpcHost"/>.
/// </summary>
public static class McpWhatsAppEndpoints
{
    public const string RoutePrefix = "/api/mcp/whatsapp";
    private const string Route = RoutePrefix + "/{lineId:guid}";
    private const string AuditReason = "mcp-whatsapp";

    /// <summary>URL del servidor MCP de una linea (para /lineas y la documentacion).</summary>
    public static string UrlFor(string baseUri, Guid lineId) => baseUri.TrimEnd('/') + RoutePrefix + "/" + lineId.ToString("D");

    public static WebApplication MapMcpWhatsAppEndpoints(this WebApplication app)
    {
        app.MapPost(Route, (Guid lineId, HttpContext http, IServiceScopeFactory scopes, CancellationToken ct) =>
                McpJsonRpcHost.HandleAsync(http, scopes, TenantApiToken.ScopeMcpWhatsApp, "ecorex-mcp-whatsapp",
                    "mcp.whatsapp", AuditReason,
                    async (sp, _, c) =>
                    {
                        var toolset = sp.GetRequiredService<IWhatsAppMcpToolset>();
                        var line = await toolset.DescribeLineAsync(lineId, c);
                        if (line is null)
                        {
                            return McpBinding.Fail(Results.Json(new { error = "La linea no existe en tu empresa." }, statusCode: StatusCodes.Status404NotFound));
                        }
                        if (line.Provider != WhatsAppProvider.Evolution)
                        {
                            return McpBinding.Fail(Results.Json(new { error = "Este MCP solo opera lineas de Evolution API." }, statusCode: StatusCodes.Status400BadRequest));
                        }
                        return McpBinding.Ok(toolset.ForLine(lineId),
                            new McpServerInfo("ecorex-whatsapp", $"WhatsApp {line.Name}", Instructions(line)));
                    },
                    ct))
            .DisableAntiforgery().AllowAnonymous().WithTags("mcp-whatsapp");

        app.MapGet(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        app.MapDelete(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        return app;
    }

    private static string Instructions(WhatsAppLineSummary line) =>
        $"Servidor de WhatsApp de la linea '{line.Name}'{(string.IsNullOrWhiteSpace(line.PhoneNumber) ? "" : $" ({line.PhoneNumber})")} " +
        "de ECOREX, via Evolution API. Actuas como el usuario dueno del token: lo que envias queda en Conversaciones a " +
        "su nombre '(via IA)' y el agente SARA deja de responder ese chat. Empieza con estado_linea. Antes de responder " +
        "un chat lee sus mensajes (leer_mensajes). Un destinatario por llamada; nada de envios masivos. Escribirle a un " +
        "numero que nunca escribio a la linea puede bloquear el numero: confirma con el usuario y usa " +
        "primer_contacto_confirmado=true. Confirma tambien antes de borrar mensajes, bloquear, salir de grupos, crear " +
        "grupos o publicar estados. Los archivos se envian por URL https publica (o base64 pequeno). Los grupos no " +
        "quedan en Conversaciones: leelos con fuente='whatsapp'. Para silenciar a SARA en un chat usa pausar_agente " +
        "(y reanudar_agente al terminar).";
}
