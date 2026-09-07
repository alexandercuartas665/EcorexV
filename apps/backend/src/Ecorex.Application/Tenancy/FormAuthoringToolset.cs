using System.Text.Json;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Directorio;
using Ecorex.Application.Forms;
using Ecorex.Application.MenuConfig;
using Ecorex.Application.Rules;
using Ecorex.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ecorex.Application.Tenancy;

/// <summary>
/// Herramienta (function calling / "MCP") de AUTORIA DE FORMULARIOS: permite a un agente de IA (o a un
/// cliente MCP via /api/mgmt/agent/tools) construir, sin SQL, TODO lo que hoy se hace a mano: formularios
/// (contenedores + preguntas, grillas, lookup/resolve, calc/format), transaccionalidad y consecutivo,
/// plantillas de impresion + su boton, enlaces publicos /f/{token}, promocion a modulo /m/{code} y
/// registros de prueba. Delega en los servicios de aplicacion existentes (NADA de SQL crudo) y respeta el
/// aislamiento por tenant (query filter global). La auditoria y el gate de auth/tenant los pone el grupo
/// /api/mgmt; aqui solo se orquestan las llamadas y se devuelven errores ESTRUCTURADOS (no excepciones).
/// </summary>
public interface IFormAuthoringToolset : IAgentToolset
{
    /// <summary>Nombres de herramientas de SOLO LECTURA (no mutan): el host no las audita.</summary>
    IReadOnlySet<string> ReadOnlyTools { get; }
}

public sealed class FormAuthoringToolset : IFormAuthoringToolset
{
    private readonly IFormDefinitionService _forms;
    private readonly IFormTokenService _tokens;
    private readonly IFormResponseService _responses;
    private readonly IQuoteTemplateService _templates;
    private readonly IRuleDocumentService _rules;
    private readonly IDataContainerService _containers;
    private readonly ITerceroFieldService _terceroFields;
    private readonly IMenuConfigService _menus;
    private readonly IApplicationDbContext _db;

    public FormAuthoringToolset(
        IFormDefinitionService forms, IFormTokenService tokens, IFormResponseService responses,
        IQuoteTemplateService templates, IRuleDocumentService rules, IDataContainerService containers,
        ITerceroFieldService terceroFields, IMenuConfigService menus, IApplicationDbContext db)
    {
        _forms = forms;
        _tokens = tokens;
        _responses = responses;
        _templates = templates;
        _rules = rules;
        _containers = containers;
        _terceroFields = terceroFields;
        _menus = menus;
        _db = db;
    }

    public string GroupKey => "form-authoring";
    public string GroupLabel => "Autoria de formularios";

    private static readonly JsonSerializerOptions JsonOut = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlySet<string> ReadOnlyTools { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "describe_components", "list_tenants", "list_forms", "get_form", "list_templates",
        "list_data_containers", "list_tercero_fields", "list_menu_views", "list_menu_nodes",
        "export_form", "get_render_urls"
    };

    public IReadOnlyList<AiToolSpec> GetSpecs() => Specs;

    private static readonly AiToolSpec[] Specs =
    {
        // ---------- Descubrimiento ----------
        new("describe_components",
            "Catalogo AUTO-DESCRIPTIVO (legible por maquina) de los componentes del constructor de formularios: " +
            "enums (tipos de control, origenes de datos, modos de identidad, presentaciones, agregados, tipos de " +
            "contenedor, layouts de tarjeta), que controles aceptan OptionsJson / soportan lookup / resolve / calc / " +
            "format, el esquema de columnas de una grilla (con sub-esquemas lookup y resolve), las claves de lookup a " +
            "nivel de campo, los marcadores de plantilla de impresion y los verbos/formatos de impresion. Llamala " +
            "PRIMERO para construir sin leer codigo.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_tenants",
            "Lista los tenants (id, nombre, estado). Solo para descubrimiento; para operar debes pasar ?tenant={id} en la URL.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_forms",
            "Lista los formularios del tenant: id, code, titulo, estado, #preguntas, Version (token de concurrencia optimista), si es modulo/transaccional.",
            """{"type":"object","properties":{"include_archived":{"type":"boolean","description":"Incluir archivados (por defecto false)"}},"additionalProperties":false}"""),
        new("get_form",
            "Devuelve la definicion COMPLETA de un formulario (cabecera + contenedores + preguntas) incluyendo su Version, para hacer updates con concurrencia optimista.",
            """{"type":"object","properties":{"form_id":{"type":"string","description":"Id (GUID) del formulario"}},"required":["form_id"],"additionalProperties":false}"""),
        new("list_templates",
            "Lista las plantillas de impresion del tenant (id, nombre, si es la predeterminada, si se envia como imagen).",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_data_containers",
            "Lista los contenedores de datos del tenant (id, nombre). Usa el id como 'sourceRef' de un lookup/resolve con source=DataContainer.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_tercero_fields",
            "Lista los campos disponibles de Tercero (Directorio) para autofill de un lookup con source=Tercero: base (nombre, identificacion, ciudad, email, telefono, vendedor, sector, cargo, estado) + los campos de ficha configurados.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_menu_views",
            "Lista las vistas de menu del tenant (id, nombre, si es la predeterminada). Usa el id como 'menu_view_id' de set_module para publicar el formulario como modulo bajo esa vista.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_menu_nodes",
            "Lista los nodos de una vista de menu (id, nombre, tipo, padre) aplanados. Un modulo solo cuelga de un nodo tipo Section o Subgroup: usa el id de uno de esos como 'parent_node_id' de set_module.",
            """{"type":"object","properties":{"view_id":{"type":"string","description":"Id de la vista de menu (de list_menu_views)"}},"required":["view_id"],"additionalProperties":false}"""),

        // ---------- Autoria de formulario ----------
        new("create_form",
            "Crea un formulario NUEVO (cabecera). Devuelve su id, code y Version. 'code' debe ser unico en el tenant.",
            """{"type":"object","properties":{"code":{"type":"string","description":"Codigo unico (ej. ENC-CLIMA)"},"title":{"type":"string"},"description":{"type":"string"}},"required":["code","title"],"additionalProperties":false}"""),
        new("import_form",
            "Crea un formulario COMPLETO de una vez desde un JSON exportado (el mismo formato de export_form). Genera un code unico; nunca pisa otro.",
            """{"type":"object","properties":{"json":{"type":"string","description":"JSON exportado del formulario"}},"required":["json"],"additionalProperties":false}"""),
        new("export_form",
            "Serializa un formulario completo (cabecera + contenedores + preguntas) a un JSON portable para respaldarlo o clonarlo con import_form.",
            """{"type":"object","properties":{"form_id":{"type":"string"}},"required":["form_id"],"additionalProperties":false}"""),
        new("update_form_header",
            "Actualiza titulo/descripcion del formulario. Requiere 'version' (concurrencia optimista) que entrega get_form.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"title":{"type":"string"},"description":{"type":"string"},"version":{"type":"integer"}},"required":["form_id","title","version"],"additionalProperties":false}"""),
        new("add_container",
            "Agrega un contenedor (seccion/tabla/fila/columna/tabs/modal) al formulario. container_type: Segment,Table,Row,Col,Section,Tabs,Modal. width en la rejilla de 12.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"name":{"type":"string"},"container_type":{"type":"string"},"parent_id":{"type":"string","description":"Contenedor padre (opcional; raiz si se omite)"},"width":{"type":"integer"},"inline_labels":{"type":"boolean"},"style":{"type":"string"}},"required":["form_id","name"],"additionalProperties":false}"""),
        new("update_container",
            "Actualiza un contenedor por su id (nombre/tipo/ancho/estilo).",
            """{"type":"object","properties":{"container_id":{"type":"string"},"name":{"type":"string"},"container_type":{"type":"string"},"width":{"type":"integer"},"inline_labels":{"type":"boolean"},"style":{"type":"string"},"parent_id":{"type":"string"}},"required":["container_id","name"],"additionalProperties":false}"""),
        new("move_container",
            "Mueve un contenedor a otro padre (o a la raiz con parent_id vacio) en la posicion 'index'.",
            """{"type":"object","properties":{"container_id":{"type":"string"},"parent_id":{"type":"string"},"index":{"type":"integer"}},"required":["container_id","index"],"additionalProperties":false}"""),
        new("add_question",
            "Agrega una pregunta/campo al formulario. control_type: Text,TextArea,Heading,Select,MultiCheck,Radio,Toggle,Number,Date,Time,DateTime,Literal,Button,GridDetail,Subform,Geografia,Html,Paragraph,Divider,Spacer,... " +
            "OptionsJson: para Select/Radio/MultiCheck es un arreglo de opciones; para GridDetail es el arreglo de columnas (ver describe_components). " +
            "Lookup a nivel de campo: source_kind (Options|DataContainer|Tercero|Item)+source_ref+display_field+value_field+filter_json+autofill_map_json+presentation. " +
            "Calculo: calc_expression + aggregate. Formato de salida: 'format'. field_code debe ser unico en el formulario.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"container_id":{"type":"string","description":"Contenedor destino (opcional; raiz si se omite)"},"field_code":{"type":"string"},"label":{"type":"string"},"control_type":{"type":"string"},"required":{"type":"boolean"},"options_json":{"type":"string","description":"JSON de opciones (Select/Radio/MultiCheck) o de columnas (GridDetail)"},"help_text":{"type":"string"},"placeholder_text":{"type":"string"},"default_value":{"type":"string"},"width":{"type":"integer"},"source_kind":{"type":"string"},"source_ref":{"type":"string"},"display_field":{"type":"string"},"value_field":{"type":"string"},"filter_json":{"type":"string"},"autofill_map_json":{"type":"string"},"presentation":{"type":"string","description":"Autocomplete|Dropdown|Modal"},"calc_expression":{"type":"string"},"aggregate":{"type":"string","description":"None|Sum|Count|Avg|Min|Max"},"format":{"type":"string"},"validation_json":{"type":"string"}},"required":["form_id","field_code","label","control_type"],"additionalProperties":false}"""),
        new("update_question",
            "Actualiza una pregunta por su id. Mismos campos que add_question (los que omitas vuelven a su valor por defecto del request).",
            """{"type":"object","properties":{"question_id":{"type":"string"},"container_id":{"type":"string"},"field_code":{"type":"string"},"label":{"type":"string"},"control_type":{"type":"string"},"required":{"type":"boolean"},"options_json":{"type":"string"},"help_text":{"type":"string"},"placeholder_text":{"type":"string"},"default_value":{"type":"string"},"width":{"type":"integer"},"source_kind":{"type":"string"},"source_ref":{"type":"string"},"display_field":{"type":"string"},"value_field":{"type":"string"},"filter_json":{"type":"string"},"autofill_map_json":{"type":"string"},"presentation":{"type":"string"},"calc_expression":{"type":"string"},"aggregate":{"type":"string"},"format":{"type":"string"},"validation_json":{"type":"string"}},"required":["question_id","field_code","label","control_type"],"additionalProperties":false}"""),
        new("move_question",
            "Mueve una pregunta a otro contenedor (o a la raiz con container_id vacio) en la posicion 'index'.",
            """{"type":"object","properties":{"question_id":{"type":"string"},"container_id":{"type":"string"},"index":{"type":"integer"}},"required":["question_id","index"],"additionalProperties":false}"""),
        new("delete_question",
            "Elimina una pregunta por su id.",
            """{"type":"object","properties":{"question_id":{"type":"string"}},"required":["question_id"],"additionalProperties":false}"""),
        new("set_transactional",
            "Configura la transaccionalidad: is_transactional + identity_mode (None|NaturalKey|Sequence). Para Sequence, identity_prefix + identity_padding definen el numero (ej. COT-000001). Para NaturalKey, identity_source_field_code apunta al campo clave. card_layout: Normal|Ancho|Completo.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"is_transactional":{"type":"boolean"},"identity_mode":{"type":"string"},"identity_source_field_code":{"type":"string"},"card_layout":{"type":"string"},"identity_prefix":{"type":"string"},"identity_padding":{"type":"integer"}},"required":["form_id","is_transactional","identity_mode"],"additionalProperties":false}"""),
        new("set_sequence_next",
            "Fija el proximo numero del consecutivo (Sequence). Anti-colision: no permite bajarlo por debajo de uno ya emitido.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"next":{"type":"integer"}},"required":["form_id","next"],"additionalProperties":false}"""),
        new("set_module",
            "Promueve (o retira) el formulario como MODULO del menu, publicandolo en /m/{code}. menu_view_id + parent_node_id ubican el nodo; icon, list_columns y filter_fields configuran su listado.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"is_module":{"type":"boolean"},"menu_view_id":{"type":"string"},"parent_node_id":{"type":"string"},"icon":{"type":"string"},"menu_label":{"type":"string"},"list_columns":{"type":"array","items":{"type":"string"}},"filter_fields":{"type":"array","items":{"type":"string"}}},"required":["form_id","is_module"],"additionalProperties":false}"""),
        new("set_custom_css",
            "Guarda el CSS personalizado de todo el formulario (pestana Estilos del disenador).",
            """{"type":"object","properties":{"form_id":{"type":"string"},"custom_css":{"type":"string"}},"required":["form_id"],"additionalProperties":false}"""),
        new("activate",
            "Activa el formulario (Draft/Inactive -> Active), validando su estructura. Empieza a aceptar respuestas.",
            """{"type":"object","properties":{"form_id":{"type":"string"}},"required":["form_id"],"additionalProperties":false}"""),
        new("deactivate",
            "Desactiva el formulario (Active -> Inactive): deja de aceptar respuestas nuevas.",
            """{"type":"object","properties":{"form_id":{"type":"string"}},"required":["form_id"],"additionalProperties":false}"""),
        new("archive",
            "Archiva o desarchiva el formulario.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"archived":{"type":"boolean"}},"required":["form_id","archived"],"additionalProperties":false}"""),

        // ---------- Plantillas de impresion ----------
        new("create_template",
            "Crea una plantilla de impresion HTML (usa marcadores {{campo.codigo}}, {{#tabla.x}}...{{/tabla.x}}, {{numero}},{{tarea}},{{fecha}},{{empresa}}, y {{barcode:numero|tarea|campo.x}}; ver describe_components). send_as_image=true la envia como imagen en vez de PDF.",
            """{"type":"object","properties":{"name":{"type":"string"},"html":{"type":"string"},"send_as_image":{"type":"boolean"}},"required":["name","html"],"additionalProperties":false}"""),
        new("update_template",
            "Actualiza una plantilla de impresion por su id.",
            """{"type":"object","properties":{"template_id":{"type":"string"},"name":{"type":"string"},"html":{"type":"string"},"send_as_image":{"type":"boolean"}},"required":["template_id","name","html"],"additionalProperties":false}"""),
        new("set_default_template",
            "Marca una plantilla como la predeterminada del tenant.",
            """{"type":"object","properties":{"template_id":{"type":"string"}},"required":["template_id"],"additionalProperties":false}"""),
        new("wire_print_button",
            "En UNA operacion: crea (o reusa) un documento de reglas, una regla IMPRIMIR_PLANTILLA {template,format}, una pregunta tipo Button y los enlaza, dejando el boton de imprimir en el formulario. format: print|pdf|img.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"template_name":{"type":"string"},"format":{"type":"string","description":"print|pdf|img"},"button_label":{"type":"string"},"container_id":{"type":"string","description":"Contenedor donde poner el boton (opcional)"},"field_code":{"type":"string","description":"Codigo del campo Button (opcional; se genera si se omite)"}},"required":["form_id","template_name"],"additionalProperties":false}"""),

        // ---------- Enlaces compartidos ----------
        new("create_share_link",
            "Emite un enlace publico /f/{token} para llenar el formulario. Devuelve el token EN CLARO una sola vez (en BD va solo el hash). expiration_hours, single_use, allow_anonymous, reference opcionales.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"expiration_hours":{"type":"integer"},"single_use":{"type":"boolean"},"allow_anonymous":{"type":"boolean"},"reference":{"type":"string"}},"required":["form_id"],"additionalProperties":false}"""),

        // ---------- Registros (prueba de punta a punta) ----------
        new("create_record",
            "Crea un registro (respuesta) del formulario con 'data' (mapa field_code -> valor). submit=true valida y confirma (asigna record_number si es transaccional); submit=false lo deja en borrador.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"data":{"type":"object","description":"Mapa field_code -> valor (escalar) o {value,type}","additionalProperties":true},"submit":{"type":"boolean"},"reference":{"type":"string"}},"required":["form_id","data"],"additionalProperties":false}"""),
        new("get_render_urls",
            "Devuelve las URLs de vista/pdf/img de un registro (para verificar la impresion). template_id opcional (usa la predeterminada si se omite).",
            """{"type":"object","properties":{"response_id":{"type":"string"},"template_id":{"type":"string"}},"required":["response_id"],"additionalProperties":false}"""),
    };

    public async Task<AgentToolResult> ExecuteAsync(string toolName, string argumentsJson, Guid actorUserId, bool autonomous, CancellationToken cancellationToken = default)
    {
        JsonElement args;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            args = doc.RootElement.Clone();
        }
        catch { return Err("Los argumentos no son un JSON valido."); }

        try
        {
            return toolName switch
            {
                "describe_components" => Ok(DescribeComponents()),
                "list_tenants" => await ListTenantsAsync(cancellationToken),
                "list_forms" => await ListFormsAsync(args, cancellationToken),
                "get_form" => await GetFormAsync(args, cancellationToken),
                "list_templates" => await ListTemplatesAsync(cancellationToken),
                "list_data_containers" => await ListDataContainersAsync(cancellationToken),
                "list_tercero_fields" => await ListTerceroFieldsAsync(cancellationToken),
                "list_menu_views" => await ListMenuViewsAsync(cancellationToken),
                "list_menu_nodes" => await ListMenuNodesAsync(args, cancellationToken),
                "create_form" => await CreateFormAsync(args, cancellationToken),
                "import_form" => await ImportFormAsync(args, cancellationToken),
                "export_form" => await ExportFormAsync(args, cancellationToken),
                "update_form_header" => await UpdateHeaderAsync(args, cancellationToken),
                "add_container" => await AddContainerAsync(args, cancellationToken),
                "update_container" => await UpdateContainerAsync(args, cancellationToken),
                "move_container" => await MoveContainerAsync(args, cancellationToken),
                "add_question" => await AddQuestionAsync(args, cancellationToken),
                "update_question" => await UpdateQuestionAsync(args, cancellationToken),
                "move_question" => await MoveQuestionAsync(args, cancellationToken),
                "delete_question" => await DeleteQuestionAsync(args, cancellationToken),
                "set_transactional" => await SetTransactionalAsync(args, cancellationToken),
                "set_sequence_next" => await SetSequenceNextAsync(args, cancellationToken),
                "set_module" => await SetModuleAsync(args, cancellationToken),
                "set_custom_css" => await SetCustomCssAsync(args, cancellationToken),
                "activate" => await ActivateAsync(args, cancellationToken),
                "deactivate" => await DeactivateAsync(args, cancellationToken),
                "archive" => await ArchiveAsync(args, cancellationToken),
                "create_template" => await CreateTemplateAsync(args, actorUserId, cancellationToken),
                "update_template" => await UpdateTemplateAsync(args, actorUserId, cancellationToken),
                "set_default_template" => await SetDefaultTemplateAsync(args, actorUserId, cancellationToken),
                "wire_print_button" => await WirePrintButtonAsync(args, cancellationToken),
                "create_share_link" => await CreateShareLinkAsync(args, cancellationToken),
                "create_record" => await CreateRecordAsync(args, cancellationToken),
                "get_render_urls" => GetRenderUrls(args),
                _ => Err($"Herramienta desconocida: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return Err($"Error ejecutando '{toolName}': {ex.Message}");
        }
    }

    // ================= Descubrimiento =================

    private static object DescribeComponents() => new
    {
        ok = true,
        enums = new
        {
            control_types = Names<FormControlType>(),
            source_kinds = Names<FormSourceKind>(),
            identity_modes = Names<FormIdentityMode>(),
            field_presentations = Names<FormFieldPresentation>(),
            aggregates = Names<FormAggregate>(),
            container_types = Names<FormContainerType>(),
            card_layouts = Names<FormCardLayout>()
        },
        control_capabilities = new
        {
            accept_options_json = new[] { "Select", "Radio", "MultiCheck", "GridDetail" },
            support_field_lookup = new[] { "Select", "Radio", "MultiCheck" },
            support_calc = new[] { "Number", "Text" },
            support_format = new[] { "Number", "Text", "Date", "DateTime" },
            no_capture = new[] { "Heading", "Literal", "Paragraph", "Divider", "Spacer", "Html", "Button" },
            grid_control = "GridDetail",
            master_detail_control = "Subform",
            geografia_control = "Geografia"
        },
        grid_column_schema = new
        {
            note = "OptionsJson de un GridDetail = arreglo de columnas. Claves por columna:",
            keys = new
            {
                id = "clave estable de la columna",
                label = "titulo visible",
                type = "text|number|date|select|lookup|resolve|calc",
                width = "ancho relativo",
                format = "formato de salida (ej. moneda)",
                @default = "valor por defecto",
                options = "opciones (columna select)",
                lookup = new
                {
                    source = "Options|DataContainer|Tercero|Item",
                    sourceRef = "id del contenedor/fuente",
                    displayField = "campo a mostrar",
                    valueField = "campo a guardar",
                    filter = "filtro",
                    autofill = "{campoOrigen: idColDestino} autocompletar otras columnas",
                    presentation = "Autocomplete|Dropdown|Modal",
                    subLabel = "campo secundario"
                },
                resolve = new
                {
                    note = "VLOOKUP multi-clave, columna de solo lectura",
                    source = "Options|DataContainer|Tercero|Item",
                    sourceRef = "id de la fuente",
                    @return = "campo a devolver",
                    match = "{ColFuente: \"{campoFormulario}\"} claves de cruce",
                    when = "condiciones opcionales"
                },
                stockCheck = "{against: colCantidad} valida existencia",
                calc = "expresion de calculo por fila",
                agg = "agregacion de la columna",
                rollup = "acumulado"
            }
        },
        field_lookup_keys = new[] { "source_kind", "source_ref", "display_field", "value_field", "filter_json", "autofill_map_json", "presentation" },
        template_markers = new
        {
            field = "{{campo.codigo}}",
            table_block = "{{#tabla.CODIGO}} ... {{col.ID}} ... {{fila}} ... {{/tabla.CODIGO}}",
            record_number = "{{numero}}",
            date = "{{fecha}}",
            company = "{{empresa}}"
        },
        print = new
        {
            verb = "IMPRIMIR_PLANTILLA",
            params_json = "{\"template\":\"<nombre>\",\"format\":\"print|pdf|img\"}",
            formats = new[] { "print", "pdf", "img" },
            wire_helper = "wire_print_button hace documento+regla+boton+enlace en una sola llamada"
        },
        public_link = "/f/{token} (create_share_link)",
        module_url = "/m/{code} (set_module)"
    };

    private static string[] Names<T>() where T : struct, Enum => Enum.GetNames<T>();

    // ================= Discovery (lecturas) =================

    private async Task<AgentToolResult> ListTenantsAsync(CancellationToken ct)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters()
            .OrderBy(t => t.Name)
            .Select(t => new { id = t.Id, name = t.Name, status = t.Status })
            .ToListAsync(ct);
        return Ok(new { ok = true, total = tenants.Count, tenants });
    }

    private async Task<AgentToolResult> ListFormsAsync(JsonElement args, CancellationToken ct)
    {
        var includeArchived = Bool(args, "include_archived") ?? false;
        var list = await _forms.ListAsync(includeArchived, ct);
        return Ok(new
        {
            ok = true,
            total = list.Count,
            forms = list.Select(f => new
            {
                id = f.Id, code = f.Code, title = f.Title, status = f.Status,
                questions = f.QuestionCount, version = f.Version, archived = f.IsArchived,
                responses = f.ResponseCount, rules = f.RuleCount
            })
        });
    }

    private async Task<AgentToolResult> GetFormAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido (GUID)."); }
        var d = await _forms.GetAsync(id, ct);
        if (d is null) { return Err("No se encontro un formulario con ese id."); }
        return Ok(new { ok = true, form = d });
    }

    private async Task<AgentToolResult> ListTemplatesAsync(CancellationToken ct)
    {
        var list = await _templates.ListAsync(ct);
        return Ok(new
        {
            ok = true,
            total = list.Count,
            templates = list.Select(t => new { id = t.Id, name = t.Name, is_default = t.IsDefault, send_as_image = t.SendAsImage, updated_at = t.UpdatedAt })
        });
    }

    private async Task<AgentToolResult> ListDataContainersAsync(CancellationToken ct)
    {
        var list = await _containers.ListAsync(ct);
        return Ok(new
        {
            ok = true,
            total = list.Count,
            containers = list.Select(c => new { id = c.Id, name = c.Name, source_kind = c.SourceKind, columns = c.ColumnCount, rows = c.RowCount })
        });
    }

    private async Task<AgentToolResult> ListTerceroFieldsAsync(CancellationToken ct)
    {
        var baseFields = new[] { "nombre", "identificacion", "ciudad", "email", "telefono", "vendedor", "sector", "cargo", "estado" };
        var fichaFields = await _terceroFields.ListFieldsAsync(ct);
        return Ok(new
        {
            ok = true,
            base_fields = baseFields,
            ficha_fields = fichaFields.Select(f => new { ficha = f.FichaKey, key = f.FieldKey, label = f.Label, type = f.FieldType })
        });
    }

    private async Task<AgentToolResult> ListMenuViewsAsync(CancellationToken ct)
    {
        var views = await _menus.ListViewsAsync(ct);
        return Ok(new
        {
            ok = true,
            total = views.Count,
            views = views.Select(v => new { id = v.Id, name = v.Name, is_default = v.IsDefault, nodes = v.NodeCount })
        });
    }

    private async Task<AgentToolResult> ListMenuNodesAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "view_id", out var viewId)) { return Err("Falta un 'view_id' valido (de list_menu_views)."); }
        var tree = await _menus.GetViewTreeAsync(viewId, ct);
        if (!tree.IsOk || tree.Value is null) { return Err(tree.Error ?? "No se encontro la vista de menu."); }
        var flat = new List<object>();
        void Walk(IReadOnlyList<MenuEditorNodeDto> nodes)
        {
            foreach (var n in nodes)
            {
                flat.Add(new { id = n.Id, name = n.Name, kind = n.Kind.ToString(), parent_id = n.ParentId, can_host_module = n.Kind is MenuNodeKind.Section or MenuNodeKind.Subgroup });
                if (n.Children.Count > 0) { Walk(n.Children); }
            }
        }
        Walk(tree.Value.Roots);
        return Ok(new { ok = true, view_id = viewId, total = flat.Count, nodes = flat });
    }

    // ================= Autoria =================

    private async Task<AgentToolResult> CreateFormAsync(JsonElement args, CancellationToken ct)
    {
        var code = Str(args, "code");
        var title = Str(args, "title");
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(title)) { return Err("Faltan 'code' y 'title'."); }
        var r = await _forms.CreateAsync(new CreateFormDefinitionRequest(code!.Trim(), title!.Trim(), Str(args, "description")), ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> ImportFormAsync(JsonElement args, CancellationToken ct)
    {
        var json = Str(args, "json");
        if (string.IsNullOrWhiteSpace(json)) { return Err("Falta 'json'."); }
        var r = await _forms.ImportAsync(json!, ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> ExportFormAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var r = await _forms.ExportAsync(id, ct);
        return FormResp(r, v => new { ok = true, json = v });
    }

    private async Task<AgentToolResult> UpdateHeaderAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var title = Str(args, "title");
        var version = Long(args, "version");
        if (string.IsNullOrWhiteSpace(title) || version is null) { return Err("Faltan 'title' y 'version'."); }
        var r = await _forms.UpdateHeaderAsync(id, new UpdateFormDefinitionRequest(title!.Trim(), Str(args, "description"), version.Value), ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> AddContainerAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var name = Str(args, "name");
        if (string.IsNullOrWhiteSpace(name)) { return Err("Falta 'name'."); }
        var req = new SaveFormContainerRequest(
            name!.Trim(),
            EnumOr(args, "container_type", FormContainerType.Segment),
            TryGuid(args, "parent_id", out var pid) ? pid : null,
            Str(args, "style"),
            Width: Int(args, "width") ?? 12,
            InlineLabels: Bool(args, "inline_labels") ?? false);
        var r = await _forms.AddContainerAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, container = v });
    }

    private async Task<AgentToolResult> UpdateContainerAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "container_id", out var id)) { return Err("Falta un 'container_id' valido."); }
        var name = Str(args, "name");
        if (string.IsNullOrWhiteSpace(name)) { return Err("Falta 'name'."); }
        var req = new SaveFormContainerRequest(
            name!.Trim(),
            EnumOr(args, "container_type", FormContainerType.Segment),
            TryGuid(args, "parent_id", out var pid) ? pid : null,
            Str(args, "style"),
            Width: Int(args, "width") ?? 12,
            InlineLabels: Bool(args, "inline_labels") ?? false);
        var r = await _forms.UpdateContainerAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, container = v });
    }

    private async Task<AgentToolResult> MoveContainerAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "container_id", out var id)) { return Err("Falta un 'container_id' valido."); }
        var index = Int(args, "index") ?? 0;
        var r = await _forms.MoveContainerToAsync(id, TryGuid(args, "parent_id", out var pid) ? pid : null, index, ct);
        return FormResp(r, v => new { ok = true, moved = v });
    }

    private SaveFormQuestionRequest BuildQuestionRequest(JsonElement args)
        => new(
            ContainerId: TryGuid(args, "container_id", out var cid) ? cid : null,
            FieldCode: (Str(args, "field_code") ?? string.Empty).Trim(),
            Label: (Str(args, "label") ?? string.Empty).Trim(),
            ControlType: EnumOr(args, "control_type", FormControlType.Text),
            HelpText: Str(args, "help_text"),
            OptionsJson: Str(args, "options_json"),
            Required: Bool(args, "required") ?? false,
            ValidationJson: Str(args, "validation_json"),
            Width: Int(args, "width") ?? 12,
            PlaceholderText: Str(args, "placeholder_text"),
            DefaultValue: Str(args, "default_value"),
            SourceKind: EnumOr(args, "source_kind", FormSourceKind.Options),
            SourceRef: Str(args, "source_ref"),
            DisplayField: Str(args, "display_field"),
            ValueField: Str(args, "value_field"),
            FilterJson: Str(args, "filter_json"),
            AutofillMapJson: Str(args, "autofill_map_json"),
            Presentation: EnumOr(args, "presentation", FormFieldPresentation.Autocomplete),
            CalcExpression: Str(args, "calc_expression"),
            Aggregate: EnumOr(args, "aggregate", FormAggregate.None),
            Format: Str(args, "format"));

    private async Task<AgentToolResult> AddQuestionAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var req = BuildQuestionRequest(args);
        if (string.IsNullOrWhiteSpace(req.FieldCode) || string.IsNullOrWhiteSpace(req.Label)) { return Err("Faltan 'field_code' y 'label'."); }
        var r = await _forms.AddQuestionAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, question = v });
    }

    private async Task<AgentToolResult> UpdateQuestionAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "question_id", out var id)) { return Err("Falta un 'question_id' valido."); }
        var req = BuildQuestionRequest(args);
        if (string.IsNullOrWhiteSpace(req.FieldCode) || string.IsNullOrWhiteSpace(req.Label)) { return Err("Faltan 'field_code' y 'label'."); }
        var r = await _forms.UpdateQuestionAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, question = v });
    }

    private async Task<AgentToolResult> MoveQuestionAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "question_id", out var id)) { return Err("Falta un 'question_id' valido."); }
        var index = Int(args, "index") ?? 0;
        var r = await _forms.MoveQuestionToAsync(id, TryGuid(args, "container_id", out var cid) ? cid : null, index, ct);
        return FormResp(r, v => new { ok = true, moved = v });
    }

    private async Task<AgentToolResult> DeleteQuestionAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "question_id", out var id)) { return Err("Falta un 'question_id' valido."); }
        var r = await _forms.DeleteQuestionAsync(id, ct);
        return FormResp(r, v => new { ok = true, deleted = v });
    }

    private async Task<AgentToolResult> SetTransactionalAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var req = new SetFormTransactionalRequest(
            IsTransactional: Bool(args, "is_transactional") ?? false,
            IdentityMode: EnumOr(args, "identity_mode", FormIdentityMode.None),
            IdentitySourceFieldCode: Str(args, "identity_source_field_code"),
            CardLayout: EnumOr(args, "card_layout", FormCardLayout.Normal),
            IdentityPrefix: Str(args, "identity_prefix"),
            IdentityPadding: Int(args, "identity_padding") ?? 6);
        var r = await _forms.SetTransactionalAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> SetSequenceNextAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var next = Long(args, "next");
        if (next is null) { return Err("Falta 'next'."); }
        var r = await _forms.SetSequenceNextAsync(id, next.Value, ct);
        return FormResp(r, v => new { ok = true, next_value = v });
    }

    private async Task<AgentToolResult> SetModuleAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var req = new SetFormModuleRequest(
            IsModule: Bool(args, "is_module") ?? false,
            MenuViewId: TryGuid(args, "menu_view_id", out var mv) ? mv : null,
            ParentNodeId: TryGuid(args, "parent_node_id", out var pn) ? pn : null,
            Icon: Str(args, "icon"),
            ListColumns: StrArray(args, "list_columns"),
            FilterFields: StrArray(args, "filter_fields"),
            MenuLabel: Str(args, "menu_label"));
        var r = await _forms.SetModuleAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, form = v, module_url = v is null ? null : $"/m/{v.Code}" });
    }

    private async Task<AgentToolResult> SetCustomCssAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var r = await _forms.SetCustomCssAsync(id, new SetFormCssRequest(Str(args, "custom_css")), ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> ActivateAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var r = await _forms.ActivateAsync(id, ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> DeactivateAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var r = await _forms.DeactivateAsync(id, ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> ArchiveAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var r = await _forms.SetArchivedAsync(id, Bool(args, "archived") ?? true, ct);
        return FormResp(r, v => new { ok = true, archived = v });
    }

    // ================= Plantillas =================

    private async Task<AgentToolResult> CreateTemplateAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        var name = Str(args, "name");
        var html = Str(args, "html");
        if (string.IsNullOrWhiteSpace(name) || html is null) { return Err("Faltan 'name' y 'html'."); }
        var t = await _templates.CreateAsync(name!.Trim(), html, Bool(args, "send_as_image") ?? false, actor, ct);
        return t is null ? Err("No se pudo crear la plantilla (nombre vacio o sin tenant).")
            : Ok(new { ok = true, template = new { id = t.Id, name = t.Name, is_default = t.IsDefault, send_as_image = t.SendAsImage } });
    }

    private async Task<AgentToolResult> UpdateTemplateAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        if (!TryGuid(args, "template_id", out var id)) { return Err("Falta un 'template_id' valido."); }
        var name = Str(args, "name");
        var html = Str(args, "html");
        if (string.IsNullOrWhiteSpace(name) || html is null) { return Err("Faltan 'name' y 'html'."); }
        var t = await _templates.UpdateAsync(id, name!.Trim(), html, Bool(args, "send_as_image") ?? false, actor, ct);
        return t is null ? Err("No se encontro la plantilla.")
            : Ok(new { ok = true, template = new { id = t.Id, name = t.Name, is_default = t.IsDefault, send_as_image = t.SendAsImage } });
    }

    private async Task<AgentToolResult> SetDefaultTemplateAsync(JsonElement args, Guid actor, CancellationToken ct)
    {
        if (!TryGuid(args, "template_id", out var id)) { return Err("Falta un 'template_id' valido."); }
        var ok = await _templates.SetDefaultAsync(id, actor, ct);
        return ok ? Ok(new { ok = true, is_default = true }) : Err("No se encontro la plantilla.");
    }

    private async Task<AgentToolResult> WirePrintButtonAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var formId)) { return Err("Falta un 'form_id' valido."); }
        var templateName = Str(args, "template_name");
        if (string.IsNullOrWhiteSpace(templateName)) { return Err("Falta 'template_name'."); }
        var format = (Str(args, "format") ?? "print").Trim().ToLowerInvariant();
        if (format is not ("print" or "pdf" or "img")) { return Err("'format' debe ser print, pdf o img."); }
        var label = Str(args, "button_label") ?? "Imprimir";

        var form = await _forms.GetAsync(formId, ct);
        if (form is null) { return Err("No se encontro el formulario."); }

        // 1) Documento de reglas para el formulario (code estable por formulario).
        var docCode = $"IMPRESION-{form.Code}";
        var docReq = new SaveRuleDocumentRequest(docCode, $"Impresion {form.Title}", "Impresion");
        var docRes = await _rules.CreateDocumentAsync(docReq, ct);
        if (!docRes.IsOk || docRes.Value is null) { return Err($"No se pudo crear el documento de reglas: {docRes.Error}"); }

        // 2) Regla IMPRIMIR_PLANTILLA {template, format}.
        var paramsJson = JsonSerializer.Serialize(new { template = templateName!.Trim(), format }, JsonOut);
        var ruleReq = new SaveRuleRequest($"Imprimir {templateName}", "IMPRIMIR_PLANTILLA", ParamsJson: paramsJson);
        var ruleRes = await _rules.CreateRuleAsync(docRes.Value.Id, ruleReq, ct);
        if (!ruleRes.IsOk || ruleRes.Value is null) { return Err($"No se pudo crear la regla: {ruleRes.Error}"); }

        // 3) Pregunta tipo Button.
        var fieldCode = Str(args, "field_code");
        if (string.IsNullOrWhiteSpace(fieldCode)) { fieldCode = $"btn_imprimir_{format}"; }
        var qReq = new SaveFormQuestionRequest(
            ContainerId: TryGuid(args, "container_id", out var cid) ? cid : null,
            FieldCode: fieldCode!.Trim(), Label: label, ControlType: FormControlType.Button);
        var qRes = await _forms.AddQuestionAsync(formId, qReq, ct);
        if (!qRes.IsOk || qRes.Value is null) { return FormResp(qRes, v => new { ok = true }); }

        // 4) Enlazar la regla a la pregunta Button.
        var linkRes = await _rules.LinkToQuestionAsync(ruleRes.Value.Id, qRes.Value.Id, 0, ct);
        if (!linkRes.IsOk) { return Err($"Se creo el boton pero no se pudo enlazar la regla: {linkRes.Error}"); }

        return Ok(new
        {
            ok = true,
            document_id = docRes.Value.Id,
            rule_id = ruleRes.Value.Id,
            question_id = qRes.Value.Id,
            field_code = fieldCode,
            template = templateName,
            format
        });
    }

    // ================= Enlaces compartidos =================

    private async Task<AgentToolResult> CreateShareLinkAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var req = new EmitFormTokenRequest(
            Reference: Str(args, "reference"),
            ExpirationHours: Int(args, "expiration_hours") ?? 24,
            SingleUse: Bool(args, "single_use") ?? false,
            AllowAnonymous: Bool(args, "allow_anonymous") ?? true);
        var r = await _tokens.EmitAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, token = v.Token, url = $"/f/{v.Token}", token_id = v.TokenId, expires_at = v.ExpiresAt });
    }

    // ================= Registros =================

    private async Task<AgentToolResult> CreateRecordAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        if (!args.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object) { return Err("Falta 'data' (objeto field_code -> valor)."); }

        var data = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        foreach (var p in dataEl.EnumerateObject())
        {
            data[p.Name] = ToFieldValue(p.Value);
        }

        var draft = await _responses.GetOrCreateDraftAsync(id, Str(args, "reference"), ct);
        if (!draft.IsOk || draft.Value is null) { return FormResp(draft, v => new { ok = true }); }

        var submit = Bool(args, "submit") ?? false;
        var saved = await _responses.SaveAsync(draft.Value.Id, data, submit, cancellationToken: ct);
        return FormResp(saved, v => new
        {
            ok = true,
            response_id = v.Id,
            status = v.Status,
            record_number = v.RecordNumber,
            record_status = v.RecordStatus,
            version = v.Version
        });
    }

    private static FormFieldValue ToFieldValue(JsonElement v)
    {
        // Acepta escalar directo o {value, type}.
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("value", out var inner))
        {
            var type = v.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "text";
            return new FormFieldValue(ScalarToString(inner), type);
        }
        return new FormFieldValue(ScalarToString(v), InferType(v));
    }

    private static string InferType(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "bool",
        _ => "text"
    };

    private static string? ScalarToString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => v.GetRawText()
    };

    private AgentToolResult GetRenderUrls(JsonElement args)
    {
        if (!TryGuid(args, "response_id", out var id)) { return Err("Falta un 'response_id' valido."); }
        var tpl = TryGuid(args, "template_id", out var t) ? $"?templateId={t}" : string.Empty;
        var sep = tpl.Length == 0 ? "?" : "&";
        return Ok(new
        {
            ok = true,
            view = $"/formularios/plantilla/{id}{tpl}{sep}print=1",
            pdf = $"/formularios/plantilla/{id}/pdf{tpl}",
            img = $"/formularios/plantilla/{id}/img{tpl}"
        });
    }

    // ================= Helpers =================

    private static AgentToolResult Ok(object payload) => new(JsonSerializer.Serialize(payload, JsonOut), SessionCompleted: false);
    private static AgentToolResult Err(string message) => new(JsonSerializer.Serialize(new { ok = false, error = message }, JsonOut), SessionCompleted: false);

    /// <summary>Mapea un FormResult a salida de tool: exito -> project(Value); fallo -> error ESTRUCTURADO.</summary>
    private static AgentToolResult FormResp<T>(FormResult<T> r, Func<T, object> project)
        => r.IsOk && r.Value is not null
            ? Ok(project(r.Value))
            : new(JsonSerializer.Serialize(new { ok = false, status = r.Status.ToString(), error = r.Error, field_errors = r.FieldErrors }, JsonOut), SessionCompleted: false);

    private static string? Str(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool? Bool(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : null;

    private static int? Int(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : null;

    private static long? Long(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n : null;

    private static bool TryGuid(JsonElement el, string prop, out Guid id)
    {
        id = Guid.Empty;
        return el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v)
            && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out id) && id != Guid.Empty;
    }

    private static IReadOnlyList<string>? StrArray(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.Array) { return null; }
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) { var s = item.GetString(); if (!string.IsNullOrWhiteSpace(s)) { list.Add(s!); } }
        }
        return list.Count == 0 ? null : list;
    }

    private static TEnum EnumOr<TEnum>(JsonElement el, string prop, TEnum fallback) where TEnum : struct, Enum
    {
        var s = Str(el, prop);
        return !string.IsNullOrWhiteSpace(s) && Enum.TryParse<TEnum>(s, ignoreCase: true, out var val) ? val : fallback;
    }
}
