using System.Text.Json;
using System.Text.Json.Nodes;
using Ecorex.Application.Forms;
using Ecorex.Application.Forms.Builder;
using Ecorex.Application.Roles;
using Ecorex.Application.Rules;
using Ecorex.Application.Tenancy;
using Ecorex.Domain.Enums;

namespace Ecorex.Application.Mcp;

/// <summary>
/// REGLAS Y OPCIONES DE FORMULARIOS en el MCP (ADR-0211, ola 2). El puente de formularios (ADR-0067) arma la
/// ESTRUCTURA; aqui la IA configura el COMPORTAMIENTO, una herramienta por propiedad (decision del usuario):
/// - Reglas del motor (ADR-0016) con CUALQUIER verbo registrado: catalogo de verbos con sus parametros, crear,
///   cambiar parametros/estado/nombre, borrar, historial; vincular al ENVIAR, a un CAMPO o BOTON, parametros por
///   vinculo, y la condicion entre campos (BLOQUEAR_CAMPO_XCONDICION). Sobre <see cref="IRuleDocumentService"/>.
/// - Opciones del formulario: ocultar barra de envio, reportable, codigo automatico, registro editable, cierre
///   automatico, versiones (guardar/listar/restaurar) y enlaces publicos (listar/revocar).
/// - Propiedades de campo que el esquema del puente no edita: bloqueado, oculto, valor por defecto dinamico,
///   permisos por rol y subformulario (PATCH sobre la pregunta actual: no borra nada mas).
/// Permisos: las herramientas del catalogo de reglas usan el modulo "reglas"; las que tocan un formulario,
/// "formularios" (igual que la pantalla). Lo que corre al ejecutarse lo decide el motor: una regla solo corre si
/// ella y su documento estan ACTIVOS.
/// </summary>
public sealed partial class TableroMcpToolset
{
    private const string RulesModuleKey = "reglas";

    private bool HasFormConfig => _ruleDocs is not null && _formDefs is not null && _rulesEngine is not null;

    private static readonly HashSet<string> FormConfigReadOnly = new(StringComparer.Ordinal)
    {
        "listar_verbos_regla", "listar_reglas", "ver_regla", "reglas_de_formulario", "historial_regla",
        "listar_versiones_formulario", "listar_enlaces_formulario"
    };

    private static readonly HashSet<string> FormConfigDestructive = new(StringComparer.Ordinal)
    {
        "eliminar_regla", "quitar_regla_al_enviar", "quitar_regla_de_campo", "restaurar_version_formulario",
        "revocar_enlace_formulario"
    };

    /// <summary>Herramientas cuyo permiso es el del modulo de REGLAS (el resto, el de formularios).</summary>
    private static readonly HashSet<string> RuleCatalogTools = new(StringComparer.Ordinal)
    {
        "listar_verbos_regla", "listar_reglas", "ver_regla", "historial_regla", "crear_regla", "parametros_regla",
        "estado_regla", "renombrar_regla", "eliminar_regla"
    };

    private static readonly HashSet<string> FormConfigToolNames = new(StringComparer.Ordinal)
    {
        "listar_verbos_regla", "listar_reglas", "ver_regla", "reglas_de_formulario", "historial_regla",
        "crear_regla", "parametros_regla", "estado_regla", "renombrar_regla", "eliminar_regla",
        "regla_al_enviar", "quitar_regla_al_enviar", "regla_en_campo", "quitar_regla_de_campo",
        "parametros_regla_en_campo", "condicion_entre_campos",
        "ocultar_barra_envio", "formulario_reportable", "codigo_automatico_formulario", "registro_editable",
        "cierre_automatico_formulario", "guardar_version_formulario", "listar_versiones_formulario",
        "restaurar_version_formulario", "listar_enlaces_formulario", "revocar_enlace_formulario",
        "campo_bloqueado", "campo_oculto", "valor_por_defecto_dinamico", "permisos_campo_por_rol",
        "subformulario_de_campo"
    };

    private const string FormProp = "\"formulario\":{\"type\":\"string\",\"description\":\"Id, codigo o titulo (ver list_forms)\"}";
    private const string CampoProp = "\"campo\":{\"type\":\"string\",\"description\":\"Codigo del campo (field_code), id o etiqueta (ver get_form)\"}";
    private const string ReglaProp = "\"regla\":{\"type\":\"string\",\"description\":\"Id o nombre (ver listar_reglas)\"}";
    private const string ParamsProp = "\"parametros\":{\"type\":\"object\",\"description\":\"Parametros del verbo (nombres de listar_verbos_regla)\"}";
    private const string ActivoProp = "\"activo\":{\"type\":\"boolean\"}";

    private static string Obj(string props, params string[] required)
        => "{\"type\":\"object\",\"properties\":{" + props + "}" +
           (required.Length > 0 ? ",\"required\":[" + string.Join(",", required.Select(r => "\"" + r + "\"")) + "]" : string.Empty) +
           ",\"additionalProperties\":false}";

    private static readonly AiToolSpec[] FormConfigSpecs =
    {
        // ---------------- Catalogo de reglas ----------------
        new("listar_verbos_regla",
            "Catalogo de VERBOS del motor de reglas (lo que una regla puede hacer: crear tarea, pasar campos, notificar, " +
            "completar paso del flujo, imprimir, convertir a otro formulario...) con sus PARAMETROS (nombre, tipo, " +
            "obligatorio, opciones). Leelo antes de crear_regla o regla_al_enviar.",
            Obj("\"buscar\":{\"type\":\"string\"},\"verbo\":{\"type\":\"string\",\"description\":\"Un verbo concreto (detalle completo)\"}")),
        new("listar_reglas",
            "Reglas del tenant (nombre, verbo, estado, documento). 'buscar' filtra por nombre, verbo o documento.",
            Obj("\"buscar\":{\"type\":\"string\"}")),
        new("ver_regla",
            "Una regla completa: verbo, parametros, estado, donde esta vinculada (campos de formularios y nodos de flujo).",
            Obj(ReglaProp, "regla")),
        new("historial_regla",
            "Ultimas ejecuciones de una regla (estado, registros afectados, error). Sirve para verificar que corrio.",
            Obj(ReglaProp + ",\"limite\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100}", "regla")),
        new("crear_regla",
            "Crea una regla con un VERBO y sus parametros (valida que el verbo exista y los obligatorios). Con 'formulario' " +
            "queda en el documento de reglas de ese formulario; si no, en 'documento' o en 'Reglas creadas por IA'. Nace " +
            "ACTIVA salvo estado=desarrollo. Para que actue, vinculala: regla_en_campo (campo o boton) o agregar_regla_nodo. " +
            "Para el ENVIO del formulario usa mejor regla_al_enviar.",
            Obj("\"nombre\":{\"type\":\"string\"},\"verbo\":{\"type\":\"string\"}," + ParamsProp + "," + FormProp +
                ",\"documento\":{\"type\":\"string\",\"description\":\"Codigo o nombre de un documento de reglas existente\"}" +
                ",\"descripcion\":{\"type\":\"string\"},\"estado\":{\"type\":\"string\",\"enum\":[\"activa\",\"desarrollo\"]}", "nombre", "verbo")),
        new("parametros_regla",
            "REEMPLAZA los parametros de una regla (envia el objeto completo; valida contra el verbo).",
            Obj(ReglaProp + "," + ParamsProp, "regla", "parametros")),
        new("estado_regla",
            "activa = corre; desarrollo = solo en prueba manual; inactiva = nunca corre.",
            Obj(ReglaProp + ",\"estado\":{\"type\":\"string\",\"enum\":[\"activa\",\"desarrollo\",\"inactiva\"]}", "regla", "estado")),
        new("renombrar_regla",
            "Cambia el nombre de una regla (max 100).",
            Obj(ReglaProp + ",\"nombre\":{\"type\":\"string\"}", "regla", "nombre")),
        new("eliminar_regla",
            "Borra una regla y sus vinculos. Solo si nunca se ha ejecutado (si tiene historial, usa estado_regla inactiva). " +
            "Confirma con el usuario.",
            Obj(ReglaProp, "regla")),

        // ---------------- Reglas en un formulario ----------------
        new("reglas_de_formulario",
            "Todas las reglas de un formulario: las que corren AL ENVIAR y las de cada campo/boton (con sus parametros " +
            "propios del vinculo, si los tiene).",
            Obj(FormProp, "formulario")),
        new("regla_al_enviar",
            "Al ENVIAR el formulario se ejecuta este verbo (ej. CREAR_TAREA, NOTIFICAR, GENERAR_TAREAS_DESDE_TABLA, " +
            "COMPLETAR_PASO_FLUJO). Una regla por verbo y formulario: si ya existe, actualiza nombre y parametros. Corre " +
            "tambien en los enlaces publicos /f/.",
            Obj(FormProp + ",\"verbo\":{\"type\":\"string\"},\"nombre\":{\"type\":\"string\"}," + ParamsProp, "formulario", "verbo", "nombre")),
        new("quitar_regla_al_enviar",
            "Quita una regla del envio (y la borra si nunca corrio). 'regla' = nombre, id o verbo. Confirma con el usuario.",
            Obj(FormProp + "," + ReglaProp, "formulario", "regla")),
        new("regla_en_campo",
            "Vincula una regla existente a un CAMPO (corre al cambiar su valor) o a un BOTON (corre al pulsarlo; un " +
            "boton es un campo de tipo Button; agregalo con apply_form_spec). Crea la regla antes con crear_regla.",
            Obj(FormProp + "," + CampoProp + "," + ReglaProp, "formulario", "campo", "regla")),
        new("quitar_regla_de_campo",
            "Desvincula una regla de un campo o boton (la regla sigue existiendo). Confirma con el usuario.",
            Obj(FormProp + "," + CampoProp + "," + ReglaProp, "formulario", "campo", "regla")),
        new("parametros_regla_en_campo",
            "Parametros PROPIOS de este vinculo (sobrescriben los de la regla solo en este campo/formulario; util para " +
            "reusar una regla en varios formularios). Sin 'parametros' los quita.",
            Obj(FormProp + "," + CampoProp + "," + ReglaProp + "," + ParamsProp, "formulario", "campo", "regla")),
        new("condicion_entre_campos",
            "Regla de UI: si 'si_campo' cumple 'operador' ('valor'), entonces 'efecto' sobre 'campo' (ocultar, mostrar, " +
            "requerir u opcional). Se evalua mientras se llena. Para solo ocultar/mostrar tambien sirve visible_when_json.",
            Obj(FormProp + ",\"si_campo\":{\"type\":\"string\"},\"operador\":{\"type\":\"string\",\"enum\":[\"igual\",\"distinto\",\"vacio\",\"no_vacio\"]}," +
                "\"valor\":{\"type\":\"string\"},\"efecto\":{\"type\":\"string\",\"enum\":[\"ocultar\",\"mostrar\",\"requerir\",\"opcional\"]}," + CampoProp,
                "formulario", "si_campo", "operador", "efecto", "campo")),

        // ---------------- Opciones del formulario ----------------
        new("ocultar_barra_envio",
            "activo=true oculta Enviar/Imprimir/autoguardado cuando el formulario se llena dentro de la creacion de una tarea.",
            Obj(FormProp + "," + ActivoProp, "formulario", "activo")),
        new("formulario_reportable",
            "activo=true lo vuelve fuente de reportes (form:{codigo}) sin hacerlo modulo del menu.",
            Obj(FormProp + "," + ActivoProp, "formulario", "activo")),
        new("codigo_automatico_formulario",
            "Al crear un registro llena 'campo' con iniciales del asignado + consecutivo (ancho de 1 a 12). activo=false lo quita.",
            Obj(FormProp + "," + ActivoProp + "," + CampoProp + ",\"ancho\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":12}", "formulario", "activo")),
        new("registro_editable",
            "activo=true: el registro queda siempre editable (autoguardado, nunca se cierra por envio).",
            Obj(FormProp + "," + ActivoProp, "formulario", "activo")),
        new("cierre_automatico_formulario",
            "Al guardar, si 'campo' cumple 'operador' ('valor'), el registro se ENVIA y cierra solo (ej. al firmar). Sin " +
            "'campo' quita el cierre automatico.",
            Obj(FormProp + "," + CampoProp + ",\"operador\":{\"type\":\"string\",\"enum\":[\"no_vacio\",\"vacio\",\"igual\",\"distinto\",\"contiene\"]},\"valor\":{\"type\":\"string\"}", "formulario")),
        new("guardar_version_formulario",
            "Guarda una VERSION del formulario (estructura completa) para poder volver a ella. Hazlo antes de cambios grandes.",
            Obj(FormProp + ",\"nombre\":{\"type\":\"string\"}", "formulario")),
        new("listar_versiones_formulario",
            "Versiones guardadas del formulario (id, nombre, fecha).",
            Obj(FormProp, "formulario")),
        new("restaurar_version_formulario",
            "Vuelve el formulario a una version guardada ('version' = id de listar_versiones_formulario). Reemplaza la " +
            "estructura actual: guarda una version antes y confirma con el usuario.",
            Obj(FormProp + ",\"version\":{\"type\":\"string\"}", "formulario", "version")),
        new("listar_enlaces_formulario",
            "Enlaces publicos emitidos del formulario (create_share_link): vigencia, uso unico, usado, revocado.",
            Obj(FormProp, "formulario")),
        new("revocar_enlace_formulario",
            "Revoca un enlace publico ('enlace' = id de listar_enlaces_formulario): deja de abrir. Confirma con el usuario.",
            Obj(FormProp + ",\"enlace\":{\"type\":\"string\"}", "formulario", "enlace")),

        // ---------------- Propiedades de campo ----------------
        new("campo_bloqueado",
            "activo=true: el campo se ve pero NO se puede editar (solo lectura para todos).",
            Obj(FormProp + "," + CampoProp + "," + ActivoProp, "formulario", "campo", "activo")),
        new("campo_oculto",
            "activo=true: el campo existe y guarda valor pero no se muestra (util para calculos o datos que pone una regla).",
            Obj(FormProp + "," + CampoProp + "," + ActivoProp, "formulario", "campo", "activo")),
        new("valor_por_defecto_dinamico",
            "Valor inicial que se calcula al abrir: hoy, usuario_actual, entidad_actual, usuario_asignado; ninguno lo quita.",
            Obj(FormProp + "," + CampoProp + ",\"valor\":{\"type\":\"string\",\"enum\":[\"ninguno\",\"hoy\",\"usuario_actual\",\"entidad_actual\",\"usuario_asignado\"]}",
                "formulario", "campo", "valor")),
        new("permisos_campo_por_rol",
            "Por ROL del usuario: 'ocultar_a' = roles que no lo ven; 'solo_lectura_a' = roles que lo ven sin editar. Roles: " +
            "Owner, Admin, Supervisor, Advisor. Listas vacias = sin restriccion. Reemplaza lo anterior.",
            Obj(FormProp + "," + CampoProp +
                ",\"ocultar_a\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"Owner\",\"Admin\",\"Supervisor\",\"Advisor\"]}}" +
                ",\"solo_lectura_a\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"Owner\",\"Admin\",\"Supervisor\",\"Advisor\"]}}",
                "formulario", "campo")),
        new("subformulario_de_campo",
            "En un campo de tipo Subform: el formulario HIJO (maestro-detalle) que se llena dentro. Sin 'subformulario' lo quita.",
            Obj(FormProp + "," + CampoProp + ",\"subformulario\":{\"type\":\"string\",\"description\":\"Formulario hijo (id, codigo o titulo)\"}",
                "formulario", "campo")),
    };

    private async Task<McpToolResult> ExecuteFormConfigAsync(string tool, JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (!HasFormConfig) { return Err("La configuracion de reglas y formularios no esta disponible."); }
        var perms = _roles is null ? EffectivePermissions.UnrestrictedAccess()
            : await _roles.ResolveEffectivePermissionsAsync(actor.PlatformUserId, ct);
        var isRead = FormConfigReadOnly.Contains(tool);
        string? denied;
        if (RuleCatalogTools.Contains(tool))
        {
            var action = isRead ? PermissionAction.View
                : tool == "crear_regla" ? PermissionAction.Create
                : tool == "eliminar_regla" ? PermissionAction.Delete
                : PermissionAction.Edit;
            denied = perms.Can(RulesModuleKey, action) ? null
                : $"Tu usuario no tiene permiso para {(isRead ? "consultar" : "gestionar")} reglas (modulo Reglas).";
        }
        else
        {
            denied = perms.Can(FormModuleKey, isRead ? PermissionAction.View : PermissionAction.Edit) ? null
                : $"Tu usuario no tiene permiso para {(isRead ? "consultar" : "disenar")} formularios.";
        }
        if (denied is not null) { return Err(denied); }

        return tool switch
        {
            "listar_verbos_regla" => await ListVerbsAsync(args, ct),
            "listar_reglas" => await ListRulesAsync(args, ct),
            "ver_regla" => await ViewRuleAsync(args, ct),
            "historial_regla" => await RuleHistoryAsync(args, ct),
            "crear_regla" => await CreateRuleAsync(args, ct),
            "parametros_regla" => await SetRuleParamsAsync(args, ct),
            "estado_regla" => await SetRuleStatusAsync(args, ct),
            "renombrar_regla" => await RenameRuleAsync(args, ct),
            "eliminar_regla" => await DeleteRuleAsync(args, ct),
            "reglas_de_formulario" => await FormRulesAsync(args, ct),
            "regla_al_enviar" => await SubmitRuleAsync(args, ct),
            "quitar_regla_al_enviar" => await RemoveSubmitRuleAsync(args, ct),
            "regla_en_campo" => await LinkFieldRuleAsync(args, ct),
            "quitar_regla_de_campo" => await UnlinkFieldRuleAsync(args, ct),
            "parametros_regla_en_campo" => await FieldRuleOverrideAsync(args, ct),
            "condicion_entre_campos" => await FieldConditionAsync(args, ct),
            "ocultar_barra_envio" => await SetFormFlagAsync(args, "barra", ct),
            "formulario_reportable" => await SetFormFlagAsync(args, "reportable", ct),
            "codigo_automatico_formulario" => await SetAutoCodeAsync(args, ct),
            "registro_editable" => await SetKeepEditableAsync(args, ct),
            "cierre_automatico_formulario" => await SetCloseRuleAsync(args, ct),
            "guardar_version_formulario" => await SnapshotFormAsync(args, actor, ct),
            "listar_versiones_formulario" => await ListFormVersionsAsync(args, ct),
            "restaurar_version_formulario" => await RestoreFormVersionAsync(args, actor, ct),
            "listar_enlaces_formulario" => await ListFormLinksAsync(args, ct),
            "revocar_enlace_formulario" => await RevokeFormLinkAsync(args, ct),
            "campo_bloqueado" => await PatchFieldAsync(args, ct, (r, a) => (r with { IsLocked = Bool(a, "activo") == true }, null)),
            "campo_oculto" => await PatchFieldAsync(args, ct, (r, a) => (r with { IsHidden = Bool(a, "activo") == true }, null)),
            "valor_por_defecto_dinamico" => await PatchFieldAsync(args, ct, DefaultDynamicPatch),
            "permisos_campo_por_rol" => await PatchFieldAsync(args, ct, FieldRolesPatch),
            "subformulario_de_campo" => await SetSubformAsync(args, ct),
            _ => Err($"Herramienta desconocida: {tool}")
        };
    }

    // ===================== Catalogo de verbos y reglas =====================

    private async Task<McpToolResult> ListVerbsAsync(JsonElement args, CancellationToken ct)
    {
        var catalog = _rulesEngine!.GetVerbCatalog();
        var verb = Str(args, "verbo");
        if (verb is not null)
        {
            var d = catalog.FirstOrDefault(v => Same(v.VerbName, verb));
            if (d is null) { return Err($"No existe el verbo '{verb}'. Verbos: {string.Join(", ", catalog.Select(v => v.VerbName))}."); }
            return Ok(await VerbPayloadAsync(d, full: true, ct));
        }
        var q = Str(args, "buscar");
        var rows = new List<object>();
        foreach (var d in catalog.Where(v => q is null || v.VerbName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                             || v.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                             || v.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            rows.Add(await VerbPayloadAsync(d, full: false, ct));
        }
        return Ok(new { total = rows.Count, verbos = rows, siguiente = "Detalle de un verbo: listar_verbos_regla(verbo=<nombre>)." });
    }

    private async Task<object> VerbPayloadAsync(RuleVerbDescriptor d, bool full, CancellationToken ct)
    {
        var ps = new List<object>();
        foreach (var p in d.Params)
        {
            IReadOnlyList<RuleParamOption>? options = p.Options;
            if (full && options is null && p.OptionsSource is not null && _ruleOptions is not null)
            {
                try { options = await _ruleOptions.GetOptionsAsync(p.OptionsSource, ct); }
                catch (Exception) { options = null; }
            }
            ps.Add(new
            {
                nombre = p.Name,
                etiqueta = p.Label,
                tipo = ParamTypeEs(p.Type),
                obligatorio = p.Required,
                descripcion = full ? p.Description : null,
                opciones = options is { Count: > 0 } ? options.Take(60).Select(o => o.Value == o.Label ? o.Value : $"{o.Value} ({o.Label})") : null
            });
        }
        return new { verbo = d.VerbName, nombre = d.DisplayName, descripcion = d.Description, parametros = ps };
    }

    private static string ParamTypeEs(RuleParamType t) => t switch
    {
        RuleParamType.Number => "numero",
        RuleParamType.Boolean => "si_no",
        RuleParamType.FieldCode => "codigo_de_campo",
        RuleParamType.Json => "json",
        RuleParamType.Select => "opcion",
        RuleParamType.Mapping => "mapeo {destino: 'col:x' | 'field:x' | 'fixed:valor'}",
        RuleParamType.FieldCodeList => "lista_de_codigos_de_campo",
        RuleParamType.FlowNode => "nodo_de_flujo (BpmnElementId)",
        _ => "texto"
    };

    private async Task<McpToolResult> ListRulesAsync(JsonElement args, CancellationToken ct)
    {
        var q = Str(args, "buscar");
        var rows = (await _ruleDocs!.ListAllRulesAsync(false, ct))
            .Where(r => q is null || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.VerbName.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.DocumentName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(300)
            .Select(r => new { regla_id = r.Id, nombre = r.Name, verbo = r.VerbName, estado = RuleStatusEs(r.Status), documento = r.DocumentName })
            .ToList();
        return Ok(new { total = rows.Count, reglas = rows });
    }

    private async Task<McpToolResult> ViewRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (item, error) = await ResolveRuleItemAsync(Str(args, "regla"), ct);
        if (item is null) { return Err(error!); }
        var rule = await _ruleDocs!.GetRuleAsync(item.Id, ct);
        if (rule is null) { return Err("Regla no encontrada."); }
        var forms = await _ruleDocs.ListFormLinksAsync(rule.Id, ct);
        var nodes = await _ruleDocs.ListNodeLinksAsync(rule.Id, ct);
        return Ok(new
        {
            regla_id = rule.Id,
            nombre = rule.Name,
            descripcion = rule.Description,
            verbo = rule.VerbName,
            estado = RuleStatusEs(rule.Status),
            documento = item.DocumentName,
            documento_activo = await DocumentActiveAsync(rule.DocumentId, ct),
            parametros = ParseObject(rule.ParamsJson),
            en_campos = forms.Count > 0 ? forms.Select(f => new { formulario = f.FormTitle, codigo = f.FormCode, campo = f.FieldCode, etiqueta = f.QuestionLabel }) : null,
            en_nodos = nodes.Count > 0 ? nodes.Select(n => new { flujo = n.WorkflowName, nodo = n.NodeName ?? n.BpmnElementId, autonoma = n.IsAutonomous }) : null
        });
    }

    private async Task<McpToolResult> RuleHistoryAsync(JsonElement args, CancellationToken ct)
    {
        var (item, error) = await ResolveRuleItemAsync(Str(args, "regla"), ct);
        if (item is null) { return Err(error!); }
        var logs = await _ruleDocs!.ListExecutionLogsAsync(null, item.Id, Math.Clamp(Int(args, "limite") ?? 20, 1, 100), ct);
        return Ok(new
        {
            regla = item.Name,
            total = logs.Count,
            ejecuciones = logs.Select(l => new
            {
                fecha = l.CreatedAt,
                disparo = l.TriggerKind.ToString(),
                estado = l.Status.ToString(),
                registros = l.RecordsAffected,
                ms = l.DurationMs,
                error = l.ErrorMessage,
                por = l.ExecutedByName
            })
        });
    }

    private async Task<McpToolResult> CreateRuleAsync(JsonElement args, CancellationToken ct)
    {
        var name = Str(args, "nombre");
        var verbKey = Str(args, "verbo");
        if (name is null || verbKey is null) { return Err("'nombre' y 'verbo' son obligatorios."); }
        var descriptor = _rulesEngine!.FindVerb(verbKey);
        if (descriptor is null) { return Err($"No existe el verbo '{verbKey}'. Usa listar_verbos_regla."); }

        FormDefinitionDetailDto? form = null;
        if (Str(args, "formulario") is not null)
        {
            var (f, ferr) = await LoadFormDefAsync(args, ct);
            if (f is null) { return Err(ferr!); }
            form = f;
        }
        var (paramsJson, perr) = BuildRuleParams(args, descriptor, form);
        if (perr is not null) { return Err(perr); }
        (paramsJson, perr) = await ResolveRuleRefsAsync(paramsJson, ct);
        if (perr is not null) { return Err(perr); }

        Guid docId;
        string? docWarning = null;
        if (form is not null)
        {
            var d = await EnsureFormRulesDocumentAsync(form, ct);
            if (d is null) { return Err("No se pudo crear el documento de reglas del formulario."); }
            docId = d.Value;
        }
        else if (Str(args, "documento") is { } docKey)
        {
            var docs = await _ruleDocs!.ListDocumentsAsync(false, ct);
            var doc = docs.FirstOrDefault(x => Same(x.DocumentCode, docKey)) ?? docs.FirstOrDefault(x => Same(x.Name, docKey))
                      ?? SingleOrNull(docs.Where(x => x.Name.Contains(docKey, StringComparison.OrdinalIgnoreCase)).ToList());
            if (doc is null) { return Err($"No encontre el documento de reglas '{docKey}'. Documentos: {string.Join(" | ", docs.Take(30).Select(x => x.Name))}."); }
            docId = doc.Id;
            if (doc.Status != RuleStatus.Active) { docWarning = $"El documento '{doc.Name}' no esta ACTIVO: sus reglas no corren hasta activarlo en el modulo Reglas."; }
        }
        else
        {
            var d = await EnsureIaRulesDocumentAsync(ct);
            if (d is null) { return Err("No se pudo crear el documento 'Reglas creadas por IA'."); }
            docId = d.Value;
        }

        var status = Str(args, "estado") == "desarrollo" ? RuleStatus.Development : RuleStatus.Active;
        var res = await _ruleDocs!.CreateRuleAsync(docId, new SaveRuleRequest(Clip(name, 100), descriptor.VerbName,
            Str(args, "descripcion"), 0, paramsJson, status), ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo crear la regla."); }
        return Ok(new
        {
            ok = true,
            regla_id = res.Value.Id,
            nombre = res.Value.Name,
            verbo = res.Value.VerbName,
            estado = RuleStatusEs(res.Value.Status),
            aviso = docWarning,
            siguiente = "Vinculala con regla_en_campo (campo o boton) o agregar_regla_nodo (paso de un flujo)."
        });
    }

    private async Task<McpToolResult> SetRuleParamsAsync(JsonElement args, CancellationToken ct)
    {
        var (rule, error) = await LoadRuleAsync(args, ct);
        if (rule is null) { return Err(error!); }
        var descriptor = _rulesEngine!.FindVerb(rule.VerbName);
        if (descriptor is null) { return Err($"El verbo '{rule.VerbName}' de la regla ya no esta registrado."); }
        var (paramsJson, perr) = BuildRuleParams(args, descriptor, null);
        if (perr is not null) { return Err(perr); }
        (paramsJson, perr) = await ResolveRuleRefsAsync(paramsJson, ct);
        if (perr is not null) { return Err(perr); }
        var res = await _ruleDocs!.UpdateRuleAsync(rule.Id, new SaveRuleRequest(rule.Name, rule.VerbName, rule.Description,
            rule.SortOrder, paramsJson, rule.Status), ct);
        return res.IsOk ? Ok(new { ok = true, regla = rule.Name, parametros = ParseObject(paramsJson) }) : Err(res.Error ?? "No se pudo guardar.");
    }

    private async Task<McpToolResult> SetRuleStatusAsync(JsonElement args, CancellationToken ct)
    {
        var (rule, error) = await LoadRuleAsync(args, ct);
        if (rule is null) { return Err(error!); }
        RuleStatus? status = Str(args, "estado") switch
        {
            "activa" => RuleStatus.Active,
            "desarrollo" => RuleStatus.Development,
            "inactiva" => RuleStatus.Inactive,
            _ => null
        };
        if (status is null) { return Err("'estado' debe ser activa, desarrollo o inactiva."); }
        var res = await _ruleDocs!.UpdateRuleAsync(rule.Id, new SaveRuleRequest(rule.Name, rule.VerbName, rule.Description,
            rule.SortOrder, rule.ParamsJson, status.Value), ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo guardar."); }
        var docActive = await DocumentActiveAsync(rule.DocumentId, ct);
        return Ok(new
        {
            ok = true,
            regla = rule.Name,
            estado = RuleStatusEs(status.Value),
            aviso = status == RuleStatus.Active && !docActive ? "La regla esta activa pero su DOCUMENTO no: no correra hasta activar el documento en el modulo Reglas." : null
        });
    }

    private async Task<McpToolResult> RenameRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (rule, error) = await LoadRuleAsync(args, ct);
        if (rule is null) { return Err(error!); }
        var name = Str(args, "nombre");
        if (name is null) { return Err("'nombre' es obligatorio."); }
        var res = await _ruleDocs!.UpdateRuleAsync(rule.Id, new SaveRuleRequest(Clip(name, 100), rule.VerbName, rule.Description,
            rule.SortOrder, rule.ParamsJson, rule.Status), ct);
        return res.IsOk ? Ok(new { ok = true, regla_id = rule.Id, nombre = name }) : Err(res.Error ?? "No se pudo renombrar.");
    }

    private async Task<McpToolResult> DeleteRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (rule, error) = await LoadRuleAsync(args, ct);
        if (rule is null) { return Err(error!); }
        var res = await _ruleDocs!.DeleteRuleAsync(rule.Id, ct);
        return res.IsOk ? Ok(new { ok = true, eliminada = rule.Name })
            : Err((res.Error ?? "No se pudo borrar.") + " Si ya se ejecuto alguna vez, usa estado_regla con 'inactiva'.");
    }

    // ===================== Reglas en un formulario =====================

    private async Task<McpToolResult> FormRulesAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var submit = await _ruleDocs!.ListFormSubmitLinksAsync(form.Id, ct);
        var submitRows = new List<object>();
        foreach (var s in submit)
        {
            var r = await _ruleDocs.GetRuleAsync(s.RuleId, ct);
            submitRows.Add(new { regla = s.RuleName, verbo = r?.VerbName, estado = RuleStatusEs(s.Status), parametros = ParseObject(r?.ParamsJson) });
        }
        var fieldRows = new List<object>();
        foreach (var q in form.Questions)
        {
            var links = await _ruleDocs.ListQuestionLinksAsync(q.Id, ct);
            foreach (var l in links)
            {
                var ov = await _ruleDocs.GetFieldRuleOverrideAsync(l.Id, ct);
                fieldRows.Add(new
                {
                    campo = q.FieldCode,
                    etiqueta = q.Label,
                    es_boton = q.ControlType == FormControlType.Button ? true : (bool?)null,
                    regla = l.RuleName,
                    verbo = l.VerbName,
                    parametros_del_vinculo = ParseObject(ov)
                });
            }
        }
        return Ok(new
        {
            formulario = form.Title,
            codigo = form.Code,
            al_enviar = submitRows.Count > 0 ? submitRows : null,
            en_campos = fieldRows.Count > 0 ? fieldRows : null,
            nota = submitRows.Count == 0 && fieldRows.Count == 0 ? "El formulario no tiene reglas." : null
        });
    }

    private async Task<McpToolResult> SubmitRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var verbKey = Str(args, "verbo");
        var name = Str(args, "nombre");
        if (verbKey is null || name is null) { return Err("'verbo' y 'nombre' son obligatorios."); }
        var descriptor = _rulesEngine!.FindVerb(verbKey);
        if (descriptor is null) { return Err($"No existe el verbo '{verbKey}'. Usa listar_verbos_regla."); }
        var (paramsJson, perr) = BuildRuleParams(args, descriptor, form);
        if (perr is not null) { return Err(perr); }
        (paramsJson, perr) = await ResolveRuleRefsAsync(paramsJson, ct);
        if (perr is not null) { return Err(perr); }
        var missing = descriptor.Params.Where(p => p.Required && (ParseObject(paramsJson) is not JsonObject o || !o.ContainsKey(p.Name)))
            .Select(p => p.Name).ToList();
        if (missing.Count > 0) { return Err($"Faltan parametros obligatorios del verbo {descriptor.VerbName}: {string.Join(", ", missing)}."); }

        var res = await _ruleDocs!.EnsureFormSubmitVerbRuleAsync(form.Id, descriptor.VerbName, Clip(name, 100), paramsJson, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo crear la regla al enviar."); }
        return Ok(new
        {
            ok = true,
            formulario = form.Title,
            verbo = descriptor.VerbName,
            nombre = name,
            nota = "Corre al ENVIAR el formulario (tambien por enlace publico). Una regla por verbo: repetir actualiza esta.",
            aviso = form.Status != FormStatus.Active ? "El formulario no esta activo: activalo (activate) para usarlo." : null
        });
    }

    private async Task<McpToolResult> RemoveSubmitRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var key = Str(args, "regla");
        if (key is null) { return Err("'regla' es obligatoria."); }
        var links = await _ruleDocs!.ListFormSubmitLinksAsync(form.Id, ct);
        if (links.Count == 0) { return Err("El formulario no tiene reglas al enviar."); }
        FormSubmitRuleLinkDto? hit = Guid.TryParse(key, out var g)
            ? links.FirstOrDefault(l => l.Id == g || l.RuleId == g)
            : links.FirstOrDefault(l => Same(l.RuleName, key));
        if (hit is null)
        {
            foreach (var l in links)
            {
                var r = await _ruleDocs.GetRuleAsync(l.RuleId, ct);
                if (r is not null && Same(r.VerbName, key)) { hit = l; break; }
            }
        }
        if (hit is null) { return Err($"El formulario no tiene la regla al enviar '{key}'. Tiene: {string.Join(" | ", links.Select(l => l.RuleName))}."); }
        var res = await _ruleDocs.UnlinkFormSubmitAsync(hit.Id, ct);
        return res.IsOk ? Ok(new { ok = true, quitada = hit.RuleName }) : Err(res.Error ?? "No se pudo quitar.");
    }

    private async Task<McpToolResult> LinkFieldRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (form, q, error) = await LoadFieldAsync(args, ct);
        if (q is null) { return Err(error!); }
        var (item, rerr) = await ResolveRuleItemAsync(Str(args, "regla"), ct);
        if (item is null) { return Err(rerr!); }
        var current = await _ruleDocs!.ListQuestionLinksAsync(q.Id, ct);
        if (current.Any(l => l.RuleId == item.Id)) { return Err($"El campo '{q.FieldCode}' ya tiene la regla '{item.Name}'."); }
        var res = await _ruleDocs.LinkToQuestionAsync(item.Id, q.Id, current.Count, ct);
        if (!res.IsOk) { return Err(res.Error ?? "No se pudo vincular."); }
        return Ok(new
        {
            ok = true,
            formulario = form!.Title,
            campo = q.FieldCode,
            regla = item.Name,
            corre = q.ControlType == FormControlType.Button ? "al pulsar el boton" : "al cambiar el valor del campo",
            aviso = item.Status != RuleStatus.Active ? "La regla no esta ACTIVA: no correra (estado_regla)." : null
        });
    }

    private async Task<McpToolResult> UnlinkFieldRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (_, q, error) = await LoadFieldAsync(args, ct);
        if (q is null) { return Err(error!); }
        var (link, lerr) = await FieldRuleLinkAsync(q, Str(args, "regla"), ct);
        if (link is null) { return Err(lerr!); }
        var res = await _ruleDocs!.UnlinkQuestionAsync(link.Id, ct);
        return res.IsOk ? Ok(new { ok = true, campo = q.FieldCode, quitada = link.RuleName }) : Err(res.Error ?? "No se pudo desvincular.");
    }

    private async Task<McpToolResult> FieldRuleOverrideAsync(JsonElement args, CancellationToken ct)
    {
        var (form, q, error) = await LoadFieldAsync(args, ct);
        if (q is null) { return Err(error!); }
        var (link, lerr) = await FieldRuleLinkAsync(q, Str(args, "regla"), ct);
        if (link is null) { return Err(lerr!); }
        string? json = null;
        if (HasArg(args, "parametros"))
        {
            var descriptor = _rulesEngine!.FindVerb(link.VerbName);
            if (descriptor is null) { return Err($"El verbo '{link.VerbName}' ya no esta registrado."); }
            var (pj, perr) = BuildRuleParams(args, descriptor, form);
            if (perr is not null) { return Err(perr); }
            (pj, perr) = await ResolveRuleRefsAsync(pj, ct);
            if (perr is not null) { return Err(perr); }
            json = pj;
        }
        var res = await _ruleDocs!.SetFieldRuleOverrideAsync(link.Id, json, ct);
        return res.IsOk
            ? Ok(new { ok = true, campo = q.FieldCode, regla = link.RuleName, parametros_del_vinculo = ParseObject(json), nota = json is null ? "Sin parametros propios: usa los de la regla." : null })
            : Err(res.Error ?? "No se pudo guardar.");
    }

    private async Task<McpToolResult> FieldConditionAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var (src, serr) = ResolveQuestion(form, Str(args, "si_campo"), "si_campo");
        if (src is null) { return Err(serr!); }
        var (dst, derr) = ResolveQuestion(form, Str(args, "campo"), "campo");
        if (dst is null) { return Err(derr!); }
        var op = Str(args, "operador") switch
        {
            "igual" => "equals",
            "distinto" => "notEquals",
            "vacio" => "empty",
            "no_vacio" => "notEmpty",
            _ => null
        };
        if (op is null) { return Err("'operador' debe ser igual, distinto, vacio o no_vacio."); }
        var effect = Str(args, "efecto") switch
        {
            "ocultar" => "hide",
            "mostrar" => "show",
            "requerir" => "require",
            "opcional" => "optional",
            _ => null
        };
        if (effect is null) { return Err("'efecto' debe ser ocultar, mostrar, requerir u opcional."); }
        var value = Str(args, "valor");
        if (op is "equals" or "notEquals" && value is null) { return Err("Con igual/distinto indica 'valor'."); }

        var res = await _ruleDocs!.CreateFieldConditionRuleAsync(new CreateFieldConditionRequest(
            form.Id, src.Id, src.FieldCode, op, value, dst.FieldCode, effect), ct);
        return res.IsOk && res.Value is not null
            ? Ok(new { ok = true, regla = res.Value.RuleName, si_campo = src.FieldCode, operador = Str(args, "operador"), valor = value, efecto = Str(args, "efecto"), campo = dst.FieldCode })
            : Err(res.Error ?? "No se pudo crear la condicion.");
    }

    // ===================== Opciones del formulario =====================

    private async Task<McpToolResult> SetFormFlagAsync(JsonElement args, string which, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var on = Bool(args, "activo");
        if (on is null) { return Err("'activo' es obligatorio."); }
        var req = TransactionalOf(form) with
        {
            HideSubmitBar = which == "barra" ? on.Value : form.HideSubmitBar,
            IsReportable = which == "reportable" ? on.Value : form.IsReportable
        };
        var res = await _formDefs!.SetTransactionalAsync(form.Id, req, ct);
        return res.IsOk ? Ok(new { ok = true, formulario = form.Title, opcion = which == "barra" ? "ocultar_barra_envio" : "reportable", activo = on.Value })
            : Err(res.Error ?? "No se pudo guardar.");
    }

    private async Task<McpToolResult> SetAutoCodeAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var on = Bool(args, "activo");
        if (on is null) { return Err("'activo' es obligatorio."); }
        string? target = null;
        if (on.Value)
        {
            var (q, qerr) = ResolveQuestion(form, Str(args, "campo") ?? form.AutoCodeTargetFieldCode, "campo");
            if (q is null) { return Err(qerr ?? "Indica 'campo' (el campo que recibe el codigo)."); }
            target = q.FieldCode;
        }
        var req = TransactionalOf(form) with
        {
            AutoCodeEnabled = on.Value,
            AutoCodeTargetFieldCode = target,
            AutoCodePadWidth = Int(args, "ancho") ?? form.AutoCodePadWidth
        };
        var res = await _formDefs!.SetTransactionalAsync(form.Id, req, ct);
        return res.IsOk ? Ok(new { ok = true, formulario = form.Title, codigo_automatico = on.Value, campo = target, ancho = req.AutoCodePadWidth })
            : Err(res.Error ?? "No se pudo guardar.");
    }

    private async Task<McpToolResult> SetKeepEditableAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var on = Bool(args, "activo");
        if (on is null) { return Err("'activo' es obligatorio."); }
        JsonObject theme;
        try { theme = (string.IsNullOrWhiteSpace(form.ThemeJson) ? null : JsonNode.Parse(form.ThemeJson) as JsonObject) ?? new JsonObject { ["tema"] = "clasico" }; }
        catch (JsonException) { theme = new JsonObject { ["tema"] = "clasico" }; }
        if (on.Value) { theme["keepEditable"] = true; } else { theme.Remove("keepEditable"); }
        var onlyDefault = theme.Count == 1 && theme["tema"]?.ToString() == "clasico";
        var res = await _formDefs!.SetThemeAsync(form.Id, onlyDefault ? null : theme.ToJsonString(), ct);
        return res.IsOk ? Ok(new { ok = true, formulario = form.Title, registro_editable = on.Value }) : Err(res.Error ?? "No se pudo guardar.");
    }

    private async Task<McpToolResult> SetCloseRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        if (Str(args, "campo") is null)
        {
            var clear = await _formDefs!.SetCloseRuleAsync(form.Id, null, ct);
            return clear.IsOk ? Ok(new { ok = true, formulario = form.Title, cierre_automatico = (string?)null }) : Err(clear.Error ?? "No se pudo quitar.");
        }
        var (q, qerr) = ResolveQuestion(form, Str(args, "campo"), "campo");
        if (q is null) { return Err(qerr!); }
        var op = (Str(args, "operador") ?? "no_vacio") switch
        {
            "no_vacio" => "notEmpty",
            "vacio" => "empty",
            "igual" => "equals",
            "distinto" => "notEquals",
            "contiene" => "includes",
            _ => null
        };
        if (op is null) { return Err("'operador' debe ser no_vacio, vacio, igual, distinto o contiene."); }
        var value = Str(args, "valor");
        if (op is "equals" or "notEquals" or "includes" && value is null) { return Err("Con igual/distinto/contiene indica 'valor'."); }
        var json = new JsonObject { ["field"] = q.FieldCode, ["op"] = op };
        if (value is not null) { json["value"] = value; }
        var res = await _formDefs!.SetCloseRuleAsync(form.Id, json.ToJsonString(), ct);
        return res.IsOk
            ? Ok(new
            {
                ok = true,
                formulario = form.Title,
                cierra_cuando = new { campo = q.FieldCode, operador = Str(args, "operador") ?? "no_vacio", valor = value },
                aviso = form.IsTransactional ? null : "El cierre automatico aplica a formularios transaccionales (set_transactional)."
            })
            : Err(res.Error ?? "No se pudo guardar.");
    }

    private async Task<McpToolResult> SnapshotFormAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_formSnapshots is null) { return Err("El versionado de formularios no esta disponible."); }
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var label = Clip(Str(args, "nombre") ?? $"Version guardada por IA {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC", 150);
        var id = await _formSnapshots.SnapshotAsync(form.Id, label, FormSnapshotTrigger.Manual, null, actor.TenantUserId, ct);
        return id is null ? Err("No se pudo guardar la version.") : Ok(new { ok = true, formulario = form.Title, version_id = id, nombre = label });
    }

    private async Task<McpToolResult> ListFormVersionsAsync(JsonElement args, CancellationToken ct)
    {
        if (_formSnapshots is null) { return Err("El versionado de formularios no esta disponible."); }
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var list = await _formSnapshots.ListAsync(form.Id, ct);
        return Ok(new
        {
            formulario = form.Title,
            total = list.Count,
            versiones = list.Select(v => new { version_id = v.Id, nombre = v.Label, origen = v.Trigger == FormSnapshotTrigger.Manual ? "manual" : "antes_de_cambio_del_chat", fecha = v.CreatedAt })
        });
    }

    private async Task<McpToolResult> RestoreFormVersionAsync(JsonElement args, McpActor actor, CancellationToken ct)
    {
        if (_formSnapshots is null) { return Err("El versionado de formularios no esta disponible."); }
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var vid = Guid2(args, "version");
        if (vid is null) { return Err("'version' no es valido (ver listar_versiones_formulario)."); }
        var list = await _formSnapshots.ListAsync(form.Id, ct);
        var v = list.FirstOrDefault(x => x.Id == vid);
        if (v is null) { return Err("Esa version no es de este formulario (ver listar_versiones_formulario)."); }
        var res = await _formSnapshots.RestoreAsync(v.Id, actor.TenantUserId, ct);
        return res.IsOk ? Ok(new { ok = true, formulario = form.Title, restaurada = v.Label }) : Err(res.Error ?? "No se pudo restaurar.");
    }

    private async Task<McpToolResult> ListFormLinksAsync(JsonElement args, CancellationToken ct)
    {
        if (_formTokens is null) { return Err("Los enlaces de formularios no estan disponibles."); }
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var list = await _formTokens.ListAsync(form.Id, ct);
        var now = DateTimeOffset.UtcNow;
        return Ok(new
        {
            formulario = form.Title,
            total = list.Count,
            enlaces = list.Select(t => new
            {
                enlace_id = t.Id,
                referencia = t.Reference,
                vence = t.ExpiresAt,
                uso_unico = t.SingleUse,
                anonimo = t.AllowAnonymous,
                estado = t.RevokedAt is not null ? "revocado" : t.SingleUse && t.UsedAt is not null ? "usado" : t.ExpiresAt < now ? "vencido" : "vigente",
                creado = t.CreatedAt
            })
        });
    }

    private async Task<McpToolResult> RevokeFormLinkAsync(JsonElement args, CancellationToken ct)
    {
        if (_formTokens is null) { return Err("Los enlaces de formularios no estan disponibles."); }
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return Err(error!); }
        var id = Guid2(args, "enlace");
        if (id is null) { return Err("'enlace' no es valido (ver listar_enlaces_formulario)."); }
        var list = await _formTokens.ListAsync(form.Id, ct);
        if (list.All(t => t.Id != id)) { return Err("Ese enlace no es de este formulario."); }
        var res = await _formTokens.RevokeAsync(id.Value, ct);
        return res.IsOk ? Ok(new { ok = true, revocado = id }) : Err(res.Error ?? "No se pudo revocar.");
    }

    // ===================== Propiedades de campo (PATCH) =====================

    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement.Clone();

    private async Task<McpToolResult> PatchFieldAsync(JsonElement args, CancellationToken ct,
        Func<SaveFormQuestionRequest, JsonElement, (SaveFormQuestionRequest Req, string? Error)> patch)
    {
        var (form, q, error) = await LoadFieldAsync(args, ct);
        if (q is null) { return Err(error!); }
        var baseline = FormAuthoringToolset.BuildQuestionRequest(EmptyArgs, q);
        var (req, perr) = patch(baseline, args);
        if (perr is not null) { return Err(perr); }
        var res = await _formDefs!.UpdateQuestionAsync(q.Id, req, ct);
        if (!res.IsOk || res.Value is null) { return Err(res.Error ?? "No se pudo guardar el campo."); }
        var v = res.Value;
        return Ok(new
        {
            ok = true,
            formulario = form!.Title,
            campo = v.FieldCode,
            bloqueado = v.IsLocked,
            oculto = v.IsHidden,
            valor_por_defecto_dinamico = DefaultDynamicEs(v.DefaultDynamic),
            permisos_por_rol = ParseObject(v.FieldVisibilityJson),
            subformulario_id = v.SubformDefinitionId
        });
    }

    private static (SaveFormQuestionRequest, string?) DefaultDynamicPatch(SaveFormQuestionRequest r, JsonElement a)
    {
        FormDefaultDynamic? d = Str(a, "valor") switch
        {
            "ninguno" => FormDefaultDynamic.None,
            "hoy" => FormDefaultDynamic.Today,
            "usuario_actual" => FormDefaultDynamic.CurrentUser,
            "entidad_actual" => FormDefaultDynamic.CurrentEntidad,
            "usuario_asignado" => FormDefaultDynamic.AssignedUser,
            _ => null
        };
        return d is null
            ? (r, "'valor' debe ser ninguno, hoy, usuario_actual, entidad_actual o usuario_asignado.")
            : (r with { DefaultDynamic = d.Value }, null);
    }

    private static readonly string[] TenantRoleNames = { "Owner", "Admin", "Supervisor", "Advisor" };

    private static (SaveFormQuestionRequest, string?) FieldRolesPatch(SaveFormQuestionRequest r, JsonElement a)
    {
        List<string>? Roles(string key, out string? err)
        {
            err = null;
            var list = new List<string>();
            if (!a.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array) { return list; }
            foreach (var x in el.EnumerateArray())
            {
                var role = TenantRoleNames.FirstOrDefault(t => string.Equals(t, x.GetString(), StringComparison.OrdinalIgnoreCase));
                if (role is null) { err = $"Rol '{x}' no valido en '{key}'. Roles: {string.Join(", ", TenantRoleNames)}."; return null; }
                if (!list.Contains(role)) { list.Add(role); }
            }
            return list;
        }
        var hide = Roles("ocultar_a", out var e1);
        if (hide is null) { return (r, e1); }
        var ro = Roles("solo_lectura_a", out var e2);
        if (ro is null) { return (r, e2); }
        var json = hide.Count == 0 && ro.Count == 0 ? null
            : JsonSerializer.Serialize(new Dictionary<string, List<string>> { ["hide"] = hide, ["readonly"] = ro });
        return (r with { FieldVisibilityJson = json }, null);
    }

    private async Task<McpToolResult> SetSubformAsync(JsonElement args, CancellationToken ct)
    {
        var (_, q, error) = await LoadFieldAsync(args, ct);
        if (q is null) { return Err(error!); }
        if (q.ControlType != FormControlType.Subform)
        {
            return Err($"El campo '{q.FieldCode}' es de tipo {q.ControlType}: el subformulario solo aplica a campos Subform.");
        }
        Guid? child = null;
        if (Str(args, "subformulario") is { } key)
        {
            var (cid, cerr) = await ResolveFormDefAsync(key, ct);
            if (cid is null) { return Err(cerr!); }
            child = cid;
        }
        return await PatchFieldAsync(args, ct, (r, _) => (r with { SubformDefinitionId = child }, null));
    }

    // ===================== Helpers =====================

    private static SetFormTransactionalRequest TransactionalOf(FormDefinitionDetailDto f) => new(
        f.IsTransactional, f.IdentityMode, f.IdentitySourceFieldCode, f.CardLayout, f.IdentityPrefix, f.IdentityPadding,
        f.HideSubmitBar, f.IsReportable, f.AutoCodeEnabled, f.AutoCodeTargetFieldCode, f.AutoCodePadWidth);

    private async Task<(FormDefinitionDetailDto? Form, string? Error)> LoadFormDefAsync(JsonElement args, CancellationToken ct)
    {
        var (id, error) = await ResolveFormDefAsync(Str(args, "formulario"), ct);
        if (id is null) { return (null, error); }
        var form = await _formDefs!.GetAsync(id.Value, ct);
        return form is null ? (null, "Formulario no encontrado.") : (form, null);
    }

    private async Task<(FormDefinitionDetailDto? Form, FormQuestionDto? Question, string? Error)> LoadFieldAsync(JsonElement args, CancellationToken ct)
    {
        var (form, error) = await LoadFormDefAsync(args, ct);
        if (form is null) { return (null, null, error); }
        var (q, qerr) = ResolveQuestion(form, Str(args, "campo"), "campo");
        return q is null ? (form, null, qerr) : (form, q, null);
    }

    private static (FormQuestionDto? Question, string? Error) ResolveQuestion(FormDefinitionDetailDto form, string? key, string argName)
    {
        if (key is null) { return (null, $"'{argName}' es obligatorio (codigo del campo; ver get_form)."); }
        var qs = form.Questions;
        var hit = Guid.TryParse(key, out var g) ? qs.FirstOrDefault(q => q.Id == g)
            : qs.FirstOrDefault(q => Same(q.FieldCode, key)) ?? qs.FirstOrDefault(q => Same(q.Label, key))
              ?? SingleOrNull(qs.Where(q => q.Label.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        return hit is null
            ? (null, $"El formulario '{form.Title}' no tiene el campo '{key}'. Campos: {string.Join(", ", qs.Take(60).Select(q => q.FieldCode))}.")
            : (hit, null);
    }

    private async Task<(RuleListItemDto? Rule, string? Error)> ResolveRuleItemAsync(string? key, CancellationToken ct)
    {
        if (key is null) { return (null, "'regla' es obligatoria (id o nombre; ver listar_reglas)."); }
        var all = await _ruleDocs!.ListAllRulesAsync(false, ct);
        var hit = Guid.TryParse(key, out var g) ? all.FirstOrDefault(r => r.Id == g)
            : SingleOrNull(all.Where(r => Same(r.Name, key)).ToList())
              ?? SingleOrNull(all.Where(r => r.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        return hit is null
            ? (null, $"No encontre una unica regla '{key}' (puede haber varias con ese nombre: usa su id). Ver listar_reglas.")
            : (hit, null);
    }

    private async Task<(RuleDto? Rule, string? Error)> LoadRuleAsync(JsonElement args, CancellationToken ct)
    {
        var (item, error) = await ResolveRuleItemAsync(Str(args, "regla"), ct);
        if (item is null) { return (null, error); }
        var rule = await _ruleDocs!.GetRuleAsync(item.Id, ct);
        return rule is null ? (null, "Regla no encontrada.") : (rule, null);
    }

    private async Task<(QuestionRuleLinkDto? Link, string? Error)> FieldRuleLinkAsync(FormQuestionDto q, string? key, CancellationToken ct)
    {
        if (key is null) { return (null, "'regla' es obligatoria."); }
        var links = await _ruleDocs!.ListQuestionLinksAsync(q.Id, ct);
        if (links.Count == 0) { return (null, $"El campo '{q.FieldCode}' no tiene reglas."); }
        var hit = Guid.TryParse(key, out var g) ? links.FirstOrDefault(l => l.Id == g || l.RuleId == g)
            : links.FirstOrDefault(l => Same(l.RuleName, key))
              ?? SingleOrNull(links.Where(l => l.RuleName.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList());
        return hit is null
            ? (null, $"El campo '{q.FieldCode}' no tiene la regla '{key}'. Tiene: {string.Join(" | ", links.Select(l => l.RuleName))}.")
            : (hit, null);
    }

    /// <summary>
    /// Arma el ParamsJson desde 'parametros' validando contra el descriptor del verbo: nombres conocidos, tipos
    /// (numero, si/no, opcion de lista estatica) y, si hay formulario, que los codigos de campo existan. Los
    /// obligatorios los valida el servicio al guardar. Sin 'parametros' = null.
    /// </summary>
    private static (string? Json, string? Error) BuildRuleParams(JsonElement args, RuleVerbDescriptor d, FormDefinitionDetailDto? form)
    {
        if (!args.TryGetProperty("parametros", out var p) || p.ValueKind == JsonValueKind.Null) { return (null, null); }
        if (p.ValueKind != JsonValueKind.Object) { return (null, "'parametros' debe ser un objeto {nombre: valor}."); }
        var byName = d.Params.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var codes = form?.Questions.Select(q => q.FieldCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in p.EnumerateObject())
        {
            if (!byName.TryGetValue(prop.Name, out var desc))
            {
                return (null, $"El verbo {d.VerbName} no tiene el parametro '{prop.Name}'. Parametros: {string.Join(", ", d.Params.Select(x => x.Name))}.");
            }
            var v = prop.Value;
            switch (desc.Type)
            {
                case RuleParamType.Number when !(v.ValueKind == JsonValueKind.Number || (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))):
                    return (null, $"'{prop.Name}' debe ser un numero.");
                case RuleParamType.Boolean when !(v.ValueKind is JsonValueKind.True or JsonValueKind.False || (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out _))):
                    return (null, $"'{prop.Name}' debe ser true o false.");
                case RuleParamType.Select when desc.Options is { Count: > 0 } opts && desc.OptionsSource is null
                                               && !opts.Any(o => string.Equals(o.Value, v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText(), StringComparison.OrdinalIgnoreCase)):
                    return (null, $"'{prop.Name}' debe ser una de: {string.Join(", ", opts.Select(o => o.Value))}.");
                case RuleParamType.FieldCode when codes is not null && v.ValueKind == JsonValueKind.String
                                                  && !string.IsNullOrWhiteSpace(v.GetString()) && !codes.Contains(v.GetString()!):
                    return (null, $"'{prop.Name}': el formulario no tiene el campo '{v.GetString()}'.");
            }
        }
        return (p.GetRawText(), null);
    }

    /// <summary>
    /// Los verbos guardan REFERENCIAS por id (Guid) que una IA no tiene a mano. Si el valor de estos parametros no es
    /// un Guid se traduce: subcategoriaId = codigo o nombre del concepto (listar_conceptos), activityTypeId = nombre
    /// del tipo de actividad (list_activity_types), assigneeUserId = correo del usuario (listar_usuarios). Tenant-scoped
    /// por el filtro global. Si no se encuentra, error claro (nunca se guarda una referencia rota).
    /// </summary>
    private async Task<(string? Json, string? Error)> ResolveRuleRefsAsync(string? json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject o) { return (json, null); }
        var changed = false;
        foreach (var key in new[] { "subcategoriaId", "activityTypeId", "assigneeUserId" })
        {
            if (!o.TryGetPropertyValue(key, out var node) || node is null) { continue; }
            var raw = node.ToString().Trim();
            if (raw.Length == 0 || Guid.TryParse(raw, out _)) { continue; }
            var low = raw.ToLowerInvariant();
            Guid? id = key switch
            {
                "subcategoriaId" => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    _db.ActividadSubcategorias.Where(s => !s.IsArchived && (s.Codigo.ToLower() == low || s.Nombre.ToLower() == low)).Select(s => (Guid?)s.Id), ct),
                "activityTypeId" => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    _db.ActivityTypes.Where(a => a.Name.ToLower() == low).Select(a => (Guid?)a.Id), ct),
                _ => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    _db.TenantUsers.Where(u => u.Email.ToLower() == low).Select(u => (Guid?)u.Id), ct)
            };
            if (id is null)
            {
                return (null, key switch
                {
                    "subcategoriaId" => $"No encontre el concepto '{raw}' (usa su codigo o nombre de listar_conceptos).",
                    "activityTypeId" => $"No encontre el tipo de actividad '{raw}' (ver list_activity_types).",
                    _ => $"No encontre el usuario '{raw}' (usa su correo de listar_usuarios)."
                });
            }
            o[key] = id.Value.ToString();
            changed = true;
        }
        return (changed ? o.ToJsonString() : json, null);
    }

    private async Task<Guid?> EnsureFormRulesDocumentAsync(FormDefinitionDetailDto form, CancellationToken ct)
    {
        // Mismo documento que usa el constructor (FRMRULES-{codigo}, activo): las reglas del formulario viven juntas.
        var code = $"FRMRULES-{form.Code}";
        var docs = await _ruleDocs!.ListDocumentsAsync(true, ct);
        var doc = docs.FirstOrDefault(d => d.DocumentCode == code);
        if (doc is not null) { return doc.Id; }
        var created = await _ruleDocs.CreateDocumentAsync(new SaveRuleDocumentRequest(code, $"Reglas de formulario - {form.Title}",
            "Formularios", "Reglas creadas desde el constructor de formularios.", RuleStatus.Active), ct);
        return created.IsOk ? created.Value?.Id : null;
    }

    private async Task<Guid?> EnsureIaRulesDocumentAsync(CancellationToken ct)
    {
        const string code = "IA-MCP";
        var docs = await _ruleDocs!.ListDocumentsAsync(true, ct);
        var doc = docs.FirstOrDefault(d => d.DocumentCode == code);
        if (doc is not null) { return doc.Id; }
        var created = await _ruleDocs.CreateDocumentAsync(new SaveRuleDocumentRequest(code, "Reglas creadas por IA", "IA",
            "Reglas creadas por una IA conectada al MCP (sin formulario).", RuleStatus.Active), ct);
        return created.IsOk ? created.Value?.Id : null;
    }

    private async Task<bool> DocumentActiveAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await _ruleDocs!.GetDocumentAsync(documentId, ct);
        return doc is { Status: RuleStatus.Active, IsArchived: false };
    }

    private static JsonNode? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) { return null; }
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return json; }
    }

    private static string RuleStatusEs(RuleStatus s) => s switch
    {
        RuleStatus.Active => "activa",
        RuleStatus.Development => "desarrollo",
        _ => "inactiva"
    };

    private static string DefaultDynamicEs(FormDefaultDynamic d) => d switch
    {
        FormDefaultDynamic.Today => "hoy",
        FormDefaultDynamic.CurrentUser => "usuario_actual",
        FormDefaultDynamic.CurrentEntidad => "entidad_actual",
        FormDefaultDynamic.AssignedUser => "usuario_asignado",
        _ => "ninguno"
    };
}
