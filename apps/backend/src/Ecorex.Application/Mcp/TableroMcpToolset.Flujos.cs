using System.Text.Json;
using Ecorex.Application.Roles;
using Ecorex.Application.Tenancy;
using Ecorex.Application.Workflows;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Mcp;

/// <summary>
/// AUTORIA DE FLUJOS (procesos BPMN) en el servidor MCP (ADR-0159, addendum "autoria flujos+formularios").
/// Capa delgada sobre <see cref="IWorkflowDesignService"/> (diseno nodo a nodo del editor /flujos),
/// <see cref="IWorkflowEngine"/> (publicar) e <see cref="Organization.IWorkflowNodePolicyService"/> (cargos por
/// nodo): la IA crea un flujo, inserta pasos, los conecta, les pone condiciones/encargado/cargo/formulario, lo
/// publica y lo engancha a un concepto para que las tareas lo ejecuten. Corre COMO el usuario del token y bajo
/// SU permiso del modulo "flujos" (igual que la pantalla). No reimplementa reglas del motor; lo unico que agrega
/// es una REVISION del grafo (avisos) para que una IA no publique un flujo roto sin que el usuario lo confirme.
/// OJO semantica del motor: un nodo que NO es compuerta sigue TODAS sus salidas (ramas paralelas).
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
        "listar_flujos", "ver_flujo", "crear_flujo", "nueva_version_flujo", "agregar_nodo", "renombrar_nodo",
        "conectar_nodos", "eliminar_conexion", "condicion_ruta", "asignar_encargado_nodo", "asignar_cargo_nodo",
        "poner_formulario_a_nodo", "eliminar_nodo", "importar_bpmn", "publicar_flujo", "enganchar_flujo_a_concepto"
    };

    private static readonly AiToolSpec[] FlowSpecs =
    {
        new("listar_flujos",
            "Lista los flujos (procesos) del tenant con su estado, version, numero de nodos e instancias en marcha. " +
            "Usala primero para conocer nombres, codigos e ids exactos.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("ver_flujo",
            "Muestra el grafo de un flujo: nodos (id, tipo, nombre, origen del encargado, cargos, formularios), aristas " +
            "(id, desde, hasta, condicion) y AVISOS de revision (ramas paralelas involuntarias, nodos sin entrada o sin " +
            "salida, pasos sin quien los atienda). 'flujo' = nombre, codigo o id (ver listar_flujos).",
            """{"type":"object","properties":{"flujo":{"type":"string"}},"required":["flujo"],"additionalProperties":false}"""),
        new("crear_flujo",
            "Crea un flujo NUEVO en borrador. Nace con un nodo de inicio y uno de fin YA CONECTADOS entre si. Para meter " +
            "pasos usa agregar_nodo con desde_nodo_id=inicio y hasta_nodo_id=fin (inserta y reemplaza esa conexion). " +
            "Devuelve 'definicion_id', nodos y aristas.",
            """{"type":"object","properties":{"nombre":{"type":"string"},"categoria":{"type":"string"}},"required":["nombre"],"additionalProperties":false}"""),
        new("nueva_version_flujo",
            "Para MODIFICAR un flujo ya publicado: crea (o reutiliza) su version borrador siguiente y devuelve su " +
            "'definicion_id' con los ids NUEVOS de nodos y aristas (los vinculos se copian). Edita ese borrador y " +
            "publicalo: los conceptos enganchados pasan solos a la version nueva.",
            """{"type":"object","properties":{"flujo":{"type":"string"}},"required":["flujo"],"additionalProperties":false}"""),
        new("agregar_nodo",
            "Agrega un nodo a un flujo en borrador. 'tipo': tarea (paso humano), compuerta (decision con rutas), fin o " +
            "inicio (solo hay uno). Con 'desde_nodo_id' y 'hasta_nodo_id' lo INSERTA entre esos dos nodos: conecta " +
            "desde->nuevo->hasta y quita la conexion directa desde->hasta (conserva su condicion). Sin ellos queda suelto " +
            "y hay que conectarlo con conectar_nodos. Devuelve 'nodo_id'.",
            """{"type":"object","properties":{"flujo":{"type":"string","description":"definicion_id del borrador"},"tipo":{"type":"string","enum":["tarea","compuerta","fin","inicio"]},"nombre":{"type":"string"},"desde_nodo_id":{"type":"string","description":"Insertar despues de este nodo"},"hasta_nodo_id":{"type":"string","description":"Insertar antes de este nodo"},"x":{"type":"integer"},"y":{"type":"integer"}},"required":["flujo","tipo"],"additionalProperties":false}"""),
        new("renombrar_nodo",
            "Cambia el nombre de un nodo (ver 'nodo_id' en ver_flujo).",
            """{"type":"object","properties":{"nodo_id":{"type":"string"},"nombre":{"type":"string"}},"required":["nodo_id","nombre"],"additionalProperties":false}"""),
        new("conectar_nodos",
            "Conecta dos nodos del mismo flujo en borrador (de 'desde_nodo_id' a 'hasta_nodo_id'). OJO: un nodo que no es " +
            "compuerta sigue TODAS sus salidas en paralelo. 'condicion' opcional para una salida de compuerta (formato del " +
            "motor, ej. approval == 'Aprobado'; vacio = ruta por defecto). Devuelve 'arista_id'.",
            """{"type":"object","properties":{"desde_nodo_id":{"type":"string"},"hasta_nodo_id":{"type":"string"},"condicion":{"type":"string"}},"required":["desde_nodo_id","hasta_nodo_id"],"additionalProperties":false}"""),
        new("eliminar_conexion",
            "Quita una conexion (arista) de un flujo en borrador. 'arista_id' de ver_flujo.",
            """{"type":"object","properties":{"arista_id":{"type":"string"}},"required":["arista_id"],"additionalProperties":false}"""),
        new("condicion_ruta",
            "Fija (o limpia) la condicion de una arista/salida de compuerta. 'arista_id' de ver_flujo; 'condicion' " +
            "vacia = ruta por defecto.",
            """{"type":"object","properties":{"arista_id":{"type":"string"},"condicion":{"type":"string"}},"required":["arista_id"],"additionalProperties":false}"""),
        new("asignar_encargado_nodo",
            "Define de donde sale el ENCARGADO de un nodo. 'origen': politica (por cargo/dependencia: asignalos con " +
            "asignar_cargo_nodo), hereda_inicio (quien arranco el flujo), hereda_anterior (quien cerro el paso previo) o " +
            "campo_formulario (toma el usuario de un campo; indica 'campo' con su codigo).",
            """{"type":"object","properties":{"nodo_id":{"type":"string"},"origen":{"type":"string","enum":["politica","hereda_inicio","hereda_anterior","campo_formulario"]},"campo":{"type":"string","description":"Codigo del campo (solo para campo_formulario)"}},"required":["nodo_id","origen"],"additionalProperties":false}"""),
        new("asignar_cargo_nodo",
            "Agrega (accion='agregar', por defecto) o quita (accion='quitar') un CARGO o DEPENDENCIA del organigrama que " +
            "atiende el nodo (origen 'politica'). 'cargo' = id o nombre; si no existe responde las opciones validas.",
            """{"type":"object","properties":{"nodo_id":{"type":"string"},"cargo":{"type":"string"},"accion":{"type":"string","enum":["agregar","quitar"]}},"required":["nodo_id","cargo"],"additionalProperties":false}"""),
        new("poner_formulario_a_nodo",
            "Vincula un formulario ACTIVO a un nodo (se diligencia al llegar el paso). 'formulario' = id, codigo o titulo " +
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
            "Publica un flujo (deja activa esta version y despublica otras del mismo proceso). Antes revisa el grafo: si " +
            "hay AVISOS no publica y los devuelve; muestraselos al usuario y solo con su visto bueno repite con " +
            "forzar=true. Solo un flujo publicado arranca al crear tareas.",
            """{"type":"object","properties":{"flujo":{"type":"string"},"forzar":{"type":"boolean","description":"Publicar aunque haya avisos (solo con visto bueno del usuario)"}},"required":["flujo"],"additionalProperties":false}"""),
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
            "nueva_version_flujo" => await NewFlowVersionAsync(args, ct),
            "agregar_nodo" => await AddFlowNodeAsync(args, ct),
            "renombrar_nodo" => await RenameFlowNodeAsync(args, ct),
            "conectar_nodos" => await ConnectFlowNodesAsync(args, ct),
            "eliminar_conexion" => await DeleteFlowEdgeAsync(args, ct),
            "condicion_ruta" => await SetFlowEdgeConditionAsync(args, ct),
            "asignar_encargado_nodo" => await SetFlowNodeAssigneeAsync(args, ct),
            "asignar_cargo_nodo" => await SetFlowNodeCargoAsync(args, ct),
            "poner_formulario_a_nodo" => await SetFlowNodeFormAsync(args, ct),
            "eliminar_nodo" => await DeleteFlowNodeAsync(args, ct),
            "importar_bpmn" => await ImportFlowBpmnAsync(args, ct),
            "publicar_flujo" => await PublishFlowAsync(args, ct),
            "enganchar_flujo_a_concepto" => await LinkFlowToConceptAsync(args, ct),
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

        var cargos = await NodeCargosAsync(canvas, ct);
        var avisos = await FlowWarningsAsync(canvas, cargos, ct);
        return Ok(new
        {
            definicion_id = canvas.DefinitionId,
            codigo = canvas.ProcessCode,
            nombre = canvas.Name,
            version = canvas.Version,
            estado = canvas.Estado,
            editable = canvas.IsEditable,
            nota = canvas.IsEditable ? null : "Publicado: para cambiar el grafo usa nueva_version_flujo.",
            nodos = canvas.Nodes.Select(n => new
            {
                nodo_id = n.Id,
                bpmn = n.BpmnElementId,
                nombre = n.Name,
                tipo = NodeTypeEs(n.NodeType),
                encargado_origen = n.NodeType == WorkflowNodeType.Task ? AssigneeSourceEs(n.AssigneeSource) : null,
                campo_encargado = n.AssigneeFormFieldCode,
                cargos = cargos.TryGetValue(n.Id, out var cs) && cs.Count > 0 ? cs : null,
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
            }),
            avisos = avisos.Count > 0 ? avisos : null
        });
    }

    // ===================== Escritura =====================

    private async Task<McpToolResult> CreateFlowAsync(JsonElement args, CancellationToken ct)
    {
        var name = Str(args, "nombre");
        if (name is null) { return Err("'nombre' es obligatorio."); }
        var res = await _flowDesign!.CreateDraftAsync(Clip(name, 200), Str(args, "categoria"), ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo crear el flujo."); }
        return Ok(FlowCanvasPayload(res.Value,
            "Flujo creado en borrador con inicio -> fin conectados. Inserta pasos con agregar_nodo (desde_nodo_id=inicio, " +
            "hasta_nodo_id=fin), revisa con ver_flujo y publica."));
    }

    private async Task<McpToolResult> NewFlowVersionAsync(JsonElement args, CancellationToken ct)
    {
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var res = await _flowDesign!.EnsureDraftAsync(defId.Value, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo preparar la version borrador."); }
        return Ok(FlowCanvasPayload(res.Value,
            "Version borrador lista: usa ESTOS ids de nodos/aristas para editar y luego publicar_flujo con este definicion_id."));
    }

    private async Task<McpToolResult> AddFlowNodeAsync(JsonElement args, CancellationToken ct)
    {
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }
        var type = ParseNodeType(Str(args, "tipo"));
        if (type is null) { return Err("'tipo' debe ser tarea, compuerta, fin o inicio."); }

        var from = Guid2(args, "desde_nodo_id");
        var to = Guid2(args, "hasta_nodo_id");
        if ((from is null) != (to is null))
        {
            return Err("Para insertar entre dos nodos indica AMBOS: desde_nodo_id y hasta_nodo_id.");
        }

        // Insercion: validar los dos nodos en el canvas y ubicar el nuevo entre ellos.
        FlowCanvasEdgeDto? direct = null;
        int x = Int(args, "x") ?? 320, y = Int(args, "y") ?? 200;
        if (from is not null)
        {
            var canvas = await _flowDesign!.GetCanvasAsync(defId.Value, ct);
            if (canvas is null) { return Err("Flujo no encontrado."); }
            var a = canvas.Nodes.FirstOrDefault(n => n.Id == from);
            var b = canvas.Nodes.FirstOrDefault(n => n.Id == to);
            if (a is null || b is null) { return Err("desde_nodo_id / hasta_nodo_id no pertenecen a este flujo (ver ver_flujo)."); }
            direct = canvas.Edges.FirstOrDefault(e => e.SourceNodeId == from && e.TargetNodeId == to);
            if (Int(args, "x") is null) { x = (a.X + b.X) / 2; }
            if (Int(args, "y") is null) { y = (a.Y + b.Y) / 2; }
        }

        var res = await _flowDesign!.AddNodeAsync(defId.Value, type.Value, x, y, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo agregar el nodo."); }
        var nodeId = res.Value.Id;

        var name = Str(args, "nombre");
        if (name is not null)
        {
            var rn = await _flowDesign.RenameNodeAsync(nodeId, Clip(name, 200), ct);
            if (!rn.IsOk) { return Err(rn.Error ?? "Se creo el nodo pero no se pudo nombrar."); }
        }

        if (from is not null)
        {
            var e1 = await _flowDesign.ConnectAsync(from.Value, nodeId, ct);
            if (!e1.IsOk || e1.Value is null) { return Err(e1.Error ?? "Se creo el nodo pero no se pudo conectar desde el nodo anterior."); }
            var e2 = await _flowDesign.ConnectAsync(nodeId, to!.Value, ct);
            if (!e2.IsOk) { return Err(e2.Error ?? "Se creo el nodo pero no se pudo conectar hacia el nodo siguiente."); }
            if (direct is not null)
            {
                // La salida original (p.ej. de una compuerta) conserva su condicion en el primer tramo.
                if (!string.IsNullOrWhiteSpace(direct.ConditionExpression))
                {
                    await _flowDesign.SetEdgeConditionAsync(e1.Value.Id, direct.ConditionExpression, ct);
                }
                var del = await _flowDesign.DeleteEdgeAsync(direct.Id, ct);
                if (!del.IsOk) { return Err(del.Error ?? "Se inserto el nodo pero no se pudo quitar la conexion directa."); }
            }
        }

        return Ok(new
        {
            ok = true,
            nodo_id = nodeId,
            bpmn = res.Value.BpmnElementId,
            tipo = NodeTypeEs(type.Value),
            nombre = name,
            insertado_entre = from is null ? null : new { desde = from, hasta = to, conexion_directa_quitada = direct is not null },
            aviso = from is null ? "Nodo suelto: conectalo con conectar_nodos." : null
        });
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

    private async Task<McpToolResult> DeleteFlowEdgeAsync(JsonElement args, CancellationToken ct)
    {
        var edgeId = Guid2(args, "arista_id");
        if (edgeId is null) { return Err("'arista_id' no es valido (ver ver_flujo)."); }
        var res = await _flowDesign!.DeleteEdgeAsync(edgeId.Value, ct);
        return res.IsOk ? Ok(new { ok = true, arista_id = edgeId }) : Err(res.Error ?? "No se pudo quitar la conexion.");
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
            ? Ok(new
            {
                ok = true,
                nodo_id = nodeId,
                origen = AssigneeSourceEs(source.Value),
                campo = source == WorkflowAssigneeSource.FormField ? field : null,
                aviso = source == WorkflowAssigneeSource.Policy ? "Con 'politica' el paso lo atienden sus cargos/dependencias: asignalos con asignar_cargo_nodo." : null
            })
            : Err(res.Error ?? "No se pudo fijar el encargado.");
    }

    private async Task<McpToolResult> SetFlowNodeCargoAsync(JsonElement args, CancellationToken ct)
    {
        if (_nodePolicies is null) { return Err("La asignacion de cargos a nodos no esta disponible."); }
        var nodeId = Guid2(args, "nodo_id");
        if (nodeId is null) { return Err("'nodo_id' no es valido (ver ver_flujo)."); }
        var key = Str(args, "cargo");
        if (key is null) { return Err("'cargo' es obligatorio (id o nombre del cargo/dependencia)."); }

        var units = await _nodePolicies.ListAssignableUnitsAsync(ct);
        var unit = Guid.TryParse(key, out var gid)
            ? units.FirstOrDefault(u => u.Id == gid)
            : units.FirstOrDefault(u => Same(u.Name, key))
              ?? SingleOrNull(units.Where(u => u.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        if (unit is null)
        {
            var options = string.Join(" | ", units.Take(40).Select(u => $"{u.Name} ({u.Classifier})"));
            return Err($"No encontre el cargo/dependencia '{key}'. Opciones: {(options.Length > 0 ? options : "el organigrama no tiene cargos ni dependencias")}.");
        }

        if (Str(args, "accion") == "quitar")
        {
            var current = await _nodePolicies.ListNodePoliciesAsync(nodeId.Value, ct);
            var row = current.FirstOrDefault(p => p.OrgUnitId == unit.Id);
            if (row is null) { return Err($"El nodo no tiene asignado '{unit.Name}'."); }
            var rm = await _nodePolicies.RemoveNodePolicyAsync(row.PolicyId, ct);
            return rm.IsOk ? Ok(new { ok = true, nodo_id = nodeId, quitado = unit.Name }) : Err(rm.Error ?? "No se pudo quitar el cargo.");
        }

        var add = await _nodePolicies.AddNodePolicyAsync(nodeId.Value, unit.Id, ct);
        if (!add.IsOk || add.Value is null) { return Err(add.Error ?? "No se pudo asignar el cargo."); }
        var source = await _db.WorkflowNodes.AsNoTracking().Where(n => n.Id == nodeId.Value).Select(n => n.AssigneeSource).FirstOrDefaultAsync(ct);
        return Ok(new
        {
            ok = true,
            nodo_id = nodeId,
            cargo = add.Value.OrgUnitName,
            candidatos = add.Value.CandidateCount,
            aviso = source != WorkflowAssigneeSource.Policy
                ? $"El nodo toma el encargado por '{AssigneeSourceEs(source)}': los cargos solo aplican con origen 'politica' (asignar_encargado_nodo)."
                : add.Value.CandidateCount == 0 ? "Ese cargo no tiene funcionarios: nadie atendera el paso hasta que se le asigne alguien." : null
        });
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
        return Ok(FlowCanvasPayload(res.Value, "Flujo importado en borrador. Revisalo con ver_flujo antes de publicar."));
    }

    private async Task<McpToolResult> PublishFlowAsync(JsonElement args, CancellationToken ct)
    {
        if (_engine is null) { return Err("La publicacion de flujos no esta disponible."); }
        var (defId, error) = await ResolveFlowDefAsync(Str(args, "flujo"), ct);
        if (defId is null) { return Err(error!); }

        // Revision previa: una IA no publica un grafo con avisos sin el visto bueno del usuario.
        var canvas = await _flowDesign!.GetCanvasAsync(defId.Value, ct);
        if (canvas is null) { return Err("Flujo no encontrado."); }
        var avisos = await FlowWarningsAsync(canvas, await NodeCargosAsync(canvas, ct), ct);
        var force = Bool(args, "forzar") == true;
        if (avisos.Count > 0 && !force)
        {
            return Err("No publique: el flujo tiene avisos. Muestraselos al usuario; corrige o, con su visto bueno, " +
                       "repite con forzar=true. Avisos: " + string.Join(" | ", avisos.Select((a, i) => $"{i + 1}) {a}")));
        }

        var res = await _engine.PublishAsync(defId.Value, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo publicar el flujo."); }
        return Ok(new { ok = true, definicion_id = defId, estado = "publicado", publicado_con_avisos = avisos.Count > 0 ? avisos : null });
    }

    private async Task<McpToolResult> LinkFlowToConceptAsync(JsonElement args, CancellationToken ct)
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

    // ===================== Revision del grafo =====================

    /// <summary>Nombres de los cargos/dependencias asignados a cada nodo de tarea (vacio si no hay servicio).</summary>
    private async Task<Dictionary<Guid, List<string>>> NodeCargosAsync(FlowCanvasDto canvas, CancellationToken ct)
    {
        var map = new Dictionary<Guid, List<string>>();
        if (_nodePolicies is null) { return map; }
        foreach (var n in canvas.Nodes.Where(n => n.NodeType == WorkflowNodeType.Task))
        {
            map[n.Id] = (await _nodePolicies.ListNodePoliciesAsync(n.Id, ct)).Select(p => p.OrgUnitName).ToList();
        }
        return map;
    }

    /// <summary>
    /// Avisos de revision del grafo (no reemplazan las reglas del motor; ayudan a que una IA no deje un flujo
    /// roto): ramas paralelas involuntarias (el motor sigue TODAS las salidas de un nodo que no es compuerta),
    /// nodos inalcanzables o sin salida, compuertas con menos de 2 salidas y pasos por politica sin cargo.
    /// </summary>
    private Task<List<string>> FlowWarningsAsync(FlowCanvasDto c, IReadOnlyDictionary<Guid, List<string>> cargos, CancellationToken ct)
    {
        var w = new List<string>();
        var outs = c.Edges.GroupBy(e => e.SourceNodeId).ToDictionary(g => g.Key, g => g.Count());
        var ins = c.Edges.GroupBy(e => e.TargetNodeId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var n in c.Nodes)
        {
            var name = string.IsNullOrWhiteSpace(n.Name) ? NodeTypeEs(n.NodeType) : n.Name!;
            var o = outs.GetValueOrDefault(n.Id);
            var i = ins.GetValueOrDefault(n.Id);
            if (n.NodeType != WorkflowNodeType.StartEvent && i == 0) { w.Add($"'{name}' no tiene entradas: nunca se alcanza."); }
            if (n.NodeType != WorkflowNodeType.EndEvent && o == 0) { w.Add($"'{name}' no tiene salida: el flujo se queda detenido ahi."); }
            if (n.NodeType is not (WorkflowNodeType.ExclusiveGateway or WorkflowNodeType.EndEvent) && o > 1)
            {
                w.Add($"'{name}' tiene {o} salidas sin compuerta: el motor las sigue EN PARALELO. Si sobra una, usa eliminar_conexion; si es una decision, usa una compuerta.");
            }
            if (n.NodeType == WorkflowNodeType.ExclusiveGateway && o < 2) { w.Add($"La compuerta '{name}' tiene {o} salida(s): una decision necesita al menos 2."); }
            if (n.NodeType == WorkflowNodeType.Task && n.AssigneeSource == WorkflowAssigneeSource.Policy
                && _nodePolicies is not null && (!cargos.TryGetValue(n.Id, out var cs) || cs.Count == 0))
            {
                w.Add($"'{name}' toma el encargado por politica pero no tiene cargo/dependencia: nadie lo atendera (asignar_cargo_nodo u otro origen con asignar_encargado_nodo).");
            }
        }
        if (!c.Nodes.Any(n => n.NodeType == WorkflowNodeType.Task)) { w.Add("El flujo no tiene ningun paso de tarea: no hay nada que atender (agrega al menos uno)."); }
        return Task.FromResult(w);
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
        version = canvas.Version,
        estado = canvas.Estado,
        mensaje,
        nodos = canvas.Nodes.Select(n => new { nodo_id = n.Id, nombre = n.Name, tipo = NodeTypeEs(n.NodeType) }),
        aristas = canvas.Edges.Select(e => new { arista_id = e.Id, desde = e.SourceNodeId, hasta = e.TargetNodeId, condicion = e.ConditionExpression })
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
