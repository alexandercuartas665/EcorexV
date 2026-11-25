using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Mcp;

/// <summary>
/// AGENTE DE IA DEL NODO en el MCP (ADR-0211, ola 3): la IA asigna a un paso del flujo un agente de IA que lo atiende
/// (ADR-0090) y le configura sus recursos, una herramienta por propiedad: instrucciones del paso, correo, busqueda web
/// (Colmena, ADR-0091), WhatsApp con plantilla de frio (ADR-0092), llamadas de voz con reintento y ventana (ADR-0190),
/// canales permitidos, que hacer si falla, cadencia de seguimiento con ventana horaria, tope de preguntas y nota de
/// traspaso por salida. Capa delgada sobre <see cref="IWorkflowDesignService"/>. Los recursos se guardan como PATCH:
/// se parte del estado actual del agente del nodo y solo cambia lo pedido (SetNodeAgentResourcesAsync guarda TODO).
/// Permiso: el del modulo "flujos" (igual que el panel del nodo en el editor).
/// </summary>
public sealed partial class TableroMcpToolset
{
    private static readonly HashSet<string> FlowAgentReadOnly = new(StringComparer.Ordinal)
    {
        "opciones_agente_nodo", "ver_agente_nodo"
    };

    private static readonly HashSet<string> FlowAgentDestructive = new(StringComparer.Ordinal)
    {
        "quitar_agente_nodo"
    };

    private static readonly HashSet<string> FlowAgentToolNames = new(StringComparer.Ordinal)
    {
        "opciones_agente_nodo", "ver_agente_nodo", "agente_nodo", "quitar_agente_nodo", "instrucciones_agente_nodo",
        "correo_agente_nodo", "web_agente_nodo", "whatsapp_agente_nodo", "voz_agente_nodo", "canales_agente_nodo",
        "fallo_agente_nodo", "seguimiento_agente_nodo", "tope_preguntas_agente_nodo", "traspaso_salida_agente_nodo"
    };

    private static readonly AiToolSpec[] FlowAgentSpecs =
    {
        new("opciones_agente_nodo",
            "Lo que se puede usar al configurar el agente de un paso: agentes de IA del tenant (activos primero), clientes " +
            "Colmena (busqueda web), lineas de WhatsApp y lineas de voz. Usala antes de agente_nodo y de los recursos.",
            Obj("")),
        new("ver_agente_nodo",
            "El agente de IA de un paso con toda su configuracion (autonomia, instrucciones, recursos, canales, fallo, " +
            "seguimiento, tope de preguntas, traspasos). Sin agente = el paso lo atiende solo gente.",
            Obj(NodoIdProp, "nodo_id")),
        new("agente_nodo",
            "Pone (o reemplaza) el AGENTE DE IA que atiende un paso. 'agente' = id o nombre (opciones_agente_nodo). " +
            "'autonomia': autonomo = el agente cierra el paso solo; propone (defecto) = deja todo listo y una persona " +
            "confirma. En una compuerta, primero decision_humana_nodo. El agente debe estar ENCENDIDO para actuar.",
            Obj(NodoIdProp + ",\"agente\":{\"type\":\"string\"},\"autonomia\":{\"type\":\"string\",\"enum\":[\"autonomo\",\"propone\"]}", "nodo_id", "agente")),
        new("quitar_agente_nodo",
            "Quita el agente del paso: vuelve a ser 100% humano (se pierde su configuracion de recursos). Confirma con el usuario.",
            Obj(NodoIdProp, "nodo_id")),
        new("instrucciones_agente_nodo",
            "Instrucciones del agente SOLO para este paso (se suman a su prompt general): que debe lograr, que datos " +
            "buscar, como hablarle al cliente. Sin 'instrucciones' las quita.",
            Obj(NodoIdProp + ",\"instrucciones\":{\"type\":\"string\"}", "nodo_id")),
        new("correo_agente_nodo",
            "activo=true permite al agente enviar CORREOS en este paso.",
            Obj(NodoIdProp + ",\"activo\":{\"type\":\"boolean\"}", "nodo_id", "activo")),
        new("web_agente_nodo",
            "Busqueda web con un navegador de la Colmena: 'cliente_colmena' (id o nombre, ver opciones_agente_nodo) y " +
            "'perfil' opcional (sesion guardada del navegador). Sin 'cliente_colmena' la quita.",
            Obj(NodoIdProp + ",\"cliente_colmena\":{\"type\":\"string\"},\"perfil\":{\"type\":\"string\"}", "nodo_id")),
        new("whatsapp_agente_nodo",
            "El agente pregunta por WhatsApp desde 'linea' (id o nombre). Para escribir en FRIO (sin conversacion " +
            "abierta) usa 'plantilla' HSM aprobada + 'idioma', 'variables' (variable -> texto con tokens) y " +
            "'encabezado_url' si la plantilla lleva archivo. 'minutos_frio' = desde cuantos minutos sin respuesta usa la " +
            "plantilla. Sin 'linea' quita WhatsApp.",
            Obj(NodoIdProp + ",\"linea\":{\"type\":\"string\"},\"plantilla\":{\"type\":\"string\"},\"idioma\":{\"type\":\"string\"}," +
                "\"variables\":{\"type\":\"object\",\"additionalProperties\":{\"type\":\"string\"}},\"encabezado_url\":{\"type\":\"string\"}," +
                "\"minutos_frio\":{\"type\":\"integer\",\"minimum\":1}", "nodo_id")),
        new("voz_agente_nodo",
            "Llamadas de VOZ: 'agente_voz' (agente de voz, id o nombre) y 'linea_voz' (id o nombre; omitida = la por " +
            "defecto). Reintento: 'max_llamadas' (1-10), 'reintento_minutos' (1-1440) y ventana 'hora_inicio'-'hora_fin' " +
            "(0-24, hora de la empresa). Sin 'agente_voz' quita la voz.",
            Obj(NodoIdProp + ",\"agente_voz\":{\"type\":\"string\"},\"linea_voz\":{\"type\":\"string\"}," +
                "\"max_llamadas\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10},\"reintento_minutos\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1440}," +
                "\"hora_inicio\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":23},\"hora_fin\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":24}", "nodo_id")),
        new("canales_agente_nodo",
            "Canales que el agente puede usar en este paso: voz, whatsapp, web, correo. Lista vacia = todos los configurados.",
            Obj(NodoIdProp + ",\"canales\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"voz\",\"whatsapp\",\"web\",\"correo\"]}}", "nodo_id", "canales")),
        new("fallo_agente_nodo",
            "Que hacer si el agente NO resuelve el paso: devolver_a_persona (defecto), reintentar ('reintentos' 0-10), " +
            "tomar_ruta ('ruta' = NOMBRE de la opcion/salida a tomar) o notificar.",
            Obj(NodoIdProp + ",\"accion\":{\"type\":\"string\",\"enum\":[\"devolver_a_persona\",\"reintentar\",\"tomar_ruta\",\"notificar\"]}," +
                "\"reintentos\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":10},\"ruta\":{\"type\":\"string\"}", "nodo_id", "accion")),
        new("seguimiento_agente_nodo",
            "CADENCIA de seguimiento: lista de esperas entre recordatorios del agente (la 1a es el primer contacto; 0 = " +
            "inmediato). Al agotarse deja de insistir. 'ventana' opcional (horario de envio: 'inicio'/'fin' HH:MM, 'dias' " +
            "1=lunes..7=domingo, 'festivos' true = no escribe en festivos). Lista vacia quita la cadencia.",
            Obj(NodoIdProp + ",\"esperas\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"dias\":{\"type\":\"integer\",\"minimum\":0}," +
                "\"horas\":{\"type\":\"integer\",\"minimum\":0},\"minutos\":{\"type\":\"integer\",\"minimum\":0}},\"additionalProperties\":false}}," +
                "\"ventana\":{\"type\":\"object\",\"properties\":{\"inicio\":{\"type\":\"string\"},\"fin\":{\"type\":\"string\"}," +
                "\"dias\":{\"type\":\"array\",\"items\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":7}},\"festivos\":{\"type\":\"boolean\"}},\"additionalProperties\":false}",
                "nodo_id", "esperas")),
        new("tope_preguntas_agente_nodo",
            "Maximo de preguntas/recordatorios por WhatsApp por actividad antes de pasarla a una persona. Sin 'tope' = el " +
            "valor por defecto del sistema.",
            Obj(NodoIdProp + ",\"tope\":{\"type\":\"integer\",\"minimum\":1}", "nodo_id")),
        new("traspaso_salida_agente_nodo",
            "En una COMPUERTA con agente: nota que el flujo deja al agente conversacional cuando la tarea sale por esta " +
            "salida ('salida_nodo_id' = nodo destino), para que sepa como seguir la conversacion. Sin 'nota' la quita.",
            Obj(NodoIdProp + ",\"salida_nodo_id\":{\"type\":\"string\"},\"nota\":{\"type\":\"string\"}", "nodo_id", "salida_nodo_id")),
    };

    private async Task<McpToolResult> ExecuteFlowAgentAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_flowDesign is null) { return Err("La autoria de flujos no esta disponible."); }
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = FlowAgentReadOnly.Contains(tool)
            ? NeedFlow(perms, PermissionAction.View, "consultar flujos")
            : NeedFlow(perms, PermissionAction.Edit, "configurar flujos");
        if (denied is not null) { return Err(denied); }

        return tool switch
        {
            "opciones_agente_nodo" => await AgentOptionsAsync(ct),
            "ver_agente_nodo" => await ViewNodeAgentAsync(args, ct),
            "agente_nodo" => await SetNodeAgentAsync(args, ct),
            "quitar_agente_nodo" => await RemoveNodeAgentAsync(args, ct),
            "instrucciones_agente_nodo" => await PatchAgentResourcesAsync(args, ct, (r, a) => Task.FromResult<(FlowNodeAgentResourcesInput, string?)>((r with { ExtraPrompt = Str(a, "instrucciones") }, null))),
            "correo_agente_nodo" => await PatchAgentResourcesAsync(args, ct, (r, a) => Task.FromResult<(FlowNodeAgentResourcesInput, string?)>(
                Bool(a, "activo") is bool on ? (r with { CanSendEmail = on }, null) : (r, "'activo' es obligatorio."))),
            "web_agente_nodo" => await PatchAgentResourcesAsync(args, ct, WebPatchAsync),
            "whatsapp_agente_nodo" => await PatchAgentResourcesAsync(args, ct, WhatsAppPatchAsync),
            "voz_agente_nodo" => await PatchAgentResourcesAsync(args, ct, VoicePatchAsync),
            "canales_agente_nodo" => await PatchAgentResourcesAsync(args, ct, (r, a) => Task.FromResult(ChannelsPatch(r, a))),
            "fallo_agente_nodo" => await PatchAgentResourcesAsync(args, ct, (r, a) => Task.FromResult(FailurePatch(r, a))),
            "seguimiento_agente_nodo" => await SetAgentFollowUpAsync(args, ct),
            "tope_preguntas_agente_nodo" => await SetAgentMaxAsksAsync(args, ct),
            "traspaso_salida_agente_nodo" => await SetAgentRouteHandoffAsync(args, ct),
            _ => Err($"Herramienta desconocida: {tool}")
        };
    }

    // ===================== Lectura =====================

    private async Task<McpToolResult> AgentOptionsAsync(CancellationToken ct)
    {
        var agents = await _flowDesign!.ListAgentCatalogAsync(ct);
        var colmena = await _flowDesign.ListColmenaClientsAsync(ct);
        var wa = await _flowDesign.ListWhatsAppLinesAsync(ct);
        var voice = await _flowDesign.ListVoiceLinesAsync(ct);
        return Ok(new
        {
            agentes = agents.Select(a => new { agente_id = a.AiAgentId, nombre = a.Name, rol = a.Role, encendido = a.IsActive }),
            clientes_colmena = colmena.Select(c => new { id = c.Id, nombre = c.Name, activo = c.IsActive }),
            lineas_whatsapp = wa.Select(l => new { id = l.Id, nombre = l.Name, conectada = l.Connected }),
            lineas_voz = voice.Select(l => new { id = l.Id, nombre = l.Name, habilitada = l.Enabled, por_defecto = l.IsDefault }),
            nota = agents.Count == 0 ? "No hay agentes de IA: crealos con crear_agente (guia general, receta R09)." : null
        });
    }

    private async Task<McpToolResult> ViewNodeAgentAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var a = await _flowDesign!.GetNodeAgentAsync(node.Id, ct);
        if (a is null) { return Ok(new { nodo_id = node.Id, agente = (string?)null, nota = "El paso lo atiende solo gente (agente_nodo para asignar uno)." }); }
        return Ok(await NodeAgentPayloadAsync(canvas!, node, a, ct));
    }

    private async Task<object> NodeAgentPayloadAsync(FlowCanvasDto canvas, FlowCanvasNodeDto node, FlowNodeAgentDto a, CancellationToken ct)
    {
        var colmena = a.ColmenaClientId is Guid cc ? (await _flowDesign!.ListColmenaClientsAsync(ct)).FirstOrDefault(x => x.Id == cc)?.Name ?? cc.ToString() : null;
        var waLine = a.WhatsAppLineId is Guid wl ? (await _flowDesign!.ListWhatsAppLinesAsync(ct)).FirstOrDefault(x => x.Id == wl)?.Name ?? wl.ToString() : null;
        var voiceLine = a.VoiceLineId is Guid vl ? (await _flowDesign!.ListVoiceLinesAsync(ct)).FirstOrDefault(x => x.Id == vl)?.Name ?? vl.ToString() : null;
        var voiceAgent = a.VoiceAiAgentId is Guid va ? (await _flowDesign!.ListAgentCatalogAsync(ct)).FirstOrDefault(x => x.AiAgentId == va)?.Name ?? va.ToString() : null;
        var rows = StepSlaList.ReadRows(node.AgentFollowUpJson);
        var win = PulseSendWindow.Read(node.AgentFollowUpJson);
        return new
        {
            nodo_id = node.Id,
            agente = a.AgentName,
            agente_id = a.AiAgentId,
            agente_encendido = a.IsActive,
            autonomia = a.Autonomy == WorkflowAgentAutonomy.Autonomous ? "autonomo" : "propone",
            instrucciones = a.ExtraPrompt,
            correo = a.CanSendEmail,
            web = colmena is null ? null : new { cliente_colmena = colmena, perfil = a.ColmenaSessionKey },
            whatsapp = waLine is null ? null : new
            {
                linea = waLine,
                plantilla = a.WhatsAppTemplateName,
                idioma = a.WhatsAppTemplateLang,
                variables = ParseObject(a.WhatsAppTemplateVarsJson),
                encabezado_url = a.WhatsAppTemplateHeaderMediaUrl,
                minutos_frio = a.ColdWindowMinutes
            },
            voz = voiceAgent is null ? null : new
            {
                agente_voz = voiceAgent,
                linea_voz = voiceLine ?? "por defecto",
                max_llamadas = a.MaxVoiceCalls,
                reintento_minutos = a.VoiceRetryMinutes,
                ventana = a.CallWindowStartHour is int s && a.CallWindowEndHour is int e ? $"{s}:00-{e}:00" : null
            },
            canales = a.AllowedChannels is null || a.AllowedChannels == WorkflowAgentChannel.None ? "todos" : (object)ChannelsEs(a.AllowedChannels.Value),
            si_falla = FailureEs(a.OnFailure),
            reintentos = a.OnFailure == WorkflowAgentFailureAction.Retry ? a.FailureRetries : (int?)null,
            ruta_si_falla = a.FailureRoute,
            seguimiento = rows.Count == 0 ? null : new
            {
                esperas = rows.Select(r => DurationEs(new StepSla(r.Days, r.Hours, r.Minutes, StepSlaDayMode.Calendar))),
                ventana = win.Enabled
                    ? new { inicio = $"{win.StartMin / 60:00}:{win.StartMin % 60:00}", fin = $"{win.EndMin / 60:00}:{win.EndMin % 60:00}", dias = win.Days.OrderBy(d => d), festivos = win.RespectHolidays }
                    : null
            },
            tope_preguntas = node.AgentMaxAsks,
            traspasos = ParseHandoff(node.AgentRouteHandoffJson, canvas),
            aviso = a.IsActive ? null : "El agente esta APAGADO: no atendera el paso hasta encenderlo (encender_agente)."
        };
    }

    // ===================== Asignar / quitar =====================

    private async Task<McpToolResult> SetNodeAgentAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        if (node.NodeType is WorkflowNodeType.StartEvent or WorkflowNodeType.EndEvent)
        {
            return Err("Un agente atiende pasos de tarea o compuertas; no el inicio ni el fin.");
        }
        if (node.NodeType == WorkflowNodeType.ExclusiveGateway && !node.AllowsAssignment)
        {
            return Err("Una compuerta solo admite agente si la decide alguien: primero decision_humana_nodo (activo=true) " +
                       "en el borrador; luego el agente decide en lugar de la persona.");
        }
        var key = Str(args, "agente");
        if (key is null) { return Err("'agente' es obligatorio (id o nombre; ver opciones_agente_nodo)."); }
        var catalog = await _flowDesign!.ListAgentCatalogAsync(ct);
        var agent = Guid.TryParse(key, out var g) ? catalog.FirstOrDefault(x => x.AiAgentId == g)
            : catalog.FirstOrDefault(x => Same(x.Name, key))
              ?? SingleOrNull(catalog.Where(x => x.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        if (agent is null) { return Err($"No encontre el agente '{key}'. Agentes: {string.Join(" | ", catalog.Select(x => x.Name))}."); }
        var autonomy = Str(args, "autonomia") == "autonomo" ? WorkflowAgentAutonomy.Autonomous : WorkflowAgentAutonomy.Proposes;
        var res = await _flowDesign.SetNodeAgentAsync(node.Id, agent.AiAgentId, autonomy, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo asignar el agente."); }
        return Ok(new
        {
            ok = true,
            nodo_id = node.Id,
            agente = agent.Name,
            autonomia = autonomy == WorkflowAgentAutonomy.Autonomous ? "autonomo" : "propone",
            aviso = agent.IsActive ? null : "El agente esta APAGADO: no atendera el paso hasta encenderlo (encender_agente).",
            siguiente = "Configura sus recursos: instrucciones_agente_nodo, whatsapp_agente_nodo, voz_agente_nodo, seguimiento_agente_nodo..."
        });
    }

    private async Task<McpToolResult> RemoveNodeAgentAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var res = await _flowDesign!.RemoveNodeAgentAsync(node.Id, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, nota = "El paso vuelve a ser atendido solo por personas." })
            : Err(res.Error ?? "No se pudo quitar el agente.");
    }

    // ===================== Recursos (PATCH) =====================

    private async Task<McpToolResult> PatchAgentResourcesAsync(JsonElement args, CancellationToken ct,
        Func<FlowNodeAgentResourcesInput, JsonElement, Task<(FlowNodeAgentResourcesInput Input, string? Error)>> patch)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var current = await _flowDesign!.GetNodeAgentAsync(node.Id, ct);
        if (current is null) { return Err("El paso no tiene agente: asignalo primero con agente_nodo."); }
        var (input, perr) = await patch(ResourcesOf(current), args);
        if (perr is not null) { return Err(perr); }
        var res = await _flowDesign.SetNodeAgentResourcesAsync(node.Id, input, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo guardar la configuracion del agente."); }
        return Ok(new { ok = true, agente_del_paso = await NodeAgentPayloadAsync(canvas!, node, res.Value, ct) });
    }

    private static FlowNodeAgentResourcesInput ResourcesOf(FlowNodeAgentDto a) => new(
        a.ColmenaClientId, a.ColmenaSessionKey, a.VoiceAiAgentId, a.WhatsAppLineId, a.WhatsAppTemplateName, a.WhatsAppTemplateLang,
        a.ExtraPrompt, a.CanSendEmail, a.OnFailure, a.FailureRetries, a.FailureRoute, a.ColdWindowMinutes,
        a.WhatsAppTemplateVarsJson, a.WhatsAppTemplateHeaderMediaUrl, a.VoiceLineId, a.AllowedChannels,
        a.MaxVoiceCalls, a.VoiceRetryMinutes, a.CallWindowStartHour, a.CallWindowEndHour);

    private async Task<(FlowNodeAgentResourcesInput, string?)> WebPatchAsync(FlowNodeAgentResourcesInput r, JsonElement a)
    {
        var key = Str(a, "cliente_colmena");
        if (key is null) { return (r with { ColmenaClientId = null, ColmenaSessionKey = null }, null); }
        var list = await _flowDesign!.ListColmenaClientsAsync();
        var hit = Guid.TryParse(key, out var g) ? list.FirstOrDefault(x => x.Id == g)
            : list.FirstOrDefault(x => Same(x.Name, key)) ?? SingleOrNull(list.Where(x => x.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        return hit is null
            ? (r, $"No encontre el cliente Colmena '{key}'. Clientes: {string.Join(" | ", list.Select(x => x.Name))}.")
            : (r with { ColmenaClientId = hit.Id, ColmenaSessionKey = Str(a, "perfil") }, null);
    }

    private async Task<(FlowNodeAgentResourcesInput, string?)> WhatsAppPatchAsync(FlowNodeAgentResourcesInput r, JsonElement a)
    {
        var key = Str(a, "linea");
        if (key is null)
        {
            return (r with { WhatsAppLineId = null, WhatsAppTemplateName = null, WhatsAppTemplateLang = null, WhatsAppTemplateVarsJson = null, WhatsAppTemplateHeaderMediaUrl = null, ColdWindowMinutes = null }, null);
        }
        var list = await _flowDesign!.ListWhatsAppLinesAsync();
        var hit = Guid.TryParse(key, out var g) ? list.FirstOrDefault(x => x.Id == g)
            : list.FirstOrDefault(x => Same(x.Name, key)) ?? SingleOrNull(list.Where(x => x.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        if (hit is null) { return (r, $"No encontre la linea de WhatsApp '{key}'. Lineas: {string.Join(" | ", list.Select(x => x.Name))}."); }
        string? vars = null;
        if (a.TryGetProperty("variables", out var v) && v.ValueKind == JsonValueKind.Object) { vars = v.GetRawText(); }
        return (r with
        {
            WhatsAppLineId = hit.Id,
            WhatsAppTemplateName = Str(a, "plantilla"),
            WhatsAppTemplateLang = Str(a, "idioma") ?? (Str(a, "plantilla") is null ? null : "es"),
            WhatsAppTemplateVarsJson = vars,
            WhatsAppTemplateHeaderMediaUrl = Str(a, "encabezado_url"),
            ColdWindowMinutes = Int(a, "minutos_frio")
        }, null);
    }

    private async Task<(FlowNodeAgentResourcesInput, string?)> VoicePatchAsync(FlowNodeAgentResourcesInput r, JsonElement a)
    {
        var key = Str(a, "agente_voz");
        if (key is null) { return (r with { VoiceAiAgentId = null, VoiceLineId = null }, null); }
        var agents = await _flowDesign!.ListAgentCatalogAsync();
        var agent = Guid.TryParse(key, out var g) ? agents.FirstOrDefault(x => x.AiAgentId == g)
            : agents.FirstOrDefault(x => Same(x.Name, key)) ?? SingleOrNull(agents.Where(x => x.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        if (agent is null) { return (r, $"No encontre el agente de voz '{key}'."); }
        Guid? line = null;
        if (Str(a, "linea_voz") is { } lk)
        {
            var lines = await _flowDesign.ListVoiceLinesAsync();
            var hit = Guid.TryParse(lk, out var lg) ? lines.FirstOrDefault(x => x.Id == lg)
                : lines.FirstOrDefault(x => Same(x.Name, lk)) ?? SingleOrNull(lines.Where(x => x.Name.Contains(lk, StringComparison.OrdinalIgnoreCase)).ToList());
            if (hit is null) { return (r, $"No encontre la linea de voz '{lk}'. Lineas: {string.Join(" | ", lines.Select(x => x.Name))}."); }
            line = hit.Id;
        }
        int? start = Int(a, "hora_inicio"), end = Int(a, "hora_fin");
        if (start is not null && end is not null && end <= start) { return (r, "'hora_fin' debe ser mayor que 'hora_inicio'."); }
        return (r with
        {
            VoiceAiAgentId = agent.AiAgentId,
            VoiceLineId = line,
            MaxVoiceCalls = Int(a, "max_llamadas"),
            VoiceRetryMinutes = Int(a, "reintento_minutos"),
            CallWindowStartHour = start,
            CallWindowEndHour = end
        }, null);
    }

    private static (FlowNodeAgentResourcesInput, string?) ChannelsPatch(FlowNodeAgentResourcesInput r, JsonElement a)
    {
        var flags = WorkflowAgentChannel.None;
        if (a.TryGetProperty("canales", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in arr.EnumerateArray())
            {
                WorkflowAgentChannel? ch = c.GetString() switch
                {
                    "voz" => WorkflowAgentChannel.Voz,
                    "whatsapp" => WorkflowAgentChannel.WhatsApp,
                    "web" => WorkflowAgentChannel.Web,
                    "correo" => WorkflowAgentChannel.Correo,
                    _ => null
                };
                if (ch is null) { return (r, $"Canal '{c}' no valido: voz, whatsapp, web o correo."); }
                flags |= ch.Value;
            }
        }
        return (r with { AllowedChannels = flags == WorkflowAgentChannel.None ? null : flags }, null);
    }

    private static (FlowNodeAgentResourcesInput, string?) FailurePatch(FlowNodeAgentResourcesInput r, JsonElement a)
    {
        WorkflowAgentFailureAction? action = Str(a, "accion") switch
        {
            "devolver_a_persona" => WorkflowAgentFailureAction.ReturnToHuman,
            "reintentar" => WorkflowAgentFailureAction.Retry,
            "tomar_ruta" => WorkflowAgentFailureAction.TakeRoute,
            "notificar" => WorkflowAgentFailureAction.Notify,
            _ => null
        };
        if (action is null) { return (r, "'accion' debe ser devolver_a_persona, reintentar, tomar_ruta o notificar."); }
        var route = Str(a, "ruta");
        if (action == WorkflowAgentFailureAction.TakeRoute && route is null) { return (r, "Con tomar_ruta indica 'ruta' (nombre de la opcion/salida)."); }
        return (r with { OnFailure = action.Value, FailureRetries = Int(a, "reintentos") ?? 0, FailureRoute = route }, null);
    }

    // ===================== Metadatos del nodo =====================

    private async Task<McpToolResult> SetAgentFollowUpAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var rows = new List<(int, int, int)>();
        if (args.TryGetProperty("esperas", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
            {
                int d = Int(e, "dias") ?? 0, h = Int(e, "horas") ?? 0, m = Int(e, "minutos") ?? 0;
                if (d < 0 || h < 0 || m < 0) { return Err("Las esperas no pueden ser negativas."); }
                rows.Add((d, h, m));
            }
        }
        PulseSendWindow? window = null;
        if (args.TryGetProperty("ventana", out var w) && w.ValueKind == JsonValueKind.Object)
        {
            var start = ParseHm(Str(w, "inicio"));
            var end = ParseHm(Str(w, "fin"));
            if (start is null || end is null || end <= start) { return Err("La ventana necesita 'inicio' y 'fin' en HH:MM, con fin despues de inicio."); }
            var days = new HashSet<int>();
            if (w.TryGetProperty("dias", out var da) && da.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in da.EnumerateArray()) { if (x.TryGetInt32(out var n) && n is >= 1 and <= 7) { days.Add(n); } }
            }
            if (days.Count == 0) { for (var i = 1; i <= 7; i++) { days.Add(i); } }
            window = new PulseSendWindow(true, start.Value, end.Value, days, Bool(w, "festivos") == true);
        }
        var json = StepSlaList.Build(rows, window);
        var res = await _flowDesign!.SetNodeAgentFollowUpAsync(node.Id, json, ct);
        return res.IsOk
            ? Ok(new { ok = true, nodo_id = node.Id, recordatorios = rows.Count, ventana = window is null ? null : $"{Str(w, "inicio")}-{Str(w, "fin")}", nota = rows.Count == 0 ? "Sin cadencia de seguimiento." : null })
            : Err(res.Error ?? "No se pudo guardar la cadencia.");
    }

    private static int? ParseHm(string? s)
    {
        if (s is null) { return null; }
        var parts = s.Trim().Split(':');
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out var h)) { return null; }
        var m = parts.Length == 2 && int.TryParse(parts[1], out var mm) ? mm : 0;
        if (h is < 0 or > 24 || m is < 0 or > 59 || (h == 24 && m > 0)) { return null; }
        return (h * 60) + m;
    }

    private async Task<McpToolResult> SetAgentMaxAsksAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var max = Int(args, "tope");
        var res = await _flowDesign!.SetNodeAgentMaxAsksAsync(node.Id, max, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, tope_preguntas = max, nota = max is null ? "Usa el valor por defecto del sistema." : null })
            : Err(res.Error ?? "No se pudo guardar el tope.");
    }

    private async Task<McpToolResult> SetAgentRouteHandoffAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        if (node.NodeType != WorkflowNodeType.ExclusiveGateway) { return Err("El traspaso por salida aplica a compuertas."); }
        var target = Guid2(args, "salida_nodo_id");
        var outs = canvas!.Edges.Where(e => e.SourceNodeId == node.Id).Select(e => e.TargetNodeId).ToHashSet();
        if (target is null || !outs.Contains(target.Value))
        {
            return Err($"'salida_nodo_id' debe ser el destino de una salida de esta compuerta: {string.Join(" | ", outs.Select(o => $"{NodeName(canvas, o)} ({o})"))}.");
        }
        var key = canvas.Nodes.First(n => n.Id == target).BpmnElementId;
        JsonObject map;
        try { map = (string.IsNullOrWhiteSpace(node.AgentRouteHandoffJson) ? null : JsonNode.Parse(node.AgentRouteHandoffJson) as JsonObject) ?? new JsonObject(); }
        catch (JsonException) { map = new JsonObject(); }
        var note = Str(args, "nota");
        if (note is null) { map.Remove(key); } else { map[key] = note; }
        var res = await _flowDesign!.SetNodeAgentRouteHandoffAsync(node.Id, map.Count == 0 ? null : map.ToJsonString(), ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, salida = NodeName(canvas, target.Value), nota = note, traspasos = map.Count })
            : Err(res.Error ?? "No se pudo guardar el traspaso.");
    }

    // ===================== Helpers =====================

    private static object? ParseHandoff(string? json, FlowCanvasDto canvas)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o || o.Count == 0) { return null; }
            return o.Select(kv => new
            {
                salida = canvas.Nodes.FirstOrDefault(n => n.BpmnElementId == kv.Key) is { } n ? (n.Name ?? kv.Key) : kv.Key,
                nota = kv.Value?.ToString()
            }).ToList();
        }
        catch (JsonException) { return null; }
    }

    private static string FailureEs(WorkflowAgentFailureAction f) => f switch
    {
        WorkflowAgentFailureAction.Retry => "reintentar",
        WorkflowAgentFailureAction.TakeRoute => "tomar_ruta",
        WorkflowAgentFailureAction.Notify => "notificar",
        _ => "devolver_a_persona"
    };

    private static List<string> ChannelsEs(WorkflowAgentChannel c)
    {
        var list = new List<string>();
        if (c.HasFlag(WorkflowAgentChannel.Voz)) { list.Add("voz"); }
        if (c.HasFlag(WorkflowAgentChannel.WhatsApp)) { list.Add("whatsapp"); }
        if (c.HasFlag(WorkflowAgentChannel.Web)) { list.Add("web"); }
        if (c.HasFlag(WorkflowAgentChannel.Correo)) { list.Add("correo"); }
        return list;
    }
}
