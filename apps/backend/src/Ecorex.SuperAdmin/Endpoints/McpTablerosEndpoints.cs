using Ecorex.Application.Mcp;
using Ecorex.Domain.Entities;

namespace Ecorex.SuperAdmin.Endpoints;

/// <summary>
/// Servidor MCP de TABLEROS (ADR-0159) para IAs externas: Claude (Code/Desktop), Gemini CLI, ChatGPT, etc.
///
/// El protocolo, la auth y la auditoria viven en <see cref="McpJsonRpcHost"/> (comun con el MCP de WhatsApp).
/// - Transporte "Streamable HTTP" (MCP 2025-06-18): POST /api/mcp/tableros con UN mensaje JSON-RPC 2.0,
///   respuesta application/json directa (sin SSE). GET responde 405 (no hay stream servidor->cliente);
///   las notificaciones responden 202. Sin sesiones (servidor sin estado).
/// - AUTH: Authorization: Bearer &lt;token personal&gt; (TenantApiToken con Scope "mcp:tableros" y TenantUserId).
///   El token resuelve tenant + USUARIO; toda herramienta corre COMO ese usuario (visibilidad, bitacora,
///   auditoria). Sin token / revocado / usuario inactivo -> 401 con WWW-Authenticate.
/// - AISLAMIENTO: la resolucion por hash es la unica lectura cross-tenant; luego AmbientTenantContext.Begin
///   + scope DI propio, y el filtro global aisla todo lo demas.
/// - AUDITORIA: toda herramienta de ESCRITURA exitosa escribe en super_admin_audit_logs (actor = el usuario,
///   reason "mcp-tableros"), ademas de la bitacora de la tarea ("Nombre (via IA)").
/// </summary>
public static class McpTablerosEndpoints
{
    public const string Route = "/api/mcp/tableros";

    private const string AuditReason = "mcp-tableros";

    private const string ServerInstructions =
        "Servidor de tableros de tareas de ECOREX. Actuas como el usuario dueno del token: solo ves y cambias lo " +
        "que el puede. Empieza con listar_tableros o mis_pasos_pendientes. Las tareas se identifican por su numero " +
        "(ej. T00123). Si una tarea tiene flujo en curso no la muevas de columna: usa ver_tarea y completar_paso. " +
        "Confirma con el usuario antes de cerrar pasos o mover tareas a columnas de cierre. " +
        "Directorio de contactos: busca antes de crear (buscar_contacto) para no duplicar; ver_contacto muestra la " +
        "ficha completa; editar_contacto cambia solo lo que envies (consulta listar_campos_contacto antes de usar " +
        "'campos'). Confirma con el usuario antes de inactivar un contacto. " +
        "Autoria de flujos: crear_flujo nace con inicio->fin conectados; mete pasos con agregar_nodo indicando " +
        "desde_nodo_id y hasta_nodo_id (inserta y reemplaza la conexion directa). Un nodo que no es compuerta sigue " +
        "TODAS sus salidas en paralelo: para decidir usa una compuerta con condicion_ruta. Con origen 'politica' asigna " +
        "cargos (asignar_cargo_nodo). Revisa ver_flujo: si trae 'avisos', muestraselos al usuario antes de publicar. " +
        "Para cambiar un flujo publicado usa nueva_version_flujo. Publica (publicar_flujo) y engancha a un concepto " +
        "(enganchar_flujo_a_concepto) para que las tareas lo arranquen. Autoria de formularios: apply_form_spec arma " +
        "uno completo (o create_form + add_container + add_question) y activate lo deja activo; consulta list_forms/" +
        "get_form antes de cambiar uno y confirma con el usuario antes de create_share_link (enlace publico). " +
        "Extraccion de datos (drones): crear_dron abre la cabecera (nombre + url_inicio); agrega pasos con agregar_paso " +
        "(tipo navegar/extraer/ia/procesar_archivos..., config fina en mapeo_json), variables con definir_variable " +
        "(marca 'secreta' las claves) y programalo con guardar_programacion (intervalo/cron/tramos). ver_dron muestra " +
        "pasos, variables y programaciones; listar_corridas, la bitacora. Confirma antes de eliminar un dron o un paso. " +
        "Contenedores de datos: crear_contenedor_datos abre el contenedor; agregale tablas con agregar_tabla y columnas " +
        "con agregar_columna (tipos texto/numero/decimal/fecha/booleano/submodelo); relaciona tablas con agregar_relacion. " +
        "Programa su importacion con guardar_programacion_importacion (manual/intervalo/cron, modo append/replace/upsert) y " +
        "revisa listar_corridas_importacion. guardar_conector NO recibe el secreto (ponlo en la pantalla). publicar_contenedor " +
        "saca una tabla al menu (privacidad por Roles y permisos). Confirma antes de borrar o despublicar. " +
        "Agentes de IA: crear_agente abre un agente (proveedor claude/gemini/chatgpt/deepseek, prompt de sistema); " +
        "nace apagado, enciendelo con encender_agente tras validarlo. Configuralo con editar_agente (herramientas on/off, " +
        "tableros, round-robin, concepto), agente_lector_medios (como lee PDF/imagenes), agregar_prompt_agente y " +
        "agregar_recurso_agente; ver_agente muestra todo. Si NO deshabilitas herramientas, el agente puede escribir en un " +
        "contenedor con agregar_filas_a_contenedor. Confirma antes de eliminar un agente. " +
        "Para clasificar filas de una tabla con un agente (encendido) usa clasificar_lote y repitela hasta restantes=0; " +
        "consume tokens de IA, asi que confirma con el usuario antes de procesar muchas filas. Para que se clasifique " +
        "SOLO lo nuevo despues de cada importacion usa configurar_paso_clasificacion en la programacion (ejecutar_" +
        "clasificacion_ahora la encola ya); limpiar_marca vacia la marca para reclasificar (confirma antes). " +
        "Para llenar una columna de filas ya cargadas desde otras columnas (ej. una clave de ingesta compuesta) usa " +
        "rellenar_columna con una plantilla \"{col1}|{col2}\" (por defecto solo filas vacias, repetible). " +
        "Reportes e indicadores: mira las fuentes con listar_fuentes_reporte y sus campos con ver_fuente_reporte (cada " +
        "tabla de un contenedor es una fuente); crear_reporte arma un panel y agregar_indicador un widget de un libro " +
        "(crear_libro_indicadores); la consulta va con nombres de campo y se ejecuta antes de guardar. " +
        "La IA solo puede lo que el usuario puede (permisos de flujos/formularios/contenedor-datos/agentes).";

    public static WebApplication MapMcpTablerosEndpoints(this WebApplication app)
    {
        app.MapPost(Route, (HttpContext http, IServiceScopeFactory scopes, CancellationToken ct) =>
                McpJsonRpcHost.HandleAsync(http, scopes, TenantApiToken.ScopeMcpTableros, "ecorex-mcp-tableros",
                    "mcp.tableros", AuditReason,
                    (sp, _, _) => Task.FromResult(McpBinding.Ok(sp.GetRequiredService<ITableroMcpToolset>(),
                        new McpServerInfo("ecorex-tableros", "ECOREX Tableros", ServerInstructions))),
                    ct))
            .DisableAntiforgery().AllowAnonymous().WithTags("mcp-tableros");

        // Sin stream servidor->cliente: la spec permite responder 405 al GET.
        app.MapGet(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        app.MapDelete(Route, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).AllowAnonymous();
        return app;
    }
}
