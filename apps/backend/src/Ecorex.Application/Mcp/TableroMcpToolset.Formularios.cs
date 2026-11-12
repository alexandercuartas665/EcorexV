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

    private HashSet<string>? _formToolNames;

    /// <summary>Nombres de las herramientas del toolset de formularios inyectado (vacio si no hay).</summary>
    private HashSet<string> FormToolNames => _formToolNames ??= _formAuthoring is null
        ? new HashSet<string>(StringComparer.Ordinal)
        : _formAuthoring.GetSpecs().Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

    private async Task<McpToolResult> ExecuteFormAsync(string tool, string argumentsJson, McpActor actor, CancellationToken ct)
    {
        if (_formAuthoring is null) { return Err("La autoria de formularios no esta disponible."); }

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
