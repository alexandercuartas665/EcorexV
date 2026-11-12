using System.Text.Json;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// AUTORIA DE FLUJOS (procesos BPMN) en el servidor MCP (ADR-0159, addendum "autoria flujos+formularios").
/// Capa delgada sobre <see cref="IWorkflowDesignService"/> (diseno nodo a nodo del editor /flujos) y
/// <see cref="IWorkflowEngine"/> (publicar): la IA crea un flujo nuevo, le agrega nodos, los conecta, les
/// pone condiciones/encargado/formulario, lo publica y lo engancha a un concepto para que las tareas lo
/// ejecuten. Corre COMO el usuario del token y bajo SU permiso del modulo "flujos" (igual que la pantalla).
/// No reimplementa reglas: cada herramienta delega en el servicio y devuelve ids para encadenar.
/// </summary>
public sealed partial class TableroMcpToolset
{
    /// <summary>Clave de modulo del Directorio de permisos para la autoria de flujos (ver Program.cs).</summary>
    private const string FlowModuleKey = "flujos";

    private static readonly HashSet<string> FlowReadOnly = new(StringComparer.Ordinal)
    {
        "listar_flujos", "ver_flujo"
    };

    private static readonly HashSet<string> FlowDestructive = new(StringComparer.Ordinal)
    {
        "eliminar_nodo"
    };

    private static readonly HashSet<string> FlowToolNames = new(StringComparer.Ordinal)
    {
        "listar_flujos", "ver_flujo", "crear_flujo", "agregar_nodo", "renombrar_nodo", "conectar_nodos",
        "condicion_ruta", "asignar_encargado_nodo", "poner_formulario_a_nodo", "eliminar_nodo",
        "importar_bpmn", "publicar_flujo", "enganchar_flujo_a_concepto"
    };

    private static readonly AiToolSpec[] FlowSpecs =
    {
        new("listar_flujos",
            "Lista los flujos (procesos) del tenant con su estado, version, numero de nodos e instancias en marcha. " +
            "Usala primero para conocer nombres, codigos e ids exactos.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_flujo",
            "Muestra el grafo de un flujo: nodos (id, tipo, nombre, origen del encargado, formularios) y aristas " +
            "(id, desde, hasta, condicion). 'flujo' = nombre, codigo o id (ver listar_flujos). Los ids que devuelve " +
            "se usan en agregar_nodo/conectar_nodos/condicion_ruta.",
            """{"type":"object","properties":{"flujo":{"type":"string"}},"required":["flujo"],"additionalProperties":false}"""),
        new("crear_flujo",
            "Crea un flujo NUEVO en borrador (arranca con un nodo de inicio y uno de fin ya conectados). Devuelve " +
            "'definicion_id' y los nodos iniciales. Luego agrega nodos entre el inicio y el fin y publica.",
            """{"type":"object","properties":{"nombre":{"type":"string"},"categoria":{"type":"string"}},"required":["nombre"],"additionalProperties":false}"""),
        new("agregar_nodo",
            "Agrega un nodo a un flujo en borrador. 'tipo': tarea (paso humano), compuerta (decision con rutas), fin " +
            "o inicio. 'nombre' opcional. Devuelve 'nodo_id'. Recuerda conectarlo con conectar_nodos.",
            """{"type":"object","properties":{"flujo":{"type":"string","description":"definicion_id del borrador"},"tipo":{"type":"string","enum":["tarea","compuerta","fin","inicio"]},"nombre":{"type":"string"},"x":{"type":"integer"},"y":{"type":"integer"}},"required":["flujo","tipo"],"additionalProperties":false}"""),
        new("renombrar_nodo",
            "Cambia el nombre de un nodo (ver 'nodo_id' en ver_flujo).",
            """{"type":"object","properties":{"nodo_id":{"type":"string"},"nombre":{"type":"string"}},"required":["nodo_id","nombre"],"additionalProperties":false}"""),
        new("conectar_nodos",
            "Conecta dos nodos del mismo flujo en borrador (de 'desde_nodo_id' a 'hasta_nodo_id'). 'condicion' opcional " +
            "para una salida de compuerta (formato del motor, ej. approval == 'Aprobado'; vacio = ruta por defecto). " +
            "Devuelve 'arista_id'.",
            """{"type":"object","properties":{"desde_nodo_id":{"type":"string"},"hasta_nodo_id":{"type":"string"},"condicion":{"type":"string"}},"required":["desde_nodo_id","hasta_nodo_id"],"additionalProperties":false}"""),
        new("condicion_ruta",
            "Fija (o limpia) la condicion de una arista/salida de compuerta. 'arista_id' de ver_flujo; 'condicion' " +
            "vacia = ruta por defecto.",
            """{"type":"object","properties":{"arista_id":{"type":"string"},"condicion":{"type":"string"}},"required":["arista_id"],"additionalProperties":false}"""),
        new("asignar_encargado_nodo",
            "Define de donde sale el ENCARGADO de un nodo. 'origen': politica (por cargo/dependencia del nodo), " +
            "hereda_inicio, hereda_anterior o campo_formulario (toma el usuario de un campo; indica 'campo' con su codigo).",
            """{"type":"object","properties":{"nodo_id":{"type":"string"},"origen":{"type":"string","enum":["politica","hereda_inicio","hereda_anterior","campo_formulario"]},"campo":{"type":"string","description":"Codigo del campo (solo para campo_formulario)"}},"required":["nodo_id","origen"],"additionalProperties":false}"""),
        new("poner_formulario_a_nodo",
            "Vincula un formulario a un nodo (se diligencia al llegar el paso). 'formulario' = id, codigo o titulo " +
            "(ver list_forms). 'obligatorio' true = el paso no se cierra hasta enviarlo (ADR-0077).",
            """{"type":"object","properties":{"nodo_id":{"type":"string"},"formulario":{"type":"string"},"obligatorio":{"type":"boolean"}},"required":["nodo_id","formulario"],"additionalProperties":false}"""),
        new("eliminar_nodo",
            "Borra un nodo del flujo en borrador con sus aristas y vinculos (NUNCA el nodo de inicio). Confirma con el " +
            "usuario antes de usarla.",
            """{"type":"object","properties":{"nodo_id":{"type":"string"}},"required":["nodo_id"],"additionalProperties":false}"""),
        new("importar_bpmn",
            "Crea un flujo nuevo en borrador a partir de un XML BPMN 2.0 (el que exporta bpmn.io o un archivo .bpmn). " +
            "Devuelve 'definicion_id'.",
            """{"type":"object","properties":{"bpmn_xml":{"type":"string"}},"required":["bpmn_xml"],"additionalProperties":false}"""),
        new("publicar_flujo",
            "Publica un flujo (deja activa esta version y despublica otras del mismo proceso). Solo un flujo publicado " +
            "arranca al crear tareas. 'flujo' = id, codigo o nombre.",
            """{"type":"object","properties":{"flujo":{"type":"string"}},"required":["flujo"],"additionalProperties":false}"""),
        new("enganchar_flujo_a_concepto",
            "Asocia un flujo a un concepto (tipo de actividad): las tareas nuevas de ese concepto ARRANCAN el flujo. " +
            "'concepto' = codigo o nombre (ver listar_conceptos); 'flujo' = id, codigo o nombre.",
            """{"type":"object","properties":{"concepto":{"type":"string"},"flujo":{"type":"string"}},"required":["concepto","flujo"],"additionalProperties":false}"""),
    };

    private async Task<McpToolResult> ExecuteFlowAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_flowDesign is null) { return Err("La autoria de flujos no esta disponible."); }

        // Permiso del modulo "flujos" (misma matriz que la pantalla; Owner/Admin y sin-rol = sin restriccion).
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var denied = tool switch
        {
            "listar_flujos" or "ver_flujo" => NeedFlow(perms, PermissionAction.View, "consultar flujos"),
            "crear_flujo" or "importar_bpmn" => NeedFlow(perms, PermissionAction.Create, "crear flujos"),
            "eliminar_nodo" => NeedFlow(perms, PermissionAction.Delete, "borrar nodos de un flujo"),
            _ => NeedFlow(perms, PermissionAction.Edit, "disenar flujos")
        };
        if (denied is not null) { return Err(denied); }

        return tool switch
        {
            "listar_flujos" => await ListFlowsAsync(ct),
            "ver_flujo" => await ViewFlowAsync(args, ct),
            "crear_flujo" => await CreateFlowAsync(args, ct),
            "agregar_nodo" => await AddFlowNodeAsync(args, ct),
            "renombrar_nodo" => await RenameFlowNodeAsync(args, ct),
            "conectar_nodos" => await ConnectFlowNodesAsync(args, ct),
            "condicion_ruta" => await SetFlowEdgeConditionAsync(args, ct),
            "asignar_encargado_nodo" => await SetFlowNodeAssigneeAsync(args, ct),
            "poner_formulario_a_nodo" => await SetFlowNodeFormAsync(args, ct),
            "eliminar_nodo" => await DeleteFlowNodeAsync(args, ct),
            "importar_bpmn" => await ImportFlowBpmnAsync(args, ct),
            "publicar_flujo" => await PublishFlowAsync(args, ct),
            "enganchar_flujo_a_concepto" => await LinkFlowToConceptAsync(args, actor, ct),
            _ => Err($"Herramienta desconocida: {tool}")
        };
    }

    private static string? NeedFlow(EffectivePermissions perms, PermissionAction action, string what)
        => perms.Can(FlowModuleKey, action) ? null : $"Tu usuario no tiene permiso para {what}.";

    // ===================== Lectura =====================

    private async Task<McpToolResult> ListFlowsAsync(CancellationToken ct)
    {
        var index = await _flowDesign!.ListForIndexAsync(ct);
        return Ok(new
        {
            flujos = index.Cards.Select(c => new
            {
                definicion_id = c.DefinitionId,
                codigo = c.ProcessCode,
                nombre = c.Name,
                categoria = c.Category,
                version = c.Version,
                estado = c.Estado,
                nodos = c.NodeCount,
                instancias_en_marcha = c.RunningInstances
            })
        });
    }

    private async Task<McpToolResult> ViewFlowAsync(JsonElement args, CancellationToken ct)
    {
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var canvas = await _flowDesign!.GetCanvasAsync(defId.Value, ct);
        if (canvas is null) { return Err("Flujo no encontrado."); }

        return Ok(new
        {
            definicion_id = canvas.DefinitionId,
            codigo = canvas.ProcessCode,
            nombre = canvas.Name,
            version = canvas.Version,
            estado = canvas.Estado,
            editable = canvas.IsEditable,
            nodos = canvas.Nodes.Select(n => new
            {
                nodo_id = n.Id,
                bpmn = n.BpmnElementId,
                nombre = n.Name,
                tipo = NodeTypeEs(n.NodeType),
                encargado_origen = AssigneeSourceEs(n.AssigneeSource),
                campo_encargado = n.AssigneeFormFieldCode,
                formularios = n.Forms is { Count: > 0 }
                    ? n.Forms.Select(f => new { codigo = f.Code, titulo = f.Title, obligatorio = f.IsRequired })
                    : null
            }),
            aristas = canvas.Edges.Select(e => new
            {
                arista_id = e.Id,
                desde = e.SourceNodeId,
                hasta = e.TargetNodeId,
                condicion = e.ConditionExpression
            })
        });
    }

    // ===================== Escritura =====================

    private async Task<McpToolResult> CreateFlowAsync(JsonElement args, CancellationToken ct)
    {
        var name = Str(args, "nombre");
        if (name is null) { return Err("'nombre' es obligatorio."); }
        var res = await _flowDesign!.CreateDraftAsync(Clip(name, 200), Str(args, "categoria"), ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo crear el flujo."); }
        return Ok(FlowCanvasPayload(res.Value, "Flujo creado en borrador. Agrega nodos entre el inicio y el fin y publicalo."));
    }

    private async Task<McpToolResult> AddFlowNodeAsync(JsonElement args, CancellationToken ct)
    {
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var type = ParseNodeType(Str(args, "tipo"));
        if (type is null) { return Err("'tipo' debe ser tarea, compuerta, fin o inicio."); }

        var x = Int(args, "x") ?? 320;
        var y = Int(args, "y") ?? 200;
        var res = await _flowDesign!.AddNodeAsync(defId.Value, type.Value, x, y, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo agregar el nodo."); }

        var name = Str(args, "nombre");
        if (name is not null)
        {
            var rn = await _flowDesign.RenameNodeAsync(res.Value.Id, Clip(name, 200), ct);
            if (!rn.IsOk) { return Err(rn.Error ?? "Se creo el nodo pero no se pudo nombrar."); }
        }
        return Ok(new { ok = true, nodo_id = res.Value.Id, bpmn = res.Value.BpmnElementId, tipo = NodeTypeEs(type.Value), nombre = name });
    }

    private async Task<McpToolResult> RenameFlowNodeAsync(JsonElement args, CancellationToken ct)
    {
        var nodeId = Guid2(args, "nodo_id");
        if (nodeId is null) { return Err("'nodo_id' no es valido (ver ver_flujo)."); }
        var name = Str(args, "nombre");
        if (name is null) { return Err("'nombre' es obligatorio."); }
        var res = await _flowDesign!.RenameNodeAsync(nodeId.Value, Clip(name, 200), ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = nodeId, nombre = name }) : Err(res.Error ?? "No se pudo renombrar.");
    }

    private async Task<McpToolResult> ConnectFlowNodesAsync(JsonElement args, CancellationToken ct)
    {
        var from = Guid2(args, "desde_nodo_id");
        var to = Guid2(args, "hasta_nodo_id");
        if (from is null || to is null) { return Err("'desde_nodo_id' y 'hasta_nodo_id' son obligatorios (ver ver_flujo)."); }
        var res = await _flowDesign!.ConnectAsync(from.Value, to.Value, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo conectar."); }

        var cond = Str(args, "condicion");
        if (cond is not null)
        {
            var sc = await _flowDesign.SetEdgeConditionAsync(res.Value.Id, cond, ct);
            if (!sc.IsOk) { return Err(sc.Error ?? "Se creo la conexion pero no se pudo poner la condicion."); }
        }
        return Ok(new { ok = true, arista_id = res.Value.Id, desde = from, hasta = to, condicion = cond });
    }

    private async Task<McpToolResult> SetFlowEdgeConditionAsync(JsonElement args, CancellationToken ct)
    {
        var edgeId = Guid2(args, "arista_id");
        if (edgeId is null) { return Err("'arista_id' no es valido (ver ver_flujo)."); }
        var res = await _flowDesign!.SetEdgeConditionAsync(edgeId.Value, Str(args, "condicion"), ct);
        return res.IsOk ? Ok(new { ok = true, arista_id = edgeId, condicion = Str(args, "condicion") }) : Err(res.Error ?? "No se pudo fijar la condicion.");
    }

    private async Task<McpToolResult> SetFlowNodeAssigneeAsync(JsonElement args, CancellationToken ct)
    {
        var nodeId = Guid2(args, "nodo_id");
        if (nodeId is null) { return Err("'nodo_id' no es valido (ver ver_flujo)."); }
        var source = ParseAssigneeSource(Str(args, "origen"));
        if (source is null) { return Err("'origen' debe ser politica, hereda_inicio, hereda_anterior o campo_formulario."); }
        var field = Str(args, "campo");
        if (source == WorkflowAssigneeSource.FormField && field is null)
        {
            return Err("Para 'campo_formulario' indica 'campo' con el codigo del campo que define el usuario.");
        }
        var res = await _flowDesign!.SetNodeAssigneeAsync(nodeId.Value, source.Value, field, ct);
        return res.IsOk
            ? Ok(new { ok = true, nodo_id = nodeId, origen = AssigneeSourceEs(source.Value), campo = source == WorkflowAssigneeSource.FormField ? field : null })
            : Err(res.Error ?? "No se pudo fijar el encargado.");
    }

    private async Task<McpToolResult> SetFlowNodeFormAsync(JsonElement args, CancellationToken ct)
    {
        var nodeId = Guid2(args, "nodo_id");
        if (nodeId is null) { return Err("'nodo_id' no es valido (ver ver_flujo)."); }
        var (formId, ferr) = await ResolveFormDefAsync(Str(args, "formulario"), ct);
        if (formId is null) { return Err(ferr!); }

        var res = await _flowDesign!.SetNodeFormAsync(nodeId.Value, formId.Value, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo vincular el formulario."); }

        var required = Bool(args, "obligatorio");
        if (required is not null)
        {
            var sr = await _flowDesign.SetNodeFormRequiredAsync(nodeId.Value, formId.Value, required.Value, ct);
            if (!sr.IsOk) { return Err(sr.Error ?? "Se vinculo el formulario pero no se pudo marcar obligatorio."); }
        }
        return Ok(new { ok = true, nodo_id = nodeId, formulario_id = formId, obligatorio = required });
    }

    private async Task<McpToolResult> DeleteFlowNodeAsync(JsonElement args, CancellationToken ct)
    {
        var nodeId = Guid2(args, "nodo_id");
        if (nodeId is null) { return Err("'nodo_id' no es valido (ver ver_flujo)."); }
        var res = await _flowDesign!.DeleteNodeAsync(nodeId.Value, ct);
        return res.IsOk ? Ok(new { ok = true, nodo_id = nodeId }) : Err(res.Error ?? "No se pudo borrar el nodo.");
    }

    private async Task<McpToolResult> ImportFlowBpmnAsync(JsonElement args, CancellationToken ct)
    {
        var xml = Str(args, "bpmn_xml");
        if (xml is null) { return Err("'bpmn_xml' es obligatorio."); }
        var res = await _flowDesign!.ImportBpmnAsync(xml, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo importar el BPMN."); }
        return Ok(FlowCanvasPayload(res.Value, "Flujo importado en borrador."));
    }

    private async Task<McpToolResult> PublishFlowAsync(JsonElement args, CancellationToken ct)
    {
        if (_engine is null) { return Err("La publicacion de flujos no esta disponible."); }
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var res = await _engine.PublishAsync(defId.Value, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo publicar el flujo."); }
        return Ok(new { ok = true, definicion_id = defId, estado = "publicado" });
    }

    private async Task<McpToolResult> LinkFlowToConceptAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        var conceptKey = Str(args, "concepto");
        if (conceptKey is null) { return Err("'concepto' es obligatorio (codigo o nombre; ver listar_conceptos)."); }
        var low = conceptKey.ToLower();
        var concept = await _db.ActividadSubcategorias
            .Where(s => !s.IsArchived && (s.Codigo.ToLower() == low || s.Nombre.ToLower() == low))
            .FirstOrDefaultAsync(ct);
        if (concept is null) { return Err($"Concepto '{conceptKey}' no encontrado. Usa listar_conceptos."); }

        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var def = await _db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Id == defId.Value).Select(d => new { d.Name, d.IsPublished }).FirstOrDefaultAsync(ct);
        if (def is null) { return Err("Flujo no encontrado."); }

        concept.WorkflowDefinitionId = defId.Value;
        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            ok = true,
            concepto = concept.Nombre,
            flujo = def.Name,
            publicado = def.IsPublished,
            aviso = def.IsPublished ? null : "El flujo aun no esta publicado: las tareas no lo arrancaran hasta publicarlo (publicar_flujo)."
        });
    }

    // ===================== Resolucion + helpers =====================

    private async Task<(Guid? Id, string? Error)> ResolveFlowDefAsync(string? key, CancellationToken ct)
    {
        if (key is null) { return (null, "'flujo' es obligatorio (nombre, codigo o id; ver listar_flujos)."); }
        if (Guid.TryParse(key, out var gid))
        {
            var exists = await _db.WorkflowDefinitions.AsNoTracking().AnyAsync(d => d.Id == gid && !d.IsArchived, ct);
            return exists ? (gid, null) : (null, $"No encontre el flujo '{key}'.");
        }
        var index = await _flowDesign!.ListForIndexAsync(ct);
        var card = index.Cards.FirstOrDefault(c => Same(c.ProcessCode, key))
                   ?? index.Cards.FirstOrDefault(c => Same(c.Name, key))
                   ?? SingleOrNull(index.Cards.Where(c => c.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        return card is null ? (null, $"No encontre el flujo '{key}'. Usa listar_flujos.") : (card.DefinitionId, null);
    }

    private async Task<(Guid? Id, string? Error)> ResolveFormDefAsync(string? key, CancellationToken ct)
    {
        if (key is null) { return (null, "'formulario' es obligatorio (id, codigo o titulo; ver list_forms)."); }
        if (Guid.TryParse(key, out var gid))
        {
            var exists = await _db.FormDefinitions.AsNoTracking().AnyAsync(f => f.Id == gid && !f.IsArchived, ct);
            return exists ? (gid, null) : (null, $"No encontre el formulario '{key}'.");
        }
        var rows = await _db.FormDefinitions.AsNoTracking().Where(f => !f.IsArchived)
            .Select(f => new { f.Id, f.Code, f.Title }).ToListAsync(ct);
        var hit = rows.FirstOrDefault(f => Same(f.Code, key)) ?? rows.FirstOrDefault(f => Same(f.Title, key))
                  ?? SingleOrNull(rows.Where(f => f.Title.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        return hit is null ? (null, $"No encontre el formulario '{key}'. Usa list_forms.") : (hit.Id, null);
    }

    private static object FlowCanvasPayload(FlowCanvasDto canvas, string mensaje) => new
    {
        ok = true,
        definicion_id = canvas.DefinitionId,
        codigo = canvas.ProcessCode,
        nombre = canvas.Name,
        estado = canvas.Estado,
        mensaje,
        nodos = canvas.Nodes.Select(n => new { nodo_id = n.Id, nombre = n.Name, tipo = NodeTypeEs(n.NodeType) })
    };

    private static Guid? Guid2(JsonElement a, string name)
        => Guid.TryParse(Str(a, name), out var g) ? g : null;

    private static WorkflowNodeType? ParseNodeType(string? t) => t?.ToLowerInvariant() switch
    {
        "tarea" or "actividad" or "task" => WorkflowNodeType.Task,
        "compuerta" or "decision" or "gateway" => WorkflowNodeType.ExclusiveGateway,
        "fin" or "final" or "end" => WorkflowNodeType.EndEvent,
        "inicio" or "start" => WorkflowNodeType.StartEvent,
        _ => null
    };

    private static string NodeTypeEs(WorkflowNodeType t) => t switch
    {
        WorkflowNodeType.StartEvent => "inicio",
        WorkflowNodeType.Task => "tarea",
        WorkflowNodeType.ExclusiveGateway => "compuerta",
        WorkflowNodeType.EndEvent => "fin",
        _ => t.ToString()
    };

    private static WorkflowAssigneeSource? ParseAssigneeSource(string? s) => s?.ToLowerInvariant() switch
    {
        "politica" or "cargo" or "policy" => WorkflowAssigneeSource.Policy,
        "hereda_inicio" or "inheritstart" => WorkflowAssigneeSource.InheritStart,
        "hereda_anterior" or "inheritprevious" => WorkflowAssigneeSource.InheritPrevious,
        "campo_formulario" or "formulario" or "campo" or "formfield" => WorkflowAssigneeSource.FormField,
        _ => null
    };

    private static string AssigneeSourceEs(WorkflowAssigneeSource s) => s switch
    {
        WorkflowAssigneeSource.Policy => "politica",
        WorkflowAssigneeSource.InheritStart => "hereda_inicio",
        WorkflowAssigneeSource.InheritPrevious => "hereda_anterior",
        WorkflowAssigneeSource.FormField => "campo_formulario",
        _ => s.ToString()
    };
}
