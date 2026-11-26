using System.Text.Json;
using Ecorex.Application.Notifications;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// CONFIGURACION POR NODO de los flujos en el MCP (ADR-0211, ola 1). Complementa a TableroMcpToolset.Flujos
/// (que solo arma el grafo): aqui la IA deja el flujo FUNCIONANDO con lo que la pantalla configura en el panel
/// derecho de cada nodo. UNA HERRAMIENTA POR PROPIEDAD (decision del usuario) y capa delgada sobre los metodos
/// ya existentes de <see cref="IWorkflowDesignService"/>: decision humana, reinicio, tablero/columna, concepto de
/// cierre, color, nota, salto a otro flujo, plazo, tiempo de arranque, notificaciones (con enlaces publicos de
/// decision), formularios por salida / creacion al llegar / quitar, reglas del nodo y ciclo de vida del flujo
/// (editar, pausar, reanudar, desenganchar). Mismo permiso del modulo "flujos" que la pantalla. Los metodos que
/// fijan DOS valores a la vez (SetNodeConfig, SetNodeAppearance) se llaman como PATCH: el valor no enviado se lee
/// del nodo actual para no borrarlo.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private static readonly HashSet<string> FlowConfigReadOnly = new(StringComparer.Ordinal)
    {
        "listar_reglas_disponibles"
    };

    private static readonly HashSet<string> FlowConfigDestructive = new(StringComparer.Ordinal)
    {
        "quitar_notificacion_nodo", "quitar_formulario_nodo", "quitar_regla_nodo", "desenganchar_flujo_de_concepto"
    };

    private static readonly HashSet<string> FlowConfigToolNames = new(StringComparer.Ordinal)
    {
        "nombre_salida", "decision_humana_nodo", "reinicio_nodo", "tablero_destino_nodo", "concepto_cierre_nodo", "color_nodo",
        "nota_nodo", "salto_a_flujo_nodo", "plazo_nodo", "tiempo_arranque_nodo", "agregar_notificacion_nodo",
        "quitar_notificacion_nodo", "quitar_formulario_nodo", "formulario_obligatorio_por_salida",
        "formulario_crear_al_llegar", "listar_reglas_disponibles", "agregar_regla_nodo", "quitar_regla_nodo",
        "regla_autonoma_nodo", "editar_flujo", "pausar_flujo", "reanudar_flujo", "desenganchar_flujo_de_concepto"
    };

    private const string NodoIdProp = "\"nodo_id\":{\"type\":\"string\",\"description\":\"Id del nodo (ver ver_flujo)\"}";

    private const string DuracionProps =
        "\"dias\":{\"type\":\"integer\",\"minimum\":0},\"horas\":{\"type\":\"integer\",\"minimum\":0}," +
        "\"minutos\":{\"type\":\"integer\",\"minimum\":0},\"dias_habiles\":{\"type\":\"boolean\",\"description\":" +
        "\"true = los dias saltan fines de semana y festivos del tenant; false (defecto) = dias calendario\"}";

    private static bool HasArg(JsonElement a, string name)
        => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out _);

    private static readonly AiToolSpec[] FlowConfigSpecs =
    {
        new("nombre_salida",
            "Pone (o quita) el NOMBRE de una salida (arista). En una compuerta AUTOMATICA los nombres de sus salidas son las " +
            "OPCIONES que elige quien cierra el paso anterior (ej. Aprobada / Rechazada) y cada salida lleva la condicion " +
            "approval == '<nombre>' (condicion_ruta); en el diagrama es la etiqueta de la flecha. 'arista_id' de ver_flujo. " +
            "Solo en borrador.",
            """{"type":"object","properties":{"arista_id":{"type":"string"},"nombre":{"type":"string"}},"required":["arista_id"],"additionalProperties":false}"""),
        new("decision_humana_nodo",
            "En una COMPUERTA o un FIN: activo=true hace que una PERSONA (el encargado del nodo) elija la ruta o confirme " +
            "el cierre; activo=false (defecto del motor) = la compuerta se resuelve SOLA por las condiciones de sus " +
            "salidas. En una TAREA equivale a 'permite asignacion manual del paso'. Solo en borrador (nueva_version_flujo). " +
            "Despues define quien decide con asignar_encargado_nodo / asignar_cargo_nodo.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"activo\":{\"type\":\"boolean\"}},\"required\":[\"nodo_id\",\"activo\"],\"additionalProperties\":false}"),
        new("reinicio_nodo",
            "Al pasar por este nodo el flujo ABRE UN CICLO NUEVO en otro nodo (p.ej. 'Corregir' vuelve a 'Revision'). " +
            "'reiniciar_en_nodo_id' = nodo destino del mismo flujo; omitelo para quitar el reinicio. Solo en borrador.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"reiniciar_en_nodo_id\":{\"type\":\"string\"}},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("tablero_destino_nodo",
            "Al LLEGAR a este paso la actividad salta a ese tablero y columna. 'tablero' = id, codigo o nombre; 'columna' = " +
            "id o nombre (omitida = primera columna). Sin 'tablero' quita el destino. Si la columna es de cierre, fija " +
            "despues el concepto con concepto_cierre_nodo. Editable aun publicado.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"tablero\":{\"type\":\"string\"},\"columna\":{\"type\":\"string\"}},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("concepto_cierre_nodo",
            "Concepto (motivo) de cierre que el flujo estampa al llevar la actividad a la columna de CIERRE del nodo. Debe " +
            "ser uno de los conceptos de cierre del tablero destino (si no, la respuesta lista los validos). Sin 'concepto' " +
            "lo quita. Requiere tablero_destino_nodo con una columna de cierre.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"concepto\":{\"type\":\"string\"}},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("color_nodo",
            "Color del nodo en el diagrama (solo visual). Sin 'color' lo quita. Editable aun publicado.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"color\":{\"type\":\"string\",\"enum\":[\"violet\",\"blue\",\"green\",\"amber\",\"rose\",\"slate\"]}},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("nota_nodo",
            "Nota adhesiva (post-it) junto al nodo en el diagrama, para explicar el paso (max 1000). Sin 'nota' la quita.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"nota\":{\"type\":\"string\"}},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("salto_a_flujo_nodo",
            "Al llegar a este nodo se arranca OTRO flujo (crea una tarea hija con ese proceso). 'flujo_destino' = id, codigo " +
            "o nombre de otro flujo publicado; omitelo para quitar el salto.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"flujo_destino\":{\"type\":\"string\"}},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("plazo_nodo",
            "PLAZO del paso (tiempo para entregarlo): con el se calcula su vencimiento y los avisos de atraso. Todo en 0 " +
            "o sin valores = sin plazo. Editable aun publicado.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + "," + DuracionProps + "},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("tiempo_arranque_nodo",
            "TIEMPO PARA ARRANCAR el paso: espera antes de que el paso quede activo (p.ej. 'llamar 2 dias despues'). Todo " +
            "en 0 = inmediato. Mismo formato que plazo_nodo.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + "," + DuracionProps + "},\"required\":[\"nodo_id\"],\"additionalProperties\":false}"),
        new("agregar_notificacion_nodo",
            "AGREGA una regla de aviso que se dispara al LLEGAR el paso. 'canal': correo | whatsapp (plantilla aprobada; " +
            "linea_id + plantilla) | whatsapp_grupo (linea_grupo_id + grupo_jid) | telegram (chat_id). 'destino' (correo y " +
            "whatsapp): encargado_paso (defecto), usuario (+ 'usuario' = correo o id) o contacto_tarea (el cliente). " +
            "'mensaje'/'asunto' admiten tokens {tarea.x}, {form.x}, {sistema.fecha}. 'enlaces_decision' (uno por salida de " +
            "una compuerta) envia al cliente un enlace publico /d/... que, al abrirlo, ELIGE esa salida; cada uno llena la " +
            "variable indicada del mensaje/plantilla. Devuelve el numero de la regla (para quitarla).",
            """{"type":"object","properties":{"nodo_id":{"type":"string"},"canal":{"type":"string","enum":["correo","whatsapp","whatsapp_grupo","telegram"]},"destino":{"type":"string","enum":["encargado_paso","usuario","contacto_tarea"]},"usuario":{"type":"string","description":"Correo o id del usuario (destino=usuario)"},"asunto":{"type":"string"},"mensaje":{"type":"string"},"incluir_enlace":{"type":"boolean","description":"Agrega el enlace a la tarea al final (no en whatsapp plantilla)"},"linea_id":{"type":"string","description":"Linea WhatsApp (plantillas)"},"plantilla":{"type":"string","description":"Nombre de la plantilla HSM aprobada"},"idioma":{"type":"string","description":"Idioma de la plantilla, ej. es"},"variables":{"type":"object","description":"Variable de la plantilla -> texto con tokens","additionalProperties":{"type":"string"}},"linea_grupo_id":{"type":"string"},"grupo_jid":{"type":"string","description":"...@g.us"},"chat_id":{"type":"string"},"adjuntar_pdf_formulario":{"type":"string","description":"Formulario (id, codigo o titulo) cuyo PDF se adjunta"},"nota_agente":{"type":"string","description":"Nota interna para el agente conversacional (no se envia al cliente)"},"enlaces_decision":{"type":"array","items":{"type":"object","properties":{"variable":{"type":"string","description":"Variable del mensaje/plantilla que recibe la URL"},"salida_nodo_id":{"type":"string","description":"Nodo destino de la salida que elige este enlace"},"captura":{"type":"string","enum":["ninguna","firma","observacion"]},"observacion_obligatoria":{"type":"boolean"},"boton":{"type":"string","description":"Texto del boton en la pagina"},"vence_horas":{"type":"integer","minimum":1},"etiqueta":{"type":"string","description":"Etiqueta de tarea a aplicar si el cliente elige esta salida"}},"required":["variable","salida_nodo_id"],"additionalProperties":false}}},"required":["nodo_id","canal"],"additionalProperties":false}"""),
        new("quitar_notificacion_nodo",
            "Quita UNA regla de aviso del nodo por su numero (1, 2... como las lista ver_flujo). Confirma con el usuario.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"numero\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"nodo_id\",\"numero\"],\"additionalProperties\":false}"),
        new("quitar_formulario_nodo",
            "Desvincula un formulario del nodo (no borra el formulario ni sus respuestas). Confirma con el usuario.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"formulario\":{\"type\":\"string\"}},\"required\":[\"nodo_id\",\"formulario\"],\"additionalProperties\":false}"),
        new("formulario_obligatorio_por_salida",
            "En una COMPUERTA: el formulario del nodo es obligatorio SOLO si se elige alguna de estas salidas ('salidas' = " +
            "ids de los nodos destino). Lista vacia = no obligatorio en ninguna. El formulario debe estar ya en el nodo " +
            "(poner_formulario_a_nodo).",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"formulario\":{\"type\":\"string\"},\"salidas\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"nodo_id\",\"formulario\",\"salidas\"],\"additionalProperties\":false}"),
        new("formulario_crear_al_llegar",
            "activo=true (defecto) = el formulario del nodo se crea solo al llegar el paso; false = queda para agregarlo a mano.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"formulario\":{\"type\":\"string\"},\"activo\":{\"type\":\"boolean\"}},\"required\":[\"nodo_id\",\"formulario\",\"activo\"],\"additionalProperties\":false}"),
        new("listar_reglas_disponibles",
            "Catalogo de reglas del tenant (nombre, verbo, estado, documento) que se pueden poner en un nodo con agregar_regla_nodo.",
            """{"type":"object","properties":{"buscar":{"type":"string"}},"additionalProperties":false}"""),
        new("agregar_regla_nodo",
            "Vincula una regla existente al nodo (se ejecuta en ese paso). 'regla' = id o nombre (listar_reglas_disponibles). " +
            "'autonoma' true = actua sola sin que la persona la dispare.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"regla\":{\"type\":\"string\"},\"autonoma\":{\"type\":\"boolean\"}},\"required\":[\"nodo_id\",\"regla\"],\"additionalProperties\":false}"),
        new("quitar_regla_nodo",
            "Desvincula una regla del nodo (la regla sigue existiendo). Confirma con el usuario.",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"regla\":{\"type\":\"string\"}},\"required\":[\"nodo_id\",\"regla\"],\"additionalProperties\":false}"),
        new("regla_autonoma_nodo",
            "Marca si una regla del nodo actua sola (true) o la dispara la persona (false).",
            "{\"type\":\"object\",\"properties\":{" + NodoIdProp + ",\"regla\":{\"type\":\"string\"},\"autonoma\":{\"type\":\"boolean\"}},\"required\":[\"nodo_id\",\"regla\",\"autonoma\"],\"additionalProperties\":false}"),
        new("editar_flujo",
            "Cambia nombre, categoria o descripcion del flujo (solo lo que envies).",
            """{"type":"object","properties":{"flujo":{"type":"string"},"nombre":{"type":"string"},"categoria":{"type":"string"},"descripcion":{"type":"string"}},"required":["flujo"],"additionalProperties":false}"""),
        new("pausar_flujo",
            "Pausa un flujo publicado: no arranca en tareas NUEVAS (las que estan en marcha siguen). Se revierte con reanudar_flujo.",
            """{"type":"object","properties":{"flujo":{"type":"string"}},"required":["flujo"],"additionalProperties":false}"""),
        new("reanudar_flujo",
            "Reanuda un flujo pausado.",
            """{"type":"object","properties":{"flujo":{"type":"string"}},"required":["flujo"],"additionalProperties":false}"""),
        new("desenganchar_flujo_de_concepto",
            "Quita el flujo de un concepto: las tareas NUEVAS de ese concepto ya no lo arrancan (las que estan en marcha " +
            "siguen). 'concepto' = codigo o nombre. Confirma con el usuario.",
            """{"type":"object","properties":{"concepto":{"type":"string"}},"required":["concepto"],"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> ExecuteFlowConfigAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_flowDesign is null) { return Err("La autoria de flujos no esta disponible."); }
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = tool == "listar_reglas_disponibles"
            ? NeedFlow(perms, PermissionAction.View, "consultar reglas de flujos")
            : NeedFlow(perms, PermissionAction.Edit, "configurar flujos");
        if (denied is not null) { return Err(denied); }

        return tool switch
        {
            "nombre_salida" => await SetEdgeNameAsync(args, ct),
            "decision_humana_nodo" => await SetHumanDecisionAsync(args, ct),
            "reinicio_nodo" => await SetRestartAsync(args, ct),
            "tablero_destino_nodo" => await SetBoardTargetAsync(args, ct),
            "concepto_cierre_nodo" => await SetCloseReasonAsync(args, ct),
            "color_nodo" => await SetNodeColorAsync(args, ct),
            "nota_nodo" => await SetNodeNoteAsync(args, ct),
            "salto_a_flujo_nodo" => await SetJumpAsync(args, ct),
            "plazo_nodo" => await SetDurationAsync(args, isSla: true, ct),
            "tiempo_arranque_nodo" => await SetDurationAsync(args, isSla: false, ct),
            "agregar_notificacion_nodo" => await AddNotifyAsync(args, ct),
            "quitar_notificacion_nodo" => await RemoveNotifyAsync(args, ct),
            "quitar_formulario_nodo" => await RemoveNodeFormAsync(args, ct),
            "formulario_obligatorio_por_salida" => await SetFormRequiredRoutesAsync(args, ct),
            "formulario_crear_al_llegar" => await SetFormAutoCreateAsync(args, ct),
            "listar_reglas_disponibles" => await ListRuleCatalogAsync(args, ct),
            "agregar_regla_nodo" => await AddNodeRuleAsync(args, ct),
            "quitar_regla_nodo" => await RemoveNodeRuleAsync(args, ct),
            "regla_autonoma_nodo" => await SetNodeRuleAutonomousAsync(args, ct),
            "editar_flujo" => await EditFlowPropsAsync(args, ct),
            "pausar_flujo" => await PauseResumeFlowAsync(args, pause: true, ct),
            "reanudar_flujo" => await PauseResumeFlowAsync(args, pause: false, ct),
            "desenganchar_flujo_de_concepto" => await UnlinkFlowFromConceptAsync(args, ct),
            _ => Err($"Herramienta desconocida: {tool}")
        };
    }

    // ===================== Salidas =====================

    private async Task<McpToolResult> SetEdgeNameAsync(JsonElement args, CancellationToken ct)
    {
        var edgeId = Guid2(args, "arista_id");
        if (edgeId is null) { return Err("'arista_id' no es valido (ver ver_flujo)."); }
        var edge = await _db.WorkflowEdges.AsNoTracking().Where(e => e.Id == edgeId)
            .Select(e => new { e.DefinitionId, e.SourceNodeId, e.ConditionExpression }).FirstOrDefaultAsync(ct);
        if (edge is null) { return Err("Salida no encontrada (ver ver_flujo)."); }
        var name = Str(args, "nombre");
        var res = await _flowDesign!.SetEdgeNameAsync(edgeId.Value, name, ct);
        if (!res.IsOk)
        {
            var canvas = await _flowDesign.GetCanvasAsync(edge.DefinitionId, ct);
            return Err(canvas is null ? (res.Error ?? "No se pudo nombrar la salida.") : DraftHint(res.Error, canvas));
        }
        var source = await _db.WorkflowNodes.AsNoTracking().Where(n => n.Id == edge.SourceNodeId)
            .Select(n => new { n.NodeType, n.AllowsAssignment }).FirstOrDefaultAsync(ct);
        var autoGateway = source is { NodeType: WorkflowNodeType.ExclusiveGateway, AllowsAssignment: false };
        return Ok(new
        {
            ok = true,
            arista_id = edgeId,
            nombre = name,
            sugerencia = autoGateway && name is not null && string.IsNullOrWhiteSpace(edge.ConditionExpression)
                ? $"Compuerta automatica: para que esta opcion lleve aqui pon condicion_ruta con approval == '{name}' (deja UNA salida sin condicion como ruta por defecto)."
                : null
        });
    }

    // ===================== Nodo: config basica (PATCH sobre SetNodeConfig/SetNodeAppearance) =====================

    private async Task<McpToolResult> SetHumanDecisionAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var on = Bool(args, "activo");
        if (on is null) { return Err("'activo' es obligatorio (true/false)."); }
        if (node.NodeType == WorkflowNodeType.StartEvent) { return Err("El inicio no admite decision humana: se completa solo."); }
        var res = await _flowDesign!.SetNodeConfigAsync(node.Id, on.Value, node.RestartNodeId, ct);
        if (!res.IsOk) { return Err(DraftHint(res.Error, canvas!)); }
        return Ok(new
        {
            ok = true,
            nodo_id = node.Id,
            decision_humana = on.Value,
            efecto = node.NodeType switch
            {
                WorkflowNodeType.ExclusiveGateway => on.Value
                    ? "Una persona elegira la ruta. Define quien con asignar_encargado_nodo / asignar_cargo_nodo."
                    : "La compuerta se resolvera sola: cada salida necesita su condicion (condicion_ruta).",
                WorkflowNodeType.EndEvent => on.Value
                    ? "Una persona confirmara el cierre del flujo."
                    : "El fin se completa solo.",
                _ => on.Value ? "El paso permite asignacion manual." : "El paso no permite asignacion manual."
            }
        });
    }

    private async Task<McpToolResult> SetRestartAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        Guid? target = null;
        if (Str(args, "reiniciar_en_nodo_id") is not null)
        {
            target = Guid2(args, "reiniciar_en_nodo_id");
            if (target is null || canvas!.Nodes.All(n => n.Id != target)) { return Err("'reiniciar_en_nodo_id' no es un nodo de este flujo (ver ver_flujo)."); }
        }
        var res = await _flowDesign!.SetNodeConfigAsync(node.Id, node.AllowsAssignment, target, ct);
        if (!res.IsOk) { return Err(DraftHint(res.Error, canvas!)); }
        return Ok(new { ok = true, nodo_id = node.Id, reinicia_en = target is null ? null : NodeName(canvas!, target.Value) });
    }

    private async Task<McpToolResult> SetNodeColorAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var color = Str(args, "color");
        var res = await _flowDesign!.SetNodeAppearanceAsync(node.Id, color, node.Note, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, color }) : Err(res.Error ?? "No se pudo fijar el color.");
    }

    private async Task<McpToolResult> SetNodeNoteAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var note = Str(args, "nota");
        var res = await _flowDesign!.SetNodeAppearanceAsync(node.Id, node.Color, note, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, nota = note }) : Err(res.Error ?? "No se pudo fijar la nota.");
    }

    // ===================== Nodo: tablero, cierre, salto, tiempos =====================

    private async Task<McpToolResult> SetBoardTargetAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var boardKey = Str(args, "tablero");
        if (boardKey is null)
        {
            var clear = await _flowDesign!.SetNodeBoardTargetAsync(node.Id, null, null, ct);
            return clear.IsOk ? Ok(new { ok = true, nodo_id = node.Id, tablero = (string?)null, aviso = "El paso ya no mueve la actividad de tablero." })
                : Err(clear.Error ?? "No se pudo quitar el tablero destino.");
        }

        var boards = await _db.TaskBoards.AsNoTracking().Where(b => !b.IsArchived)
            .Select(b => new { b.Id, b.Code, b.Name }).ToListAsync(ct);
        var board = Guid.TryParse(boardKey, out var bg) ? boards.FirstOrDefault(b => b.Id == bg)
            : boards.FirstOrDefault(b => Same(b.Code, boardKey)) ?? boards.FirstOrDefault(b => Same(b.Name, boardKey))
              ?? SingleOrNull(boards.Where(b => b.Name.Contains(boardKey, StringComparison.OrdinalIgnoreCase)).ToList());
        if (board is null) { return Err($"No encontre el tablero '{boardKey}'. Usa listar_tableros."); }

        var columns = await _db.TaskBoardColumns.AsNoTracking().Where(c => c.BoardId == board.Id)
            .OrderBy(c => c.SortOrder).Select(c => new { c.Id, c.Name, c.IsDone }).ToListAsync(ct);
        Guid? columnId = null;
        var colKey = Str(args, "columna");
        var colName = columns.FirstOrDefault()?.Name;
        var isDone = columns.FirstOrDefault()?.IsDone ?? false;
        if (colKey is not null)
        {
            var col = Guid.TryParse(colKey, out var cg) ? columns.FirstOrDefault(c => c.Id == cg)
                : columns.FirstOrDefault(c => Same(c.Name, colKey))
                  ?? SingleOrNull(columns.Where(c => c.Name.Contains(colKey, StringComparison.OrdinalIgnoreCase)).ToList());
            if (col is null)
            {
                return Err($"El tablero '{board.Name}' no tiene la columna '{colKey}'. Columnas: {string.Join(" | ", columns.Select(c => c.Name + (c.IsDone ? " (cierre)" : "")))}.");
            }
            columnId = col.Id;
            colName = col.Name;
            isDone = col.IsDone;
        }

        var res = await _flowDesign!.SetNodeBoardTargetAsync(node.Id, board.Id, columnId, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo fijar el tablero destino."); }
        return Ok(new
        {
            ok = true,
            nodo_id = node.Id,
            tablero = board.Name,
            columna = colName,
            columna_de_cierre = isDone,
            aviso = isDone ? "Es una columna de CIERRE: fija el concepto con concepto_cierre_nodo." : null
        });
    }

    private async Task<McpToolResult> SetCloseReasonAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var reason = Str(args, "concepto");
        var res = await _flowDesign!.SetNodeCloseReasonAsync(node.Id, reason, ct);
        if (res.IsOk) { return Ok(new { ok = true, nodo_id = node.Id, concepto_cierre = reason }); }

        var hint = string.Empty;
        if (node.TargetBoardId is Guid bid)
        {
            var json = await _db.TaskBoards.AsNoTracking().Where(b => b.Id == bid).Select(b => b.CloseReasonsJson).FirstOrDefaultAsync(ct);
            var list = ParseStringList(json);
            if (list.Count > 0) { hint = " Conceptos validos del tablero: " + string.Join(" | ", list) + "."; }
        }
        return Err((res.Error ?? "No se pudo fijar el concepto de cierre.") + hint);
    }

    private async Task<McpToolResult> SetJumpAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        Guid? target = null;
        string? targetName = null;
        if (Str(args, "flujo_destino") is { } key)
        {
            var (defId, ferr) = await ResolveFlowDefAsync(key, ct);
            if (defId is null) { return Err(ferr!); }
            target = defId;
            targetName = await _db.WorkflowDefinitions.AsNoTracking().Where(d => d.Id == defId).Select(d => d.Name).FirstOrDefaultAsync(ct);
        }
        var res = await _flowDesign!.SetNodeJumpAsync(node.Id, target, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, salta_a = targetName })
            : Err(res.Error ?? "No se pudo fijar el salto.");
    }

    private async Task<McpToolResult> SetDurationAsync(JsonElement args, bool isSla, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        int d = Int(args, "dias") ?? 0, h = Int(args, "horas") ?? 0, m = Int(args, "minutos") ?? 0;
        if (d < 0 || h < 0 || m < 0) { return Err("dias, horas y minutos no pueden ser negativos."); }
        var mode = Bool(args, "dias_habiles") == true ? StepSlaDayMode.Business : StepSlaDayMode.Calendar;
        var json = StepSla.Build(d, h, m, mode);
        var res = isSla
            ? await _flowDesign!.SetNodeSlaAsync(node.Id, json, ct)
            : await _flowDesign!.SetNodeStartDelayAsync(node.Id, json, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo guardar."); }
        return Ok(new
        {
            ok = true,
            nodo_id = node.Id,
            que = isSla ? "plazo" : "tiempo_arranque",
            valor = json is null ? (isSla ? "sin plazo" : "inmediato") : DurationEs(StepSla.Read(json))
        });
    }

    // ===================== Notificaciones =====================

    private async Task<McpToolResult> AddNotifyAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }

        var current = NodeNotifyConfig.Parse(node.NotifyJson);
        if (current.IsEmpty && !string.IsNullOrWhiteSpace(node.NotifyJson) && node.NotifyJson.Trim() is not ("{}" or "null"))
        {
            return Err("No pude leer los avisos actuales del nodo; revisalos en la pantalla antes de agregar uno por MCP.");
        }

        var canal = (Str(args, "canal") ?? string.Empty).ToLowerInvariant() switch
        {
            "correo" or "email" => NotifyChannel.Correo,
            "whatsapp" => NotifyChannel.WhatsApp,
            "whatsapp_grupo" or "grupo" => NotifyChannel.WhatsAppGrupo,
            "telegram" => NotifyChannel.Telegram,
            _ => (NotifyChannel?)null
        };
        if (canal is null) { return Err("'canal' debe ser correo, whatsapp, whatsapp_grupo o telegram."); }

        var destino = (Str(args, "destino") ?? "encargado_paso").ToLowerInvariant() switch
        {
            "encargado_paso" or "encargado" => NodeNotifyRecipient.StepAssignee,
            "usuario" => NodeNotifyRecipient.Usuario,
            "contacto_tarea" or "contacto" or "cliente" => NodeNotifyRecipient.ContactoTarea,
            _ => (NodeNotifyRecipient?)null
        };
        if (destino is null) { return Err("'destino' debe ser encargado_paso, usuario o contacto_tarea."); }

        Guid? userId = null;
        if (destino == NodeNotifyRecipient.Usuario && canal is NotifyChannel.Correo or NotifyChannel.WhatsApp)
        {
            var uKey = Str(args, "usuario");
            if (uKey is null) { return Err("Con destino=usuario indica 'usuario' (correo o id)."); }
            var low = uKey.ToLowerInvariant();
            userId = Guid.TryParse(uKey, out var ug)
                ? await _db.TenantUsers.AsNoTracking().Where(u => u.Id == ug).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct)
                : await _db.TenantUsers.AsNoTracking().Where(u => u.Email.ToLower() == low).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
            if (userId is null) { return Err($"No encontre el usuario '{uKey}' en esta empresa."); }
        }

        var mensaje = Str(args, "mensaje");
        Guid? lineId = Guid2(args, "linea_id"), groupLineId = Guid2(args, "linea_grupo_id");
        string? plantilla = Str(args, "plantilla"), grupoJid = Str(args, "grupo_jid"), chatId = Str(args, "chat_id");
        switch (canal)
        {
            case NotifyChannel.WhatsApp when lineId is null || plantilla is null:
                return Err("WhatsApp necesita 'linea_id' y 'plantilla' (plantilla HSM aprobada).");
            case NotifyChannel.WhatsAppGrupo when groupLineId is null || grupoJid is null:
                return Err("WhatsApp grupo necesita 'linea_grupo_id' y 'grupo_jid' (...@g.us).");
            case NotifyChannel.Telegram when chatId is null:
                return Err("Telegram necesita 'chat_id'.");
            case NotifyChannel.Correo or NotifyChannel.WhatsAppGrupo or NotifyChannel.Telegram when mensaje is null:
                return Err("Indica 'mensaje' (admite tokens {tarea.x}, {form.x}).");
        }

        Guid? pdfForm = null;
        if (Str(args, "adjuntar_pdf_formulario") is { } fKey)
        {
            var (fid, ferr) = await ResolveFormDefAsync(fKey, ct);
            if (fid is null) { return Err(ferr!); }
            pdfForm = fid;
        }

        Dictionary<string, string>? vars = null;
        if (args.TryGetProperty("variables", out var vEl) && vEl.ValueKind == JsonValueKind.Object)
        {
            vars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in vEl.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String) { vars[p.Name] = p.Value.GetString()!; }
            }
            if (vars.Count == 0) { vars = null; }
        }

        List<NotifyDecisionLink>? links = null;
        if (args.TryGetProperty("enlaces_decision", out var lEl) && lEl.ValueKind == JsonValueKind.Array)
        {
            links = new List<NotifyDecisionLink>();
            var i = 0;
            foreach (var l in lEl.EnumerateArray())
            {
                i++;
                var variable = Str(l, "variable");
                var target = Guid2(l, "salida_nodo_id");
                if (variable is null || target is null) { return Err($"Enlace {i}: 'variable' y 'salida_nodo_id' son obligatorios."); }
                if (canvas!.Nodes.All(n => n.Id != target)) { return Err($"Enlace {i}: 'salida_nodo_id' no es un nodo de este flujo."); }
                if (!canvas.Edges.Any(e => e.TargetNodeId == target && canvas.Nodes.Any(n => n.Id == e.SourceNodeId && n.NodeType == WorkflowNodeType.ExclusiveGateway)))
                {
                    return Err($"Enlace {i}: '{NodeName(canvas, target.Value)}' no es la salida de ninguna compuerta.");
                }
                var capture = (Str(l, "captura") ?? "ninguna").ToLowerInvariant() switch
                {
                    "firma" => WorkflowDecisionCapture.Signature,
                    "observacion" => WorkflowDecisionCapture.Observation,
                    _ => WorkflowDecisionCapture.None
                };
                Guid? tagId = null;
                if (Str(l, "etiqueta") is { } tKey)
                {
                    var tlow = tKey.ToLowerInvariant();
                    tagId = Guid.TryParse(tKey, out var tg)
                        ? await _db.TaskItemTags.AsNoTracking().Where(t => t.Id == tg).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct)
                        : await _db.TaskItemTags.AsNoTracking().Where(t => t.Name.ToLower() == tlow).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
                    if (tagId is null) { return Err($"Enlace {i}: no existe la etiqueta '{tKey}'."); }
                }
                links.Add(new NotifyDecisionLink(variable, target.Value, capture, Bool(l, "observacion_obligatoria") ?? false,
                    Str(l, "boton"), Int(l, "vence_horas"), ApplyTagId: tagId));
            }
            if (links.Count == 0) { links = null; }
        }

        var rule = new NodeNotifyRule(
            Canal: canal.Value,
            Destino: destino.Value,
            UsuarioId: userId,
            LineaId: canal == NotifyChannel.WhatsApp ? lineId : null,
            Plantilla: canal == NotifyChannel.WhatsApp ? plantilla : null,
            Idioma: canal == NotifyChannel.WhatsApp ? (Str(args, "idioma") ?? "es") : null,
            Asunto: canal == NotifyChannel.Correo ? Str(args, "asunto") : null,
            LineaGrupoId: canal == NotifyChannel.WhatsAppGrupo ? groupLineId : null,
            GrupoJid: canal == NotifyChannel.WhatsAppGrupo ? grupoJid : null,
            ChatId: canal == NotifyChannel.Telegram ? chatId : null,
            Mensaje: mensaje,
            IncluirEnlace: Bool(args, "incluir_enlace") ?? false,
            Variables: vars,
            AdjuntarPdfFormDefId: pdfForm,
            EnlacesDecision: links,
            NotaAgente: Str(args, "nota_agente"));

        var list = (current.Reglas ?? Array.Empty<NodeNotifyRule>()).ToList();
        list.Add(rule);
        var res = await _flowDesign!.SetNodeNotifyAsync(node.Id, new NodeNotifyConfig(list).Serialize(), ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo guardar el aviso."); }
        return Ok(new
        {
            ok = true,
            nodo_id = node.Id,
            numero = list.Count,
            total_avisos = list.Count,
            resumen = NotifySummary(rule, canvas!),
            aviso = links is not null && canal == NotifyChannel.WhatsApp
                ? "Cada 'variable' de enlace debe existir en la plantilla HSM; si no, el cliente no vera el enlace."
                : null
        });
    }

    private async Task<McpToolResult> RemoveNotifyAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var n = Int(args, "numero");
        var list = (NodeNotifyConfig.Parse(node.NotifyJson).Reglas ?? Array.Empty<NodeNotifyRule>()).ToList();
        if (n is null || n < 1 || n > list.Count) { return Err($"'numero' debe estar entre 1 y {list.Count} (ver ver_flujo)."); }
        var removed = list[n.Value - 1];
        list.RemoveAt(n.Value - 1);
        var res = await _flowDesign!.SetNodeNotifyAsync(node.Id, list.Count == 0 ? null : new NodeNotifyConfig(list).Serialize(), ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, quitado = NotifySummary(removed, canvas!), quedan = list.Count })
            : Err(res.Error ?? "No se pudo quitar el aviso.");
    }

    // ===================== Formularios del nodo =====================

    private async Task<McpToolResult> RemoveNodeFormAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var (formId, ferr) = await NodeFormAsync(node, Str(args, "formulario"), ct);
        if (formId is null) { return Err(ferr!); }
        var res = await _flowDesign!.RemoveNodeFormAsync(node.Id, formId.Value, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, formulario_id = formId }) : Err(res.Error ?? "No se pudo quitar el formulario.");
    }

    private async Task<McpToolResult> SetFormRequiredRoutesAsync(JsonElement args, CancellationToken ct)
    {
        var (canvas, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        if (node.NodeType != WorkflowNodeType.ExclusiveGateway)
        {
            return Err("Obligatorio por salida solo aplica a compuertas. En una tarea usa poner_formulario_a_nodo con obligatorio=true.");
        }
        var (formId, ferr) = await NodeFormAsync(node, Str(args, "formulario"), ct);
        if (formId is null) { return Err(ferr!); }

        var outs = canvas!.Edges.Where(e => e.SourceNodeId == node.Id).Select(e => e.TargetNodeId).ToHashSet();
        var keys = new List<string>();
        var names = new List<string>();
        if (args.TryGetProperty("salidas", out var sEl) && sEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in sEl.EnumerateArray())
            {
                if (!Guid.TryParse(s.GetString(), out var tid) || !outs.Contains(tid))
                {
                    return Err($"'{s}' no es una salida de esta compuerta. Salidas: {string.Join(" | ", outs.Select(o => $"{NodeName(canvas, o)} ({o})"))}.");
                }
                var target = canvas.Nodes.First(n => n.Id == tid);
                keys.Add(target.BpmnElementId);
                names.Add(NodeName(canvas, tid));
            }
        }
        var res = await _flowDesign!.SetNodeFormRequiredRoutesAsync(node.Id, formId.Value, keys, ct);
        return res.IsOk
            ? Ok(new { ok = true, nodo_id = node.Id, formulario_id = formId, obligatorio_en = names.Count > 0 ? names : null, nota = names.Count == 0 ? "No es obligatorio en ninguna salida." : null })
            : Err(res.Error ?? "No se pudo fijar el obligatorio por salida.");
    }

    private async Task<McpToolResult> SetFormAutoCreateAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var (formId, ferr) = await NodeFormAsync(node, Str(args, "formulario"), ct);
        if (formId is null) { return Err(ferr!); }
        var on = Bool(args, "activo");
        if (on is null) { return Err("'activo' es obligatorio."); }
        var res = await _flowDesign!.SetNodeFormAutoCreateAsync(node.Id, formId.Value, on.Value, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, formulario_id = formId, crear_al_llegar = on.Value })
            : Err(res.Error ?? "No se pudo guardar.");
    }

    /// <summary>Resuelve un formulario QUE YA ESTA en el nodo (id, codigo o titulo).</summary>
    private async Task<(Guid? Id, string? Error)> NodeFormAsync(FlowCanvasNodeDto node, string? key, CancellationToken ct)
    {
        var forms = node.Forms ?? Array.Empty<FlowNodeFormDto>();
        if (forms.Count == 0) { return (null, "El nodo no tiene formularios (poner_formulario_a_nodo)."); }
        if (key is null) { return (null, "'formulario' es obligatorio."); }
        var hit = Guid.TryParse(key, out var g) ? forms.FirstOrDefault(f => f.DefinitionId == g)
            : forms.FirstOrDefault(f => Same(f.Code, key)) ?? forms.FirstOrDefault(f => Same(f.Title, key))
              ?? SingleOrNull(forms.Where(f => f.Title.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        if (hit is not null) { return (hit.DefinitionId, null); }
        await Task.CompletedTask;
        return (null, $"El nodo no tiene el formulario '{key}'. Tiene: {string.Join(" | ", forms.Select(f => $"{f.Title} ({f.Code})"))}.");
    }

    // ===================== Reglas del nodo =====================

    private async Task<McpToolResult> ListRuleCatalogAsync(JsonElement args, CancellationToken ct)
    {
        var items = await _flowDesign!.ListRuleCatalogAsync(ct);
        var q = Str(args, "buscar");
        var rows = items.Where(r => q is null || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                    || r.VerbName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                    || r.DocumentName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(200)
            .Select(r => new { regla_id = r.RuleId, nombre = r.Name, verbo = r.VerbName, estado = r.Status.ToString(), documento = r.DocumentName })
            .ToList();
        return Ok(new { total = rows.Count, reglas = rows });
    }

    private async Task<McpToolResult> AddNodeRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var key = Str(args, "regla");
        if (key is null) { return Err("'regla' es obligatoria (id o nombre; ver listar_reglas_disponibles)."); }
        var catalog = await _flowDesign!.ListRuleCatalogAsync(ct);
        var rule = Guid.TryParse(key, out var g) ? catalog.FirstOrDefault(r => r.RuleId == g)
            : catalog.FirstOrDefault(r => Same(r.Name, key))
              ?? SingleOrNull(catalog.Where(r => r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        if (rule is null) { return Err($"No encontre la regla '{key}' (o hay varias con ese texto). Usa listar_reglas_disponibles."); }
        if (node.Rules.Any(r => r.RuleId == rule.RuleId)) { return Err($"El nodo ya tiene la regla '{rule.Name}'."); }

        var res = await _flowDesign.AddNodeRuleAsync(node.Id, rule.RuleId, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo vincular la regla."); }
        var auto = Bool(args, "autonoma");
        if (auto is not null && auto.Value != res.Value.IsAutonomous)
        {
            var sa = await _flowDesign.SetNodeRuleAutonomousAsync(res.Value.LinkId, auto.Value, ct);
            if (!sa.IsOk) { return Err(sa.Error ?? "Se vinculo la regla pero no se pudo fijar si es autonoma."); }
        }
        return Ok(new { ok = true, nodo_id = node.Id, regla = rule.Name, verbo = rule.VerbName, autonoma = auto ?? res.Value.IsAutonomous });
    }

    private async Task<McpToolResult> RemoveNodeRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var (link, lerr) = NodeRuleLink(node, Str(args, "regla"));
        if (link is null) { return Err(lerr!); }
        var res = await _flowDesign!.RemoveNodeRuleAsync(link.LinkId, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, quitada = link.RuleName }) : Err(res.Error ?? "No se pudo quitar la regla.");
    }

    private async Task<McpToolResult> SetNodeRuleAutonomousAsync(JsonElement args, CancellationToken ct)
    {
        var (_, node, error) = await LoadFlowNodeAsync(args, ct);
        if (node is null) { return Err(error!); }
        var (link, lerr) = NodeRuleLink(node, Str(args, "regla"));
        if (link is null) { return Err(lerr!); }
        var on = Bool(args, "autonoma");
        if (on is null) { return Err("'autonoma' es obligatorio."); }
        var res = await _flowDesign!.SetNodeRuleAutonomousAsync(link.LinkId, on.Value, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = node.Id, regla = link.RuleName, autonoma = on.Value }) : Err(res.Error ?? "No se pudo guardar.");
    }

    private static (FlowNodeRuleDto? Link, string? Error) NodeRuleLink(FlowCanvasNodeDto node, string? key)
    {
        if (key is null) { return (null, "'regla' es obligatoria (id o nombre)."); }
        if (node.Rules.Count == 0) { return (null, "El nodo no tiene reglas."); }
        var hit = Guid.TryParse(key, out var g) ? node.Rules.FirstOrDefault(r => r.RuleId == g || r.LinkId == g)
            : node.Rules.FirstOrDefault(r => Same(r.RuleName, key))
              ?? SingleOrNull(node.Rules.Where(r => r.RuleName.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        return hit is null
            ? (null, $"El nodo no tiene la regla '{key}'. Tiene: {string.Join(" | ", node.Rules.Select(r => r.RuleName))}.")
            : (hit, null);
    }

    // ===================== Ciclo de vida del flujo =====================

    private async Task<McpToolResult> EditFlowPropsAsync(JsonElement args, CancellationToken ct)
    {
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var canvas = await _flowDesign!.GetCanvasAsync(defId.Value, ct);
        if (canvas is null) { return Err("Flujo no encontrado."); }
        var name = HasArg(args, "nombre") ? Str(args, "nombre") : canvas.Name;
        if (string.IsNullOrWhiteSpace(name)) { return Err("'nombre' no puede quedar vacio."); }
        var cat = HasArg(args, "categoria") ? Str(args, "categoria") : canvas.Category;
        var desc = HasArg(args, "descripcion") ? Str(args, "descripcion") : canvas.Description;
        var res = await _flowDesign.UpdateDefinitionPropsAsync(defId.Value, Clip(name!, 200), cat, desc, ct);
        return res.IsOk ? Ok(new { ok = true, definicion_id = defId, nombre = name, categoria = cat, descripcion = desc })
            : Err(res.Error ?? "No se pudo editar el flujo.");
    }

    private async Task<McpToolResult> PauseResumeFlowAsync(JsonElement args, bool pause, CancellationToken ct)
    {
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var res = pause ? await _flowDesign!.PauseAsync(defId.Value, ct) : await _flowDesign!.ResumeAsync(defId.Value, ct);
        return res.IsOk
            ? Ok(new { ok = true, definicion_id = defId, estado = pause ? "pausado" : "activo", nota = pause ? "Las tareas nuevas no arrancan este flujo; las que estan en marcha siguen." : null })
            : Err(res.Error ?? (pause ? "No se pudo pausar." : "No se pudo reanudar."));
    }

    private async Task<McpToolResult> UnlinkFlowFromConceptAsync(JsonElement args, CancellationToken ct)
    {
        var key = Str(args, "concepto");
        if (key is null) { return Err("'concepto' es obligatorio (codigo o nombre; ver listar_conceptos)."); }
        var low = key.ToLower();
        var concept = await _db.ActividadSubcategorias
            .Where(s => !s.IsArchived && (s.Codigo.ToLower() == low || s.Nombre.ToLower() == low))
            .FirstOrDefaultAsync(ct);
        if (concept is null) { return Err($"Concepto '{key}' no encontrado. Usa listar_conceptos."); }
        if (concept.WorkflowDefinitionId is null) { return Err($"El concepto '{concept.Nombre}' no tiene flujo enganchado."); }
        var flowName = await _db.WorkflowDefinitions.AsNoTracking().Where(d => d.Id == concept.WorkflowDefinitionId)
            .Select(d => d.Name).FirstOrDefaultAsync(ct);
        concept.WorkflowDefinitionId = null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true, concepto = concept.Nombre, flujo_quitado = flowName });
    }

    // ===================== Helpers =====================

    /// <summary>Carga el canvas y el nodo de 'nodo_id' (con toda su configuracion).</summary>
    private async Task<(FlowCanvasDto? Canvas, FlowCanvasNodeDto? Node, string? Error)> LoadFlowNodeAsync(JsonElement args, CancellationToken ct)
    {
        var nodeId = Guid2(args, "nodo_id");
        if (nodeId is null) { return (null, null, "'nodo_id' no es valido (ver ver_flujo)."); }
        var defId = await _db.WorkflowNodes.AsNoTracking().Where(n => n.Id == nodeId).Select(n => (Guid?)n.DefinitionId).FirstOrDefaultAsync(ct);
        if (defId is null) { return (null, null, "Nodo no encontrado (ver ver_flujo)."); }
        var canvas = await _flowDesign!.GetCanvasAsync(defId.Value, ct);
        var node = canvas?.Nodes.FirstOrDefault(n => n.Id == nodeId);
        return node is null ? (null, null, "Nodo no encontrado (ver ver_flujo).") : (canvas, node, null);
    }

    private static string DraftHint(string? error, FlowCanvasDto canvas)
        => (error ?? "No se pudo guardar.") + (canvas.IsEditable ? string.Empty
            : " Esta propiedad solo se cambia en borrador: usa nueva_version_flujo, aplicala en el borrador (ids nuevos) y publica.");

    private static string NodeName(FlowCanvasDto canvas, Guid id)
    {
        var n = canvas.Nodes.FirstOrDefault(x => x.Id == id);
        return n is null ? id.ToString() : string.IsNullOrWhiteSpace(n.Name) ? NodeTypeEs(n.NodeType) : n.Name!;
    }

    private static string DurationEs(StepSla s)
    {
        var parts = new List<string>();
        if (s.Days > 0) { parts.Add($"{s.Days} dia(s) {(s.DayMode == StepSlaDayMode.Business ? "habiles" : "calendario")}"); }
        if (s.Hours > 0) { parts.Add($"{s.Hours} h"); }
        if (s.Minutes > 0) { parts.Add($"{s.Minutes} min"); }
        return parts.Count == 0 ? "0" : string.Join(" ", parts);
    }

    private static object NotifySummary(NodeNotifyRule r, FlowCanvasDto canvas) => new
    {
        canal = r.Canal switch
        {
            NotifyChannel.WhatsApp => "whatsapp",
            NotifyChannel.WhatsAppGrupo => "whatsapp_grupo",
            NotifyChannel.Telegram => "telegram",
            _ => "correo"
        },
        destino = r.Canal is NotifyChannel.WhatsAppGrupo or NotifyChannel.Telegram ? null : r.Destino switch
        {
            NodeNotifyRecipient.Usuario => "usuario",
            NodeNotifyRecipient.ContactoTarea => "contacto_tarea",
            _ => "encargado_paso"
        },
        plantilla = r.Plantilla,
        asunto = r.Asunto,
        mensaje = r.Mensaje is null ? null : Clip(r.Mensaje, 160),
        enlaces_decision = r.EnlacesDecision is { Count: > 0 } ls
            ? ls.Select(l => new { variable = l.Variable, salida = NodeName(canvas, l.TargetNodeId), captura = l.Capture.ToString() })
            : null
    };

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return new List<string>(); }
        try { return JsonSerializer.Deserialize<List<string>>(json!) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }
}
