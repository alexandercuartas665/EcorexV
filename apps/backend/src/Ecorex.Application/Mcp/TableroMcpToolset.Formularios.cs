using System.Text.Json;
using Ecorex.Application.Roles;

namespace Ecorex.Application.Mcp;

/// <summary>
/// AUTORIA DE FORMULARIOS en el servidor MCP (ADR-0159, addendum "autoria flujos+formularios").
/// NO reimplementa nada: PUENTEA el <see cref="Ecorex.Application.Tenancy.IFormAuthoringToolset"/> que ya
/// construye formularios (ADR-0067) y que hoy solo se exponia por el MCP de administracion (/api/mgmt, con
/// llave de admin, actuando como "Sistema"). Aqui esas mismas herramientas corren COMO el usuario del token
/// y bajo SU permiso del modulo "formularios" (igual que la pantalla de diseno exige Ver + Editar), con la
/// bitacora y la auditoria del usuario. Las herramientas conservan sus nombres nativos (create_form,
/// add_question, apply_form_spec, activate...) para no duplicar su contrato.
/// </summary>
public sealed partial class TableroMcpToolset
{
    /// <summary>Clave de modulo del Directorio de permisos para la autoria de formularios (ver Program.cs).</summary>
    private const string FormModuleKey = "formularios";

    /// <summary>Herramientas de formularios que BORRAN estructura (reversibles por versionado, pero la IA confirma).</summary>
    private static readonly HashSet<string> FormDestructive = new(StringComparer.Ordinal)
    {
        "delete_container", "delete_question", "archive"
    };

    /// <summary>
    /// LISTA PERMITIDA (segura por construccion): solo estas herramientas del toolset de formularios se
    /// exponen al usuario final. Una herramienta nueva del toolset NO aparece aqui sola. Quedan FUERA, y solo
    /// en el MCP de administracion (/api/mgmt):
    /// - list_tenants: lee TODOS los tenants (IgnoreQueryFilters) -> fuga cross-tenant (regla #1 de CLAUDE.md).
    /// - list_menu_views, list_menu_nodes, set_module: configuracion del MENU del tenant (otro modulo/permiso).
    /// - create_data_container, add_container_rows: escritura en el Contenedor de datos (otro modulo/permiso);
    ///   sus LECTURAS si se permiten porque el disenador las usa para configurar listas/lookups.
    /// </summary>
    private static readonly HashSet<string> FormBridgeAllowed = new(StringComparer.Ordinal)
    {
        // Descubrimiento (lectura)
        "describe_components", "list_forms", "get_form", "verify_form", "export_form", "get_render_urls",
        "list_templates", "list_data_containers", "describe_data_container", "list_tercero_fields",
        "list_org_units", "list_activity_types",
        // Estructura
        "apply_form_spec", "create_form", "import_form", "update_form_header",
        "add_container", "update_container", "move_container", "delete_container",
        "add_question", "update_question", "move_question", "delete_question",
        // Comportamiento y ciclo de vida
        "set_transactional", "set_sequence_next", "set_custom_css", "set_theme", "set_status_ladder",
        "wire_submit_task_rule", "activate", "deactivate", "archive",
        // Impresion, enlaces y registros
        "create_template", "update_template", "set_default_template", "wire_print_button", "wire_convert_button",
        "create_share_link", "create_record"
    };

    private HashSet<string>? _formToolNames;

    /// <summary>Herramientas de formularios EXPUESTAS: las del toolset inyectado que estan en la lista permitida.</summary>
    private HashSet<string> FormToolNames => _formToolNames ??= _formAuthoring is null
        ? new HashSet<string>(StringComparer.Ordinal)
        : _formAuthoring.GetSpecs().Select(s => s.Name).Where(FormBridgeAllowed.Contains).ToHashSet(StringComparer.Ordinal);

    private async Task<McpToolResult> ExecuteFormAsync(string tool, string argumentsJson, McpActor actor, CancellationToken ct)
    {
        if (_formAuthoring is null) { return Err("La autoria de formularios no esta disponible."); }
        if (!FormBridgeAllowed.Contains(tool)) { return Err($"Herramienta desconocida: {tool}"); }

        // Mismo permiso que la pantalla de diseno (Formularios.Disenar = Ver + Editar). Lectura => Ver; cambio => Editar.
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var readOnly = _formAuthoring.ReadOnlyTools.Contains(tool);
        var action = readOnly ? PermissionAction.View : PermissionAction.Edit;
        if (!perms.Can(FormModuleKey, action))
        {
            return Err(readOnly
                ? "Tu usuario no tiene permiso para consultar formularios."
                : "Tu usuario no tiene permiso para disenar formularios (requiere Ver y Editar en Formularios).");
        }

        // Puente directo: el toolset de formularios devuelve su propio JSON (errores estructurados con "ok": false).
        var tr = await _formAuthoring.ExecuteAsync(tool, argumentsJson, actor.PlatformUserId, autonomous: false, ct);
        return new McpToolResult(tr.Json, StructuredResultIsError(tr.Json));
    }

    /// <summary>Un resultado estructurado es error si trae "ok": false (convencion compartida con /api/mgmt).</summary>
    private static bool StructuredResultIsError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("ok", out var ok)
                   && ok.ValueKind == JsonValueKind.False;
        }
        catch (JsonException) { return false; }
    }
}
