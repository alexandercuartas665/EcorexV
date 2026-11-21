using System.Text.Json;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// AGENTES DE IA en el servidor MCP personal (ADR-0159, addendum agentes / ADR-0195). Capa delgada sobre el
/// servicio REAL del modulo (000867 / "agentes"): <see cref="IAiAgentService"/>. Permite crear, estructurar
/// (prompts enrutados + recursos), configurar (proveedor, modelo, prompt, lector de medios, herramientas
/// on/off, tableros permitidos, round-robin, concepto por defecto, historial) y encender/apagar agentes COMO
/// el usuario del token, tenant-scoped y auditado (el servicio escribe AdminAuditLog en cada mutacion).
///
/// PERMISO: el modulo "agentes" esta en la matriz de roles (ruta del menu). Estas herramientas exigen el
/// permiso efectivo del actor (View/Create/Edit/Delete), igual que flujos y contenedores. Owner/Admin y
/// sin-rol = acceso total. Aislamiento multi-tenant por el filtro global del servicio.
///
/// HERRAMIENTAS DEL AGENTE: DisabledTools es una whitelist POR EXCLUSION (las que NO estan en la lista quedan
/// habilitadas). Por eso crear un agente sin 'herramientas_deshabilitadas' deja habilitadas TODAS, incluida
/// 'agregar_filas_a_contenedor' (con la que el agente escribe en un contenedor de datos).
///
/// FUERA DE v1 (no estan en IAiAgentService; se configuran en la pantalla): cierre, reactivacion/seguimiento
/// y datos-cache del agente.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const string AgentModuleKey = "agentes";

    private static readonly HashSet<string> AgentReadOnly = new(StringComparer.Ordinal)
    {
        "listar_agentes", "ver_agente", "historial_prompts_agente"
    };

    private static readonly HashSet<string> AgentDestructive = new(StringComparer.Ordinal)
    {
        "eliminar_agente", "eliminar_recurso_agente", "eliminar_prompt_agente"
    };

    private static readonly HashSet<string> AgentToolNames = new(StringComparer.Ordinal)
    {
        "listar_agentes", "ver_agente", "historial_prompts_agente",
        "crear_agente", "editar_agente", "agente_lector_medios",
        "encender_agente", "apagar_agente", "duplicar_agente", "restaurar_version_prompt",
        "agregar_recurso_agente", "editar_recurso_agente", "agregar_prompt_agente", "editar_prompt_agente",
        "eliminar_agente", "eliminar_recurso_agente", "eliminar_prompt_agente"
    };

    private const string ProveedoresDoc = "claude, gemini, chatgpt o deepseek.";
    private const string TiposRecursoDoc = "imagen, video, audio, pdf, ubicacion, texto.";

    private static readonly AiToolSpec[] AgentSpecs =
    {
        // ---- Lectura ----
        new("listar_agentes",
            "Lista los agentes de IA del tenant: id, nombre, rol, proveedor, modelo, si esta encendido y " +
            "numero de recursos. Usala primero para conocer ids y nombres exactos.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_agente",
            "Muestra un agente completo: proveedor/modelo, prompt de sistema, prompt del lector de medios, " +
            "herramientas DESHABILITADAS (vacio = todas habilitadas), tableros permitidos, asignados " +
            "round-robin, concepto por defecto, recursos y prompts enrutados. 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string","description":"id o nombre del agente"}},"required":["agente"],"additionalProperties":false}"""),
        new("historial_prompts_agente",
            "Historial de versiones de los prompts del agente (red de seguridad, ultimas 5), mas reciente " +
            "primero. Si envias 'prompt' muestra solo las versiones de ese prompt enrutado por nombre. " +
            "'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"},"prompt":{"type":"string","description":"nombre de un prompt enrutado (opcional)"}},"required":["agente"],"additionalProperties":false}"""),

        // ---- Crear / configurar ----
        new("crear_agente",
            "Crea un agente de IA NUEVO. 'proveedor': " + ProveedoresDoc + " 'modelo' opcional (vacio = el por " +
            "defecto del proveedor). 'prompt' = prompt de sistema (rol). 'herramientas_deshabilitadas' " +
            "(opcional) = lista de nombres de herramientas a APAGAR; si la omites quedan TODAS habilitadas " +
            "(incluida 'agregar_filas_a_contenedor', con la que el agente escribe a un contenedor). Nace " +
            "APAGADO (enciendelo con encender_agente). Devuelve 'agente_id'.",
            """{"type":"object","properties":{"nombre":{"type":"string"},"rol":{"type":"string","description":"rol/tipo descriptivo"},"proveedor":{"type":"string","enum":["claude","gemini","chatgpt","deepseek"]},"modelo":{"type":"string"},"prompt":{"type":"string","description":"prompt de sistema"},"herramientas_deshabilitadas":{"type":"array","items":{"type":"string"}},"reacciones_activas":{"type":"boolean"},"reaccion_n":{"type":"integer"},"reaccion_m":{"type":"integer"},"reaccion_emojis":{"type":"string"}},"required":["nombre"],"additionalProperties":false}"""),
        new("editar_agente",
            "Edita un agente (reemplazo por campo: lo que NO envies conserva su valor actual). Cambia nombre, " +
            "rol, proveedor (" + ProveedoresDoc + "), modelo, prompt de sistema, herramientas deshabilitadas, " +
            "tableros permitidos (nombre o id; lista vacia = sin restriccion), asignados round-robin (email o " +
            "nombre) y concepto por defecto (codigo o nombre; cadena vacia = ninguno). El lector de medios se " +
            "edita con agente_lector_medios. 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"},"nombre":{"type":"string"},"rol":{"type":"string"},"proveedor":{"type":"string","enum":["claude","gemini","chatgpt","deepseek"]},"modelo":{"type":"string"},"prompt":{"type":"string"},"herramientas_deshabilitadas":{"type":"array","items":{"type":"string"}},"tableros_permitidos":{"type":"array","items":{"type":"string"}},"asignados_round_robin":{"type":"array","items":{"type":"string"}},"concepto_default":{"type":"string"},"reacciones_activas":{"type":"boolean"},"reaccion_n":{"type":"integer"},"reaccion_m":{"type":"integer"},"reaccion_emojis":{"type":"string"}},"required":["agente"],"additionalProperties":false}"""),
        new("agente_lector_medios",
            "Fija (o limpia) el prompt del LECTOR DE MEDIOS del agente: el prompt con que lee cada imagen o PDF " +
            "que recibe, antes de responder. 'prompt' vacio o ausente = lector generico. 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"},"prompt":{"type":"string"}},"required":["agente"],"additionalProperties":false}"""),
        new("encender_agente",
            "Enciende el agente (lo pone en produccion). 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"}},"required":["agente"],"additionalProperties":false}"""),
        new("apagar_agente",
            "Apaga el agente (lo saca de produccion). 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"}},"required":["agente"],"additionalProperties":false}"""),
        new("duplicar_agente",
            "Duplica un agente con su config (prompts, recursos, herramientas, historial), SIN la linea de " +
            "atencion ni los datos de conversacion. La copia queda APAGADA. 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"}},"required":["agente"],"additionalProperties":false}"""),
        new("restaurar_version_prompt",
            "Restaura una version del historial de prompts del agente (ver historial_prompts_agente). " +
            "'version' = indice (0 = mas reciente). 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"},"version":{"type":"integer","minimum":0}},"required":["agente","version"],"additionalProperties":false}"""),

        // ---- Recursos ----
        new("agregar_recurso_agente",
            "Agrega un recurso al agente (archivo/dato que puede usar para responder). 'tipo': " + TiposRecursoDoc +
            " 'detalle' (texto/descripcion), 'archivo_url' y 'archivo_nombre' opcionales. 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"},"nombre":{"type":"string"},"tipo":{"type":"string","enum":["imagen","video","audio","pdf","ubicacion","texto"]},"detalle":{"type":"string"},"archivo_url":{"type":"string"},"archivo_nombre":{"type":"string"}},"required":["agente","nombre","tipo"],"additionalProperties":false}"""),
        new("editar_recurso_agente",
            "Edita un recurso del agente por su id (ver ver_agente). Cambia nombre, tipo (" + TiposRecursoDoc +
            "), detalle y archivo.",
            """{"type":"object","properties":{"recurso_id":{"type":"string"},"nombre":{"type":"string"},"tipo":{"type":"string","enum":["imagen","video","audio","pdf","ubicacion","texto"]},"detalle":{"type":"string"},"archivo_url":{"type":"string"},"archivo_nombre":{"type":"string"}},"required":["recurso_id"],"additionalProperties":false}"""),

        // ---- Prompts enrutados ----
        new("agregar_prompt_agente",
            "Agrega un prompt ENRUTADO al agente (ademas del prompt base): se activa cuando su 'regla' aplica. " +
            "'cuerpo' = texto del prompt. 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"},"nombre":{"type":"string"},"regla":{"type":"string","description":"condicion de enrutamiento (opcional)"},"cuerpo":{"type":"string"}},"required":["agente","nombre","cuerpo"],"additionalProperties":false}"""),
        new("editar_prompt_agente",
            "Edita un prompt enrutado por su id (ver ver_agente). Cambia nombre, regla y cuerpo.",
            """{"type":"object","properties":{"prompt_id":{"type":"string"},"nombre":{"type":"string"},"regla":{"type":"string"},"cuerpo":{"type":"string"}},"required":["prompt_id"],"additionalProperties":false}"""),

        // ---- Borrado ----
        new("eliminar_agente",
            "Borra un agente COMPLETO con sus recursos, prompts y config. Confirma con el usuario antes de " +
            "usarla. 'agente' = id o nombre.",
            """{"type":"object","properties":{"agente":{"type":"string"}},"required":["agente"],"additionalProperties":false}"""),
        new("eliminar_recurso_agente",
            "Borra un recurso del agente por su id (ver ver_agente).",
            """{"type":"object","properties":{"recurso_id":{"type":"string"}},"required":["recurso_id"],"additionalProperties":false}"""),
        new("eliminar_prompt_agente",
            "Borra un prompt enrutado del agente por su id (ver ver_agente).",
            """{"type":"object","properties":{"prompt_id":{"type":"string"}},"required":["prompt_id"],"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> ExecuteAgentAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_agents is null) { return Err("El modulo de Agentes de IA no esta disponible."); }

        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = tool switch
        {
            "listar_agentes" or "ver_agente" or "historial_prompts_agente"
                => NeedAgent(perms, PermissionAction.View, "consultar agentes de IA"),
            "crear_agente" => NeedAgent(perms, PermissionAction.Create, "crear agentes de IA"),
            "eliminar_agente" or "eliminar_recurso_agente" or "eliminar_prompt_agente"
                => NeedAgent(perms, PermissionAction.Delete, "borrar agentes de IA"),
            _ => NeedAgent(perms, PermissionAction.Edit, "editar agentes de IA")
        };
        if (denied is not null) { return Err(denied); }

        return tool switch
        {
            "listar_agentes" => await ListAgentsAsync(ct),
            "ver_agente" => await ViewAgentAsync(args, ct),
            "historial_prompts_agente" => await AgentPromptHistoryAsync(args, ct),
            "crear_agente" => await CreateAgentAsync(args, actor, ct),
            "editar_agente" => await EditAgentAsync(args, actor, ct),
            "agente_lector_medios" => await SetAgentMediaReaderAsync(args, actor, ct),
            "encender_agente" => await SetAgentActiveAsync(args, actor, true, ct),
            "apagar_agente" => await SetAgentActiveAsync(args, actor, false, ct),
            "duplicar_agente" => await DuplicateAgentAsync(args, actor, ct),
            "restaurar_version_prompt" => await RestoreAgentPromptAsync(args, actor, ct),
            "agregar_recurso_agente" => await AddAgentResourceAsync(args, actor, ct),
            "editar_recurso_agente" => await EditAgentResourceAsync(args, actor, ct),
            "agregar_prompt_agente" => await AddAgentPromptAsync(args, actor, ct),
            "editar_prompt_agente" => await EditAgentPromptAsync(args, actor, ct),
            "eliminar_agente" => await DeleteAgentAsync(args, actor, ct),
            "eliminar_recurso_agente" => await DeleteAgentResourceAsync(args, actor, ct),
            "eliminar_prompt_agente" => await DeleteAgentPromptAsync(args, actor, ct),
            _ => Err($"Herramienta desconocida: {tool}")
        };
    }

    private static string? NeedAgent(EffectivePermissions perms, PermissionAction action, string what)
        => perms.Can(AgentModuleKey, action) ? null : $"Tu usuario no tiene permiso para {what}.";

    // ===================== Lectura =====================

    private async Task<McpToolResult> ListAgentsAsync(CancellationToken ct)
    {
        var list = await _agents!.ListAsync(ct);
        return Ok(new
        {
            agentes = list.Select(a => new
            {
                agente_id = a.Id,
                nombre = a.Name,
                rol = a.Role,
                proveedor = ProviderEs(a.Provider),
                modelo = a.Model,
                encendido = a.IsActive,
                recursos = a.ResourceCount
            })
        });
    }

    private async Task<McpToolResult> ViewAgentAsync(JsonElement args, CancellationToken ct)
    {
        var (detail, error) = await ResolveAgentDetailAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var a = detail!.Agent;
        return Ok(new
        {
            agente_id = a.Id,
            nombre = a.Name,
            rol = a.Role,
            proveedor = ProviderEs(a.Provider),
            modelo = a.Model,
            encendido = a.IsActive,
            prompt = a.SystemPrompt,
            lector_medios = a.MediaReaderPrompt,
            herramientas_deshabilitadas = a.DisabledTools ?? (IReadOnlyList<string>)Array.Empty<string>(),
            aviso_herramientas = (a.DisabledTools is null || a.DisabledTools.Count == 0)
                ? "Sin herramientas deshabilitadas: todas estan habilitadas (incluida agregar_filas_a_contenedor)."
                : null,
            tableros_permitidos = a.AllowedBoardIds,
            asignados_round_robin = a.AssigneeUserIds,
            concepto_default_id = a.DefaultSubcategoriaId,
            reacciones = new { activas = a.ReactionsEnabled, n = a.ReactionRatioN, m = a.ReactionRatioM, emojis = a.ReactionEmojis },
            recursos = detail.Resources.Select(r => new
            {
                recurso_id = r.Id,
                nombre = r.Name,
                tipo = ResourceTypeEs(r.ResourceType),
                detalle = r.Detail,
                archivo_url = r.FileUrl,
                archivo_nombre = r.FileName
            }),
            prompts_enrutados = detail.Prompts.Select(p => new
            {
                prompt_id = p.Id,
                nombre = p.Name,
                regla = p.Rule,
                cuerpo = p.Body
            })
        });
    }

    private async Task<McpToolResult> AgentPromptHistoryAsync(JsonElement args, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var promptName = Str(args, "prompt");
        if (promptName is not null)
        {
            var vers = await _agents!.GetPromptVersionsAsync(agent!.Id, promptName, ct);
            return Ok(new
            {
                agente_id = agent.Id,
                prompt = promptName,
                versiones = vers.Select(v => new { indice = v.Index, guardado = v.SavedAt, regla = v.Rule, cuerpo = v.Body })
            });
        }
        var hist = await _agents!.GetPromptHistoryAsync(agent!.Id, ct);
        return Ok(new
        {
            agente_id = agent.Id,
            versiones = hist.Select(v => new
            {
                indice = v.Index,
                guardado = v.SavedAt,
                prompt_base = v.BasePrompt,
                prompts = v.Prompts.Select(p => new { nombre = p.Name, regla = p.Rule, cuerpo = p.Body })
            })
        });
    }

    // ===================== Crear / configurar =====================

    private async Task<McpToolResult> CreateAgentAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' del agente."); }
        var proveedor = ParseProvider(Str(args, "proveedor"));
        if (proveedor is null) { return Err("'proveedor' debe ser " + ProveedoresDoc); }

        var dto = await _agents!.CreateAsync(new CreateAiAgentRequest(
            Name: nombre, Role: Str(args, "rol"), Provider: proveedor.Value, Model: Str(args, "modelo"),
            SystemPrompt: Str(args, "prompt") ?? "",
            DisabledTools: StrArray(args, "herramientas_deshabilitadas"),
            ReactionsEnabled: Bool(args, "reacciones_activas") ?? false,
            ReactionRatioN: Int(args, "reaccion_n") ?? 3, ReactionRatioM: Int(args, "reaccion_m") ?? 4,
            ReactionEmojis: Str(args, "reaccion_emojis")),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo crear el agente."); }
        return Ok(new
        {
            ok = true,
            agente_id = dto.Id,
            nombre = dto.Name,
            proveedor = ProviderEs(dto.Provider),
            encendido = dto.IsActive,
            aviso = "El agente nace apagado; enciendelo con encender_agente cuando lo validez."
        });
    }

    private async Task<McpToolResult> EditAgentAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (detail, error) = await ResolveAgentDetailAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var cur = detail!.Agent;

        var proveedor = cur.Provider;
        if (args.TryGetProperty("proveedor", out _))
        {
            var p = ParseProvider(Str(args, "proveedor"));
            if (p is null) { return Err("'proveedor' debe ser " + ProveedoresDoc); }
            proveedor = p.Value;
        }

        // Herramientas deshabilitadas: si viene la propiedad se reemplaza (lista vacia = ninguna); si no, se conserva.
        var tools = args.TryGetProperty("herramientas_deshabilitadas", out _)
            ? StrArray(args, "herramientas_deshabilitadas") ?? Array.Empty<string>()
            : cur.DisabledTools;

        // Tableros permitidos: por nombre o id; lista vacia = sin restriccion. Si no viene, se conserva.
        IReadOnlyList<Guid>? boards = cur.AllowedBoardIds;
        if (args.TryGetProperty("tableros_permitidos", out _))
        {
            var (ids, berr) = await ResolveBoardIdsAsync(StrArray(args, "tableros_permitidos"), actor, ct);
            if (berr is not null) { return Err(berr); }
            boards = ids;
        }

        // Asignados round-robin: por email o nombre. Si no viene, se conserva.
        IReadOnlyList<Guid>? assignees = cur.AssigneeUserIds;
        if (args.TryGetProperty("asignados_round_robin", out _))
        {
            var (ids, uerr) = await ResolveUserIdsAsync(StrArray(args, "asignados_round_robin"), ct);
            if (uerr is not null) { return Err(uerr); }
            assignees = ids;
        }

        // Concepto por defecto: codigo/nombre, o cadena vacia para quitarlo. Si no viene, se conserva.
        Guid? conceptId = cur.DefaultSubcategoriaId;
        if (args.TryGetProperty("concepto_default", out _))
        {
            var key = Str(args, "concepto_default");
            if (key is null) { conceptId = null; }
            else
            {
                var (cid, cerr) = await ResolveConceptAsync(key, ct);
                if (cerr is not null) { return Err(cerr); }
                conceptId = cid;
            }
        }

        var dto = await _agents!.UpdateAsync(cur.Id, new UpdateAiAgentRequest(
            Name: Str(args, "nombre") ?? cur.Name,
            Role: args.TryGetProperty("rol", out _) ? Str(args, "rol") : cur.Role,
            Provider: proveedor,
            Model: args.TryGetProperty("modelo", out _) ? Str(args, "modelo") : cur.Model,
            SystemPrompt: args.TryGetProperty("prompt", out _) ? (Str(args, "prompt") ?? "") : cur.SystemPrompt,
            DisabledTools: tools,
            ReactionsEnabled: Bool(args, "reacciones_activas") ?? cur.ReactionsEnabled,
            ReactionRatioN: Int(args, "reaccion_n") ?? cur.ReactionRatioN,
            ReactionRatioM: Int(args, "reaccion_m") ?? cur.ReactionRatioM,
            ReactionEmojis: args.TryGetProperty("reaccion_emojis", out _) ? Str(args, "reaccion_emojis") : cur.ReactionEmojis,
            AllowedBoardIds: boards, AssigneeUserIds: assignees, DefaultSubcategoriaId: conceptId),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo editar el agente."); }
        return Ok(new { ok = true, agente_id = dto.Id, nombre = dto.Name, proveedor = ProviderEs(dto.Provider), encendido = dto.IsActive });
    }

    private async Task<McpToolResult> SetAgentMediaReaderAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var dto = await _agents!.SetMediaReaderPromptAsync(agent!.Id, Str(args, "prompt"), actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo fijar el lector de medios."); }
        return Ok(new { ok = true, agente_id = dto.Id, lector_medios = dto.MediaReaderPrompt, generico = dto.MediaReaderPrompt is null });
    }

    private async Task<McpToolResult> SetAgentActiveAsync(JsonElement args, McpActor actor, bool active, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var dto = await _agents!.SetActiveAsync(agent!.Id, active, actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo cambiar el estado del agente."); }
        return Ok(new { ok = true, agente_id = dto.Id, nombre = dto.Name, encendido = dto.IsActive });
    }

    private async Task<McpToolResult> DuplicateAgentAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var dto = await _agents!.DuplicateAsync(agent!.Id, actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo duplicar el agente."); }
        return Ok(new { ok = true, agente_id = dto.Id, nombre = dto.Name, encendido = dto.IsActive });
    }

    private async Task<McpToolResult> RestoreAgentPromptAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var version = Int(args, "version");
        if (version is null or < 0) { return Err("Indica 'version' (indice >= 0; ver historial_prompts_agente)."); }
        var dto = await _agents!.RestorePromptVersionAsync(agent!.Id, version.Value, actor.PlatformUserId, ct);
        if (dto is null) { return Err($"No existe la version {version} en el historial del agente."); }
        return Ok(new { ok = true, agente_id = agent.Id, version = version.Value, prompt = dto.Agent.SystemPrompt });
    }

    // ===================== Recursos =====================

    private async Task<McpToolResult> AddAgentResourceAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' del recurso."); }
        var tipo = ParseResourceType(Str(args, "tipo"));
        if (tipo is null) { return Err("'tipo' debe ser " + TiposRecursoDoc); }
        var dto = await _agents!.AddResourceAsync(new CreateAgentResourceRequest(
            agent!.Id, nombre, tipo.Value, Str(args, "detalle"), Str(args, "archivo_url"), Str(args, "archivo_nombre")),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo agregar el recurso."); }
        return Ok(new { ok = true, recurso_id = dto.Id, nombre = dto.Name, tipo = ResourceTypeEs(dto.ResourceType) });
    }

    private async Task<McpToolResult> EditAgentResourceAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var id = Guid2(args, "recurso_id");
        if (id is null) { return Err("Indica el 'recurso_id' (ver ver_agente)."); }
        // UpdateResourceAsync reemplaza todos los campos: se parte del estado actual y se fusiona lo que llegue.
        var cur = await _db.AiAgentResources.AsNoTracking()
            .Where(r => r.Id == id.Value)
            .Select(r => new { r.Name, r.ResourceType, r.Detail, r.FileUrl, r.FileName })
            .FirstOrDefaultAsync(ct);
        if (cur is null) { return Err("No encontre ese recurso."); }
        var tipo = cur.ResourceType;
        if (args.TryGetProperty("tipo", out _))
        {
            var parsed = ParseResourceType(Str(args, "tipo"));
            if (parsed is null) { return Err("'tipo' debe ser " + TiposRecursoDoc); }
            tipo = parsed.Value;
        }
        var dto = await _agents!.UpdateResourceAsync(id.Value, new UpdateAgentResourceRequest(
            Str(args, "nombre") ?? cur.Name, tipo,
            args.TryGetProperty("detalle", out _) ? Str(args, "detalle") : cur.Detail,
            args.TryGetProperty("archivo_url", out _) ? Str(args, "archivo_url") : cur.FileUrl,
            args.TryGetProperty("archivo_nombre", out _) ? Str(args, "archivo_nombre") : cur.FileName),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No encontre ese recurso."); }
        return Ok(new { ok = true, recurso_id = dto.Id, nombre = dto.Name, tipo = ResourceTypeEs(dto.ResourceType) });
    }

    private async Task<McpToolResult> DeleteAgentResourceAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var id = Guid2(args, "recurso_id");
        if (id is null) { return Err("Indica el 'recurso_id' (ver ver_agente)."); }
        var done = await _agents!.DeleteResourceAsync(id.Value, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, recurso_id = id.Value, borrado = true }) : Err("No encontre ese recurso.");
    }

    // ===================== Prompts enrutados =====================

    private async Task<McpToolResult> AddAgentPromptAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var nombre = Str(args, "nombre");
        if (nombre is null) { return Err("Indica el 'nombre' del prompt."); }
        var dto = await _agents!.AddPromptAsync(new CreateAgentPromptRequest(
            agent!.Id, nombre, Str(args, "regla"), Str(args, "cuerpo") ?? ""), actor.PlatformUserId, ct);
        if (dto is null) { return Err("No se pudo agregar el prompt."); }
        return Ok(new { ok = true, prompt_id = dto.Id, nombre = dto.Name });
    }

    private async Task<McpToolResult> EditAgentPromptAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var id = Guid2(args, "prompt_id");
        if (id is null) { return Err("Indica el 'prompt_id' (ver ver_agente)."); }
        // UpdatePromptAsync reemplaza todos los campos: se parte del estado actual y se fusiona lo que llegue.
        var cur = await _db.AiAgentPrompts.AsNoTracking()
            .Where(p => p.Id == id.Value)
            .Select(p => new { p.Name, p.Rule, p.Body })
            .FirstOrDefaultAsync(ct);
        if (cur is null) { return Err("No encontre ese prompt."); }
        var dto = await _agents!.UpdatePromptAsync(id.Value, new UpdateAgentPromptRequest(
            Str(args, "nombre") ?? cur.Name,
            args.TryGetProperty("regla", out _) ? Str(args, "regla") : cur.Rule,
            args.TryGetProperty("cuerpo", out _) ? (Str(args, "cuerpo") ?? "") : cur.Body),
            actor.PlatformUserId, ct);
        if (dto is null) { return Err("No encontre ese prompt."); }
        return Ok(new { ok = true, prompt_id = dto.Id, nombre = dto.Name });
    }

    private async Task<McpToolResult> DeleteAgentPromptAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var id = Guid2(args, "prompt_id");
        if (id is null) { return Err("Indica el 'prompt_id' (ver ver_agente)."); }
        var done = await _agents!.DeletePromptAsync(id.Value, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, prompt_id = id.Value, borrado = true }) : Err("No encontre ese prompt.");
    }

    // ===================== Borrado del agente =====================

    private async Task<McpToolResult> DeleteAgentAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(Str(args, "agente"), ct);
        if (error is not null) { return Err(error); }
        var done = await _agents!.DeleteAsync(agent!.Id, actor.PlatformUserId, ct);
        return done ? Ok(new { ok = true, agente_id = agent.Id, borrado = true }) : Err("No se pudo borrar el agente.");
    }

    // ===================== Resolucion =====================

    private async Task<(AiAgentDto? agent, string? error)> ResolveAgentAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) { return (null, "Indica el agente (id o nombre)."); }
        var list = await _agents!.ListAsync(ct);
        if (Guid.TryParse(key, out var g))
        {
            var byId = list.FirstOrDefault(a => a.Id == g);
            return byId is null ? (null, $"No encontre un agente con id '{key}'.") : (byId, null);
        }
        var exact = list.Where(a => string.Equals(a.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
        var pick = exact.Count > 0 ? exact : list.Where(a => a.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pick.Count == 0) { return (null, $"No encontre ningun agente que coincida con '{key}'."); }
        if (pick.Count > 1) { return (null, $"Hay varios agentes que coinciden con '{key}': {string.Join(", ", pick.Take(5).Select(a => a.Name))}. Usa el id."); }
        return (pick[0], null);
    }

    private async Task<(AiAgentDetailDto? detail, string? error)> ResolveAgentDetailAsync(string? key, CancellationToken ct)
    {
        var (agent, error) = await ResolveAgentAsync(key, ct);
        if (error is not null) { return (null, error); }
        var detail = await _agents!.GetAsync(agent!.Id, ct);
        return detail is null ? (null, $"No pude abrir el agente '{agent.Name}'.") : (detail, null);
    }

    private async Task<(IReadOnlyList<Guid> ids, string? error)> ResolveBoardIdsAsync(IReadOnlyList<string>? keys, McpActor actor, CancellationToken ct)
    {
        var result = new List<Guid>();
        if (keys is null) { return (result, null); }
        foreach (var k in keys)
        {
            var board = await ResolveBoardAsync(k, actor, ct);
            if (board is null) { return (result, $"No encontre el tablero '{k}' (o sin acceso). Usa listar_tableros."); }
            result.Add(board.Id);
        }
        return (result, null);
    }

    private async Task<(IReadOnlyList<Guid> ids, string? error)> ResolveUserIdsAsync(IReadOnlyList<string>? keys, CancellationToken ct)
    {
        var result = new List<Guid>();
        if (keys is null) { return (result, null); }
        foreach (var k in keys)
        {
            var uid = await ResolveUserAsync(k, ct);
            if (uid is null) { return (result, $"No encontre el usuario '{k}'. Usa listar_usuarios."); }
            result.Add(uid.Value);
        }
        return (result, null);
    }

    private async Task<(Guid? id, string? error)> ResolveConceptAsync(string key, CancellationToken ct)
    {
        var low = key.ToLowerInvariant();
        var hit = await _db.ActividadSubcategorias.AsNoTracking()
            .Where(s => !s.IsArchived && (s.Codigo.ToLower() == low || s.Nombre.ToLower() == low))
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        return hit is null ? (null, $"No encontre el concepto '{key}'. Usa listar_conceptos.") : (hit, null);
    }

    // ===================== Parsers / etiquetas =====================

    private static string[]? StrArray(JsonElement args, string name)
    {
        if (!args.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) { return null; }
        var list = new List<string>();
        foreach (var e in arr.EnumerateArray())
        {
            var s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
            if (!string.IsNullOrWhiteSpace(s)) { list.Add(s.Trim()); }
        }
        return list.ToArray();
    }

    private static AiProvider? ParseProvider(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return AiProvider.Claude; }
        return s.Trim().ToLowerInvariant() switch
        {
            "claude" or "anthropic" => AiProvider.Claude,
            "gemini" or "google" => AiProvider.Gemini,
            "chatgpt" or "gpt" or "openai" => AiProvider.ChatGpt,
            "deepseek" => AiProvider.DeepSeek,
            _ => null
        };
    }

    private static string ProviderEs(AiProvider p) => p switch
    {
        AiProvider.Claude => "claude",
        AiProvider.Gemini => "gemini",
        AiProvider.ChatGpt => "chatgpt",
        AiProvider.DeepSeek => "deepseek",
        _ => p.ToString().ToLowerInvariant()
    };

    private static AgentResourceType? ParseResourceType(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return null; }
        return s.Trim().ToLowerInvariant() switch
        {
            "imagen" or "image" => AgentResourceType.Image,
            "video" => AgentResourceType.Video,
            "audio" => AgentResourceType.Audio,
            "pdf" => AgentResourceType.Pdf,
            "ubicacion" or "location" => AgentResourceType.Location,
            "texto" or "text" => AgentResourceType.Text,
            _ => null
        };
    }

    private static string ResourceTypeEs(AgentResourceType t) => t switch
    {
        AgentResourceType.Image => "imagen",
        AgentResourceType.Video => "video",
        AgentResourceType.Audio => "audio",
        AgentResourceType.Pdf => "pdf",
        AgentResourceType.Location => "ubicacion",
        AgentResourceType.Text => "texto",
        _ => t.ToString().ToLowerInvariant()
    };
}
