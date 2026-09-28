using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ecorex.Application.Common;
using Ecorex.Application.DataContainers;
using Ecorex.Application.Directorio;
using Ecorex.Application.Forms;
using Ecorex.Application.Forms.Calc;
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
    private readonly Organization.IWorkflowNodePolicyService _orgUnits;

    public FormAuthoringToolset(
        IFormDefinitionService forms, IFormTokenService tokens, IFormResponseService responses,
        IQuoteTemplateService templates, IRuleDocumentService rules, IDataContainerService containers,
        ITerceroFieldService terceroFields, IMenuConfigService menus, IApplicationDbContext db,
        Organization.IWorkflowNodePolicyService orgUnits)
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
        _orgUnits = orgUnits;
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
        "describe_components", "list_tenants", "list_forms", "get_form", "verify_form", "list_templates",
        "list_data_containers", "describe_data_container", "list_tercero_fields", "list_org_units",
        "list_activity_types", "list_menu_views", "list_menu_nodes", "export_form", "get_render_urls"
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
        new("verify_form",
            "AUTO-REVISION de coherencia de un formulario ya construido (read-only). Devuelve la lista de PROBLEMAS: " +
            "columna calc sin formula, rollup que apunta a un campo inexistente, un CAMPO que suma una columna con " +
            "{#...} (deberia ser rollup), un campo destino de rollup que ademas tiene calc (lo pisa), lookup sin " +
            "source_ref, lista Options sin opciones, referencias {codigo} colgantes, NaturalKey a un campo inexistente. " +
            "Llamala AL TERMINAR de construir y CORRIGE cada 'error' que reporte antes de cerrar.",
            """{"type":"object","properties":{"form_id":{"type":"string"}},"required":["form_id"],"additionalProperties":false}"""),
        new("list_templates",
            "Lista las plantillas de impresion del tenant (id, nombre, si es la predeterminada, si se envia como imagen).",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_data_containers",
            "Lista los contenedores de datos del tenant (id, nombre). Usa el id como 'sourceRef' de un lookup/resolve con source=DataContainer.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("describe_data_container",
            "Devuelve el ESQUEMA de un contenedor de datos: sus columnas (nombre + tipo) y cuantas filas tiene. " +
            "Llamala ANTES de enlazar un lookup/resolve para saber que columnas usar como displayField/valueField " +
            "(lista) o match/return (VLOOKUP): esos campos van por NOMBRE de columna. Acepta 'container_id' (GUID) o 'name'.",
            """{"type":"object","properties":{"container_id":{"type":"string","description":"Id (GUID) del contenedor"},"name":{"type":"string","description":"Nombre exacto (alternativa al id)"}},"additionalProperties":false}"""),
        new("create_data_container",
            "Crea un CONTENEDOR de datos (tabla de respaldo para una lista/desplegable o una formula VLOOKUP del formulario). " +
            "columns: arreglo de {name, type} con type = Text|Number|Decimal|Date|Boolean. Devuelve el id del contenedor y sus " +
            "columnas con id. Si ya existe uno con ese nombre lo REUSA (no duplica). Luego carga filas con add_container_rows.",
            """{"type":"object","properties":{"name":{"type":"string"},"description":{"type":"string"},"columns":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"type":{"type":"string","description":"Text|Number|Decimal|Date|Boolean"}},"required":["name"]}}},"required":["name","columns"],"additionalProperties":false}"""),
        new("add_container_rows",
            "Carga filas en un contenedor de datos (ej. un catalogo/lista de precios sacado de una hoja del Excel). 'rows' es un " +
            "arreglo de objetos donde cada CLAVE es el NOMBRE de una columna del contenedor y su valor el dato; las claves que no " +
            "sean columnas se ignoran. Todo se guarda como texto (EAV). Solo INSERTA. Acepta 'container_id' o 'name'.",
            """{"type":"object","properties":{"container_id":{"type":"string"},"name":{"type":"string"},"rows":{"type":"array","items":{"type":"object"},"description":"Filas: cada objeto es columna->valor"}},"required":["rows"],"additionalProperties":false}"""),
        new("list_tercero_fields",
            "Lista los campos disponibles de Tercero (Directorio) para autofill de un lookup con source=Tercero: base (nombre, identificacion, ciudad, email, telefono, vendedor, sector, cargo, estado) + los campos de ficha configurados.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_org_units",
            "Lista las Dependencias y Cargos del organigrama del tenant (id, nombre, classifier Dependencia|Cargo, parent). Usa estos ids en 'allowed_cargos_json' de add_container/update_container para restringir el acceso a una seccion.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        new("list_activity_types",
            "Lista los tipos de actividad/tarea del tenant (id, nombre). Usa el id como 'activity_type_id' de wire_submit_task_rule (la tarea que se crea al enviar el formulario).",
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
            "Agrega un contenedor (seccion/tabla/fila/columna/tabs/modal). container_type: Segment,Table,Row,Col,Section,Tabs,Modal. " +
            "width en la rejilla de 12. visible_when_json (muestra/oculta por valor de otra pregunta) y allowed_cargos_json " +
            "(restringe a Cargos/Dependencias, ids de list_org_units).",
            """{"type":"object","properties":{"form_id":{"type":"string"},"name":{"type":"string"},"container_type":{"type":"string"},"parent_id":{"type":"string","description":"Contenedor padre (opcional; raiz si se omite)"},"width":{"type":"integer"},"inline_labels":{"type":"boolean"},"style":{"type":"string"},"visible_when_json":{"type":"string","description":"Condicion de visibilidad {\"field\":\"codigo\",\"op\":\"equals|notEquals|includes|empty|notEmpty\",\"value\":\"x\"}"},"allowed_cargos_json":{"type":"string","description":"Arreglo JSON de ids de OrgUnit (Cargo|Dependencia) de list_org_units; vacio/omitido = sin restriccion"}},"required":["form_id","name"],"additionalProperties":false}"""),
        new("update_container",
            "Actualiza un contenedor por su id (nombre/tipo/ancho/estilo/visibilidad/acceso por cargo).",
            """{"type":"object","properties":{"container_id":{"type":"string"},"name":{"type":"string"},"container_type":{"type":"string"},"width":{"type":"integer"},"inline_labels":{"type":"boolean"},"style":{"type":"string"},"parent_id":{"type":"string"},"visible_when_json":{"type":"string"},"allowed_cargos_json":{"type":"string"}},"required":["container_id","name"],"additionalProperties":false}"""),
        new("move_container",
            "Mueve un contenedor a otro padre (o a la raiz con parent_id vacio) en la posicion 'index'.",
            """{"type":"object","properties":{"container_id":{"type":"string"},"parent_id":{"type":"string"},"index":{"type":"integer"}},"required":["container_id","index"],"additionalProperties":false}"""),
        new("add_question",
            "Agrega una pregunta/campo. control_type, capacidades (options_json de Select/MultiCheck/GridDetail, lookup " +
            "de campo con source_kind/source_ref/display_field/value_field/autofill_map_json, calc_expression+aggregate, " +
            "format, default_value con tokens de prellenado) en describe_components. field_code unico en el formulario.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"container_id":{"type":"string","description":"Contenedor destino (opcional; raiz si se omite)"},"field_code":{"type":"string"},"label":{"type":"string"},"control_type":{"type":"string"},"required":{"type":"boolean"},"options_json":{"type":"string","description":"JSON de opciones (Select/Radio/MultiCheck) o de columnas (GridDetail)"},"help_text":{"type":"string"},"placeholder_text":{"type":"string"},"default_value":{"type":"string"},"width":{"type":"integer"},"source_kind":{"type":"string"},"source_ref":{"type":"string"},"display_field":{"type":"string"},"value_field":{"type":"string"},"filter_json":{"type":"string"},"autofill_map_json":{"type":"string"},"presentation":{"type":"string","description":"Autocomplete|Dropdown|Modal"},"calc_expression":{"type":"string"},"aggregate":{"type":"string","description":"None|Sum|Count|Avg|Min|Max"},"format":{"type":"string"},"validation_json":{"type":"string"},"visible_when_json":{"type":"string","description":"Muestra/oculta el campo segun otra pregunta {\"field\":\"codigo\",\"op\":\"equals|notEquals|includes|empty|notEmpty\",\"value\":\"x\"}"}},"required":["form_id","field_code","label","control_type"],"additionalProperties":false}"""),
        new("update_question",
            "Actualiza una pregunta por su id. Mismos campos que add_question (los que omitas vuelven a su valor por defecto del request).",
            """{"type":"object","properties":{"question_id":{"type":"string"},"container_id":{"type":"string"},"field_code":{"type":"string"},"label":{"type":"string"},"control_type":{"type":"string"},"required":{"type":"boolean"},"options_json":{"type":"string"},"help_text":{"type":"string"},"placeholder_text":{"type":"string"},"default_value":{"type":"string"},"width":{"type":"integer"},"source_kind":{"type":"string"},"source_ref":{"type":"string"},"display_field":{"type":"string"},"value_field":{"type":"string"},"filter_json":{"type":"string"},"autofill_map_json":{"type":"string"},"presentation":{"type":"string"},"calc_expression":{"type":"string"},"aggregate":{"type":"string"},"format":{"type":"string"},"validation_json":{"type":"string"},"visible_when_json":{"type":"string"}},"required":["question_id","field_code","label","control_type"],"additionalProperties":false}"""),
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
            "Guarda el CSS personalizado de todo el formulario (pestana Estilos del disenador). Para colores/marca usa MEJOR set_theme (aplica la variable --brand que el renderer si consume). Si usas CSS, apunta a las clases REALES del renderer: dfr-segment/.dfr-seg-head (secciones), form-control (inputs), dfr-tabbar/.dfr-tab (pestanas), dfr-formbtn (botones). NO uses .form-section/.btn-primary/.nav-tabs (no existen).",
            """{"type":"object","properties":{"form_id":{"type":"string"},"custom_css":{"type":"string"}},"required":["form_id"],"additionalProperties":false}"""),
        new("set_theme",
            "APARIENCIA/TEMA del formulario (la forma correcta de aplicar la identidad de marca). 'color' (hex) es el COLOR DE MARCA: fija la variable --brand que TODO el renderer usa (encabezados de seccion, acentos, opt-cards, chips) -> tematiza el formulario entero de una. tema: clasico|prototipo (prototipo activa el look hero+rotulo). hero: encabezado tipo hero. eyebrow: rotulo pequeno arriba del titulo. hide_chips: oculta chips tecnicos. cards: estilo tarjetas para opciones (Radio/MultiCheck). Enviar todo vacio/omitido deja el tema clasico por defecto.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"color":{"type":"string","description":"Color de marca hex (#RRGGBB): fija --brand y tematiza todo"},"tema":{"type":"string","description":"clasico|prototipo"},"hero":{"type":"boolean"},"eyebrow":{"type":"string","description":"Rotulo pequeno sobre el titulo. Si lleva icono, usa el EMOJI directo (ej. ⚡ Gestion), NO entidades HTML como &#9889;"},"hide_chips":{"type":"boolean"},"cards":{"type":"boolean"}},"required":["form_id"],"additionalProperties":false}"""),
        new("set_status_ladder",
            "Escalon de ESTADOS calculados del registro: un campo 'estado' toma la etiqueta del escalon MAS ALTO cuyas " +
            "condiciones se cumplen (solo avanza, nunca baja). status_ladder_json = {\"field\":\"<field_code destino>\"," +
            "\"states\":[{\"label\":\"Inicial\",\"when\":[]},{\"label\":\"Siguiente\",\"when\":[{\"field\":\"otro_codigo\"," +
            "\"op\":\"equals|notEquals|includes|notEmpty|empty\",\"value\":\"x\"}]}, ...]}. Estados ORDENADOS de menor a " +
            "mayor; el primero (when vacio) es el piso. El 'field' destino es un campo del formulario (Text) que muestra el estado.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"status_ladder_json":{"type":"string","description":"JSON del escalon {field,states[]}; vacio/omitido lo quita"}},"required":["form_id"],"additionalProperties":false}"""),
        new("wire_submit_task_rule",
            "REGLA AL ENVIAR que crea una TAREA/actividad cuando se envia el formulario (incl. la ruta publica /f/). " +
            "activity_type_id (de list_activity_types) es el tipo de actividad a crear. Titulo: 'fixed_title' (una sola tarea) " +
            "O 'table_field_code' (una tarea POR FILA de esa grilla, con 'title_key' = columna que da el titulo). 'assignee_user_id' " +
            "opcional (a quien se asigna); 'title_prefix' opcional; 'auto_complete' la deja completada.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"activity_type_id":{"type":"string"},"assignee_user_id":{"type":"string"},"fixed_title":{"type":"string"},"table_field_code":{"type":"string"},"title_key":{"type":"string"},"title_prefix":{"type":"string"},"auto_complete":{"type":"boolean"}},"required":["form_id","activity_type_id"],"additionalProperties":false}"""),
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
        new("wire_convert_button",
            "En UNA operacion deja un boton 'Convertir a otro formulario' (CONVERTIR_A_FORMULARIO, ADR-0078): crea documento de reglas + regla + pregunta Button + enlace. Copia los datos mapeables del registro actual a un NUEVO registro del formulario destino (por codigo) y lo abre. " +
            "target_code: codigo EXACTO del formulario destino (activo). mapping_json: {campoOrigen:campoDestino} solo para los que cambian de nombre (los de igual codigo se copian solos). grid_mapping_json: {grilla:{colOrigen:colDestino}}. " +
            "grid_derive_json (AUTO-MARCADO de columnas de grilla al convertir): {grilla:[{target,from,when,set}]}; por fila, si 'when' se cumple sobre la columna 'from' pone 'set' en 'target' (si no, vacio). when: '>N' (numerico mayor que N), '=<valor>' (igualdad, ej '=SI'), 'notempty'. set por defecto 'X'. " +
            "defaults_json: {campoDestino:valor} para rellenar lo que no viene del origen; admite tokens @usuario.nombre/@usuario.email/@fecha.hoy/@fecha.hora.",
            """{"type":"object","properties":{"form_id":{"type":"string"},"target_code":{"type":"string"},"button_label":{"type":"string"},"mapping_json":{"type":"string"},"grid_mapping_json":{"type":"string"},"grid_derive_json":{"type":"string","description":"gridDerive {grilla:[{target,from,when,set}]}"},"defaults_json":{"type":"string"},"open_mode":{"type":"string"},"container_id":{"type":"string"},"field_code":{"type":"string"}},"required":["form_id","target_code"],"additionalProperties":false}"""),

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
                "verify_form" => await VerifyFormAsync(args, cancellationToken),
                "list_templates" => await ListTemplatesAsync(cancellationToken),
                "list_data_containers" => await ListDataContainersAsync(cancellationToken),
                "describe_data_container" => await DescribeDataContainerAsync(args, cancellationToken),
                "create_data_container" => await CreateDataContainerAsync(args, actorUserId, cancellationToken),
                "add_container_rows" => await AddContainerRowsAsync(args, actorUserId, cancellationToken),
                "list_tercero_fields" => await ListTerceroFieldsAsync(cancellationToken),
                "list_org_units" => await ListOrgUnitsAsync(cancellationToken),
                "list_activity_types" => await ListActivityTypesAsync(cancellationToken),
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
                "set_theme" => await SetThemeAsync(args, cancellationToken),
                "set_status_ladder" => await SetStatusLadderAsync(args, cancellationToken),
                "wire_submit_task_rule" => await WireSubmitTaskRuleAsync(args, cancellationToken),
                "activate" => await ActivateAsync(args, cancellationToken),
                "deactivate" => await DeactivateAsync(args, cancellationToken),
                "archive" => await ArchiveAsync(args, cancellationToken),
                "create_template" => await CreateTemplateAsync(args, actorUserId, cancellationToken),
                "update_template" => await UpdateTemplateAsync(args, actorUserId, cancellationToken),
                "set_default_template" => await SetDefaultTemplateAsync(args, actorUserId, cancellationToken),
                "wire_print_button" => await WirePrintButtonAsync(args, cancellationToken),
                "wire_convert_button" => await WireConvertButtonAsync(args, cancellationToken),
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
            card_layouts = Names<FormCardLayout>(),
            theme = new
            {
                tool = "set_theme",
                brand_color = "set_theme(color=#RRGGBB) fija --brand y tematiza TODO el formulario; es la forma correcta de aplicar la marca (no adivinar CSS)",
                tema = new[] { "clasico", "prototipo" },
                flags = new[] { "hero", "eyebrow", "hide_chips", "cards" },
                layout = "columnas via width (rejilla de 12; 6=2 col, 4=3 col); pestanas via contenedor Tabs + mover secciones dentro (update_container parent_id)"
            }
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
        calc = new
        {
            note = "Sintaxis del 'calc'/'calc_expression'. Motor NUMERICO. Refs a otro campo/columna SIEMPRE {codigo}; " +
                "encabezado desde una grilla {#codigo}. NUNCA [x] ni nombre suelto.",
            functions = new[] { "SI(cond;siSi;siNo)", "REDONDEAR", "REDONDEAR.SUPERIOR", "REDONDEAR.INFERIOR", "MIN", "MAX" },
            operators = new[] { "+", "-", "*", "/", ">", "<", ">=", "<=", "==", "!=", "()" },
            no_lookup_function = "NO existe resolve()/vlookup()/buscarv()/lookup() como funcion; un VLOOKUP multi-clave es una COLUMNA type:resolve de una GRILLA, no una formula",
            percent = "format 'percent' guarda el numero tal cual (5=5). Para aplicar %, divide entre 100: (1 - {dcto}/100)",
            rollup = "sumar una columna NO es una formula: la columna calc lleva agg=Sum + rollup=<field_code destino>; el campo destino NO debe tener calc_expression (lo pisaria)"
        },
        prefill_tokens = new
        {
            note = "Para prellenar un campo pon estos tokens en su default_value. Fuera de una tarea quedan vacios.",
            tarea = new[] { "{tareas.cliente}", "{tareas.contacto}", "{tareas.solicitante}", "{tareas.email}", "{tareas.correo}", "{tareas.telefono}", "{tareas.nit}", "{tareas.documento}", "{tareas.identificacion}", "{tareas.titulo}", "{tareas.numero}", "{tareas.comercial}", "{tareas.responsable}" },
            sistema = new[] { "{hoy}", "{hoy+N}", "{ahora}", "{numero}" }
        },
        status_ladder = new
        {
            note = "Escalon de estados via set_status_ladder. Estados de menor a mayor; el primero (when vacio) es el piso; solo AVANZA.",
            format = "{\"field\":\"<field_code destino>\",\"states\":[{\"label\":\"Inicial\",\"when\":[]},{\"label\":\"Sig\",\"when\":[{\"field\":\"otro\",\"op\":\"equals|notEquals|includes|empty|notEmpty\",\"value\":\"x\"}]}]}"
        },
        submit_task_rule = "Regla al enviar que crea una tarea: wire_submit_task_rule (activity_type_id de list_activity_types; fixed_title=una tarea o table_field_code+title_key=una por fila). Otras acciones on-submit NO estan expuestas.",
        verify_form = "AUTO-REVISION de solo lectura: verify_form(form_id) devuelve los problemas de coherencia (rollup a un campo inexistente, un campo que suma una columna con {#...}, destino de rollup con calc que lo pisa, lookup sin source_ref, lista sin opciones, referencias {codigo} colgantes, NaturalKey a un campo inexistente). Llamala AL TERMINAR y corrige cada 'error' hasta que salga errors=0.",
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
        convert = new
        {
            verb = "CONVERTIR_A_FORMULARIO",
            wire_helper = "wire_convert_button deja un boton que crea+abre un registro de OTRO formulario copiando lo mapeable",
            grid_derive = "grid_derive_json = {\"grilla\":[{\"target\":\"col\",\"from\":\"colOrigen\",\"when\":\">0|=SI|notempty\",\"set\":\"X\"}]}"
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

    // Columnas ESCALARES de un contenedor (se excluyen Submodel y los tipos deprecados de relacion, que
    // no tienen celda simple ni sirven como campo de lookup por nombre).
    private static bool IsScalarColumn(DataContainerColumnType t)
        => t is not (DataContainerColumnType.Submodel or DataContainerColumnType.Reference or DataContainerColumnType.RelationMany);

    // Resuelve un contenedor por 'container_id' (GUID) o, si no, por 'name' (exacto, case-insensitive).
    private async Task<DataContainerDetailDto?> ResolveContainerAsync(JsonElement args, CancellationToken ct)
    {
        if (TryGuid(args, "container_id", out var id)) { return await _containers.GetAsync(id, ct); }
        var name = Str(args, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name)) { return null; }
        var all = await _containers.ListAsync(ct);
        var match = all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : await _containers.GetAsync(match.Id, ct);
    }

    private async Task<AgentToolResult> DescribeDataContainerAsync(JsonElement args, CancellationToken ct)
    {
        var detail = await ResolveContainerAsync(args, ct);
        if (detail is null) { return Err("No se encontro el contenedor. Pasa un 'container_id' valido o el 'name' exacto (ver list_data_containers)."); }
        var rows = await _containers.ListRowsAsync(detail.Id, take: 1, ct: ct);
        var hasRows = rows.Count > 0;
        return Ok(new
        {
            ok = true,
            id = detail.Id,
            name = detail.Name,
            has_rows = hasRows,
            columns = detail.Columns.Where(c => IsScalarColumn(c.Type))
                .OrderBy(c => c.SortOrder)
                .Select(c => new { name = c.Name, type = c.Type.ToString(), required = c.IsRequired })
        });
    }

    // Mapea el 'type' textual de una columna (tolera ingles/espanol) al enum. Default Text.
    private static DataContainerColumnType ParseColumnType(string? type) => (type ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "number" or "numero" or "entero" or "int" or "integer" => DataContainerColumnType.Number,
        "decimal" or "moneda" or "currency" or "float" or "double" => DataContainerColumnType.Decimal,
        "date" or "fecha" or "datetime" => DataContainerColumnType.Date,
        "boolean" or "bool" or "si_no" or "sino" => DataContainerColumnType.Boolean,
        _ => DataContainerColumnType.Text,
    };

    private async Task<AgentToolResult> CreateDataContainerAsync(JsonElement args, Guid actorUserId, CancellationToken ct)
    {
        var name = Str(args, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name)) { return Err("Falta 'name' del contenedor."); }

        // Reuso idempotente: si ya existe uno con ese nombre, se devuelve (no se duplica).
        var existing = (await _containers.ListAsync(ct)).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var det0 = await _containers.GetAsync(existing.Id, ct);
            return Ok(new
            {
                ok = true,
                reused = true,
                id = existing.Id,
                name = existing.Name,
                columns = det0?.Columns.Where(c => IsScalarColumn(c.Type)).Select(c => new { id = c.Id, name = c.Name, type = c.Type.ToString() })
            });
        }

        var colsEl = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("columns", out var cv) && cv.ValueKind == JsonValueKind.Array ? cv : (JsonElement?)null;
        if (colsEl is not { } colsArr || colsArr.GetArrayLength() == 0) { return Err("Falta 'columns' (arreglo de {name,type})."); }

        var columns = new List<SaveDataColumnInput>();
        var order = 0;
        foreach (var col in colsArr.EnumerateArray())
        {
            if (col.ValueKind != JsonValueKind.Object) { continue; }
            var cname = (col.TryGetProperty("name", out var cn) && cn.ValueKind == JsonValueKind.String ? cn.GetString() : null)?.Trim();
            if (string.IsNullOrWhiteSpace(cname)) { continue; }
            var ctype = ParseColumnType(col.TryGetProperty("type", out var ct2) && ct2.ValueKind == JsonValueKind.String ? ct2.GetString() : null);
            columns.Add(new SaveDataColumnInput(null, cname!, null, ctype, order++, IsRequired: false));
        }
        if (columns.Count == 0) { return Err("Ninguna columna valida en 'columns' (cada una necesita 'name')."); }

        var req = new SaveDataContainerRequest(null, name!, Str(args, "description"), DataSourceKind.Manual, columns);
        var saved = await _containers.SaveAsync(req, actorUserId, ct);
        if (saved is null) { return Err("No se pudo crear el contenedor."); }
        return Ok(new
        {
            ok = true,
            id = saved.Id,
            name = saved.Name,
            columns = saved.Columns.Where(c => IsScalarColumn(c.Type)).Select(c => new { id = c.Id, name = c.Name, type = c.Type.ToString() })
        });
    }

    private async Task<AgentToolResult> AddContainerRowsAsync(JsonElement args, Guid actorUserId, CancellationToken ct)
    {
        var detail = await ResolveContainerAsync(args, ct);
        if (detail is null) { return Err("No se encontro el contenedor. Pasa 'container_id' o 'name' (ver list_data_containers)."); }

        var rowsEl = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("rows", out var rv) && rv.ValueKind == JsonValueKind.Array ? rv : (JsonElement?)null;
        if (rowsEl is not { } rowsArr || rowsArr.GetArrayLength() == 0) { return Err("Falta 'rows' (arreglo de objetos columna->valor)."); }

        var colByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in detail.Columns.Where(c => IsScalarColumn(c.Type)))
        {
            if (!colByName.ContainsKey(c.Name.Trim())) { colByName[c.Name.Trim()] = c.Id; }
        }
        if (colByName.Count == 0) { return Err("El contenedor no tiene columnas escalares donde cargar datos."); }

        var loaded = 0;
        var cells = 0;
        foreach (var item in rowsArr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { continue; }
            var values = new Dictionary<Guid, string?>();
            foreach (var prop in item.EnumerateObject())
            {
                if (!colByName.TryGetValue(prop.Name.Trim(), out var colId)) { continue; }
                values[colId] = CellText(prop.Value);
                cells++;
            }
            if (values.Count == 0) { continue; }
            await _containers.SaveRowAsync(new SaveDataRowRequest(detail.Id, null, values), actorUserId, ct);
            loaded++;
        }
        return Ok(new { ok = true, loaded, cells, container = detail.Name, id = detail.Id });
    }

    // El contenedor guarda TODO como texto (EAV): numeros/booleanos se serializan a su texto tal cual.
    private static string? CellText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => v.GetRawText(),
    };

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

    private async Task<AgentToolResult> ListOrgUnitsAsync(CancellationToken ct)
    {
        var units = await _orgUnits.ListAssignableUnitsAsync(ct);
        return Ok(new
        {
            ok = true,
            total = units.Count,
            note = "Usa estos ids en allowed_cargos_json de una seccion para restringir su acceso.",
            units = units.Select(u => new { id = u.Id, name = u.Name, classifier = u.Classifier.ToString(), parent_id = u.ParentId })
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
            InlineLabels: Bool(args, "inline_labels") ?? false,
            AllowedCargosJson: Str(args, "allowed_cargos_json"),
            VisibleWhenJson: Str(args, "visible_when_json"));
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
            InlineLabels: Bool(args, "inline_labels") ?? false,
            AllowedCargosJson: Str(args, "allowed_cargos_json"),
            VisibleWhenJson: Str(args, "visible_when_json"));
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
    {
        var controlType = EnumOr(args, "control_type", FormControlType.Text);
        return new(
            ContainerId: TryGuid(args, "container_id", out var cid) ? cid : null,
            FieldCode: (Str(args, "field_code") ?? string.Empty).Trim(),
            Label: (Str(args, "label") ?? string.Empty).Trim(),
            ControlType: controlType,
            HelpText: Str(args, "help_text"),
            OptionsJson: NormalizeOptionsJson(Str(args, "options_json"), controlType),
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
            CalcExpression: FormExpressionEvaluator.NormalizeReferences(Str(args, "calc_expression")),
            Aggregate: EnumOr(args, "aggregate", FormAggregate.None),
            Format: Str(args, "format"),
            VisibleWhenJson: Str(args, "visible_when_json"));
    }

    // BLINDAJE de autoria por agente para el options_json. Segun el control:
    //  - GridDetail: es un arreglo de COLUMNAS -> normaliza el 'calc' de cada columna a la sintaxis del motor
    //    ({codigo}) y, en columnas select/multicheck, normaliza sus 'options' anidadas para que cada una tenga
    //    id Y label (el modelo suele omitir el label y la persistencia lo rechaza -> bucle de error).
    //  - Select/Radio/MultiCheck: es un arreglo de OPCIONES -> normaliza cada opcion (string suelto o falta de
    //    id/label) al par {id,label}.
    // Si no es JSON de arreglo valido lo devuelve sin tocar (no rompe nada).
    private static string? NormalizeOptionsJson(string? optionsJson, FormControlType controlType)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) { return optionsJson; }
        JsonNode? root;
        try { root = JsonNode.Parse(optionsJson); }
        catch (JsonException) { return optionsJson; }
        if (root is not JsonArray arr) { return optionsJson; }

        var changed = false;
        if (controlType == FormControlType.GridDetail)
        {
            foreach (var col in arr)
            {
                if (col is not JsonObject obj) { continue; }
                // 0) CANONICALIZA claves camelCase que el modelo suele escribir por analogia con el campo
                //    (calcExpression/aggregate/controlType) a las claves cortas del motor (calc/agg/type). Sin
                //    esto la columna se guarda muda: ParseColumns no la lee y el editor del disenador no la muestra.
                foreach (var (alias, canon) in new[] { ("calcExpression", "calc"), ("aggregate", "agg"), ("controlType", "type") })
                {
                    if (!obj.ContainsKey(canon) && obj.TryGetPropertyValue(alias, out var an) && an is JsonValue av
                        && av.TryGetValue<string>(out var asv) && !string.IsNullOrWhiteSpace(asv))
                    {
                        obj[canon] = asv;
                        obj.Remove(alias);
                        changed = true;
                    }
                }
                // 1) calc de columna -> sintaxis {codigo}
                if (obj.TryGetPropertyValue("calc", out var calcNode) && calcNode is JsonValue cv
                    && cv.TryGetValue<string>(out var calc) && !string.IsNullOrWhiteSpace(calc))
                {
                    var norm = FormExpressionEvaluator.NormalizeReferences(calc);
                    if (!string.Equals(norm, calc, StringComparison.Ordinal)) { obj["calc"] = norm; changed = true; }
                }
                // 2) options anidadas de una columna select/multicheck -> cada una {id,label}
                if (obj.TryGetPropertyValue("options", out var optsNode) && optsNode is JsonArray colOpts)
                {
                    changed |= NormalizeOptionArray(colOpts);
                }
            }
        }
        else if (controlType is FormControlType.Select or FormControlType.Radio or FormControlType.MultiCheck)
        {
            changed |= NormalizeOptionArray(arr);
        }

        return changed ? root.ToJsonString() : optionsJson;
    }

    // Normaliza EN SITIO un arreglo de opciones para que cada una sea {id,label} no vacios. Tolera:
    //  - string suelto "Cotizacion" -> {id:"cotizacion", label:"Cotizacion"}
    //  - claves alternativas (value/text/name/title/key) -> id/label
    //  - falta id -> id derivado del label; falta label -> label = id.
    // Devuelve true si cambio algo.
    private static bool NormalizeOptionArray(JsonArray options)
    {
        var changed = false;
        for (var i = 0; i < options.Count; i++)
        {
            var node = options[i];
            if (node is JsonValue val && val.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
            {
                options[i] = new JsonObject { ["id"] = SlugId(s), ["label"] = s.Trim() };
                changed = true;
                continue;
            }
            if (node is not JsonObject obj) { continue; }

            string? Pick(params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (obj.TryGetPropertyValue(k, out var n) && n is JsonValue v
                        && v.TryGetValue<string>(out var str) && !string.IsNullOrWhiteSpace(str)) { return str.Trim(); }
                }
                return null;
            }

            var label = Pick("label", "text", "name", "title", "nombre", "etiqueta");
            var idv = Pick("id", "value", "key", "valor", "clave");
            if (label is null && idv is null) { continue; } // no reconocible: no la tocamos

            label ??= idv;
            idv ??= SlugId(label!);

            var curId = (obj.TryGetPropertyValue("id", out var idn) && idn is JsonValue iv && iv.TryGetValue<string>(out var ids)) ? ids : null;
            var curLabel = (obj.TryGetPropertyValue("label", out var ln) && ln is JsonValue lv && lv.TryGetValue<string>(out var lbs)) ? lbs : null;
            if (!string.Equals(curId, idv, StringComparison.Ordinal)) { obj["id"] = idv; changed = true; }
            if (!string.Equals(curLabel, label, StringComparison.Ordinal)) { obj["label"] = label; changed = true; }
        }
        return changed;
    }

    // Slug estable para un id de opcion: minusculas, no-alfanumerico -> '_', sin bordes; vacio -> "opcion".
    private static string SlugId(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); }
            else if (sb.Length > 0 && sb[^1] != '_') { sb.Append('_'); }
        }
        var slug = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(slug) ? "opcion" : slug;
    }

    private async Task<AgentToolResult> AddQuestionAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var req = BuildQuestionRequest(args);
        if (string.IsNullOrWhiteSpace(req.FieldCode) || string.IsNullOrWhiteSpace(req.Label)) { return Err("Faltan 'field_code' y 'label'."); }
        if (HeaderGridCalcError(req) is { } gce) { return Err(gce); }
        var r = await _forms.AddQuestionAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, question = v });
    }

    private async Task<AgentToolResult> UpdateQuestionAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "question_id", out var id)) { return Err("Falta un 'question_id' valido."); }
        var req = BuildQuestionRequest(args);
        if (string.IsNullOrWhiteSpace(req.FieldCode) || string.IsNullOrWhiteSpace(req.Label)) { return Err("Faltan 'field_code' y 'label'."); }
        if (HeaderGridCalcError(req) is { } gce) { return Err(gce); }
        var r = await _forms.UpdateQuestionAsync(id, req, ct);
        return FormResp(r, v => new { ok = true, question = v });
    }

    // BLINDAJE: el calc_expression de un CAMPO (encabezado) NO puede referenciar una columna de grilla con
    // {#codigo} (ese token solo vale DENTRO de una columna de grilla, para leer el encabezado). El agente
    // insiste en poner subtotal.calc = {#items.total} para "sumar la columna", lo que no computa. Se rechaza con
    // el camino correcto (rollup), en vez de guardar un formulario roto.
    internal static string? HeaderGridCalcError(SaveFormQuestionRequest req)
    {
        if (req.ControlType == FormControlType.GridDetail) { return null; } // el calc de una grilla va en options_json
        if (req.CalcExpression is { } ce && ce.Contains("{#", StringComparison.Ordinal))
        {
            return $"El calc de un campo NO puede referenciar una columna de grilla con {{#...}} (eso solo vale " +
                $"DENTRO de una columna de grilla). Para SUMAR una columna de la tabla usa el ROLLUP: en esa columna " +
                $"pon agg=Sum + rollup=\"{req.FieldCode}\" y deja el campo '{req.FieldCode}' SIN calc_expression.";
        }
        return null;
    }

    // ---- AUTO-REVISION (verify_form) --------------------------------------------------------------
    // Un problema de coherencia hallado por verify_form. Severity "error" (bloquea: hay que corregirlo) o
    // "warn" (aviso: probablemente esta mal pero no lo damos por seguro).
    internal sealed record FormVerifyIssue(string Severity, string Where, string Problem, string Fix);

    private async Task<AgentToolResult> VerifyFormAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido (GUID)."); }
        var d = await _forms.GetAsync(id, ct);
        if (d is null) { return Err("No se encontro un formulario con ese id."); }
        var issues = VerifyForm(d);
        var errors = issues.Count(i => i.Severity == "error");
        return Ok(new
        {
            ok = errors == 0,
            errors,
            warnings = issues.Count - errors,
            message = errors > 0
                ? "Hay ERRORES que debes CORREGIR antes de dar por terminado el formulario."
                : issues.Count == 0 ? "Formulario coherente: sin problemas." : "Sin errores; revisa los avisos por si aplican.",
            issues = issues.Select(i => new { severity = i.Severity, where = i.Where, problem = i.Problem, fix = i.Fix })
        });
    }

    // Checks de coherencia PUROS (sin SQL, sin I/O) sobre una definicion ya leida. Solo reporta lo que
    // sabemos con certeza que rompe el formulario (error) o que casi seguro esta incompleto (warn); nada
    // que pueda dar falso positivo en un formulario correcto. internal para poder probarlo con datos armados.
    internal static IReadOnlyList<FormVerifyIssue> VerifyForm(FormDefinitionDetailDto d)
    {
        var issues = new List<FormVerifyIssue>();
        var headerCodes = new HashSet<string>(d.Questions.Select(q => q.FieldCode), StringComparer.OrdinalIgnoreCase);
        var byCode = new Dictionary<string, FormQuestionDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in d.Questions) { byCode.TryAdd(q.FieldCode, q); }

        // (grid, columna) -> field destino: rollups declarados en las grillas, para cotejarlos con el encabezado.
        var rollupTargets = new List<(string Grid, string Col, string Target)>();

        foreach (var q in d.Questions)
        {
            var isGrid = q.ControlType == FormControlType.GridDetail;

            // 1/2) CAMPO (encabezado) cuyo calc referencia una columna de grilla con {#...} (debe ser rollup),
            //      o un {codigo} inexistente (referencia colgante por typo).
            if (!isGrid && !string.IsNullOrWhiteSpace(q.CalcExpression))
            {
                foreach (var r in Refs(q.CalcExpression!))
                {
                    if (r.StartsWith('#'))
                    {
                        issues.Add(new("error", $"campo '{q.FieldCode}'",
                            $"su calc referencia una columna de grilla con {{{r}}}",
                            "Un campo NO suma una columna con {#...}. Usa ROLLUP: en la columna pon agg=Sum + rollup y deja este campo SIN calc."));
                    }
                    else if (!r.Contains('.') && !headerCodes.Contains(r))
                    {
                        issues.Add(new("error", $"campo '{q.FieldCode}'",
                            $"su calc referencia {{{r}}} que no es un campo del formulario",
                            "Referencia el field_code exacto de otro campo del encabezado."));
                    }
                }
            }

            // 3) Lookup sin fuente (DataContainer/Item/ExternalDataset requieren source_ref; Tercero no).
            if (q.SourceKind is FormSourceKind.DataContainer or FormSourceKind.Item or FormSourceKind.ExternalDataset
                && string.IsNullOrWhiteSpace(q.SourceRef))
            {
                issues.Add(new("error", $"campo '{q.FieldCode}'",
                    $"es un lookup {q.SourceKind} pero no tiene source_ref (fuente)",
                    "Pon source_ref con el id de la fuente (list_data_containers o la fuente que corresponda)."));
            }

            // 4) Lista de opciones fijas (Options) sin ninguna opcion.
            if (q.SourceKind == FormSourceKind.Options
                && q.ControlType is FormControlType.Select or FormControlType.Radio or FormControlType.MultiCheck
                && FormFieldValidator.ParseOptions(q.OptionsJson).Count == 0)
            {
                issues.Add(new("error", $"campo '{q.FieldCode}'",
                    "es una lista (Select/Radio/MultiCheck) de opciones fijas pero SIN opciones",
                    "Agrega options [{id,label}] o cambia el source_kind a la fuente correcta."));
            }

            // 5) Grilla: columnas y sus formulas/agregados.
            if (isGrid)
            {
                var cols = FormGridCalculator.ParseColumns(q.OptionsJson);
                if (cols.Count == 0)
                {
                    issues.Add(new("error", $"tabla '{q.FieldCode}'", "no tiene columnas definidas",
                        "Define las columnas en options_json ([{id,label,type,calc,agg,rollup}])."));
                }
                var colIds = new HashSet<string>(cols.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
                foreach (var c in cols)
                {
                    // 5a) calc de columna que referencia algo inexistente ({col} de la tabla o {#campo} del encabezado).
                    if (!string.IsNullOrWhiteSpace(c.Calc))
                    {
                        foreach (var r in Refs(c.Calc!))
                        {
                            if (r.StartsWith('#'))
                            {
                                var head = r[1..];
                                if (!head.Contains('.') && !headerCodes.Contains(head))
                                {
                                    issues.Add(new("warn", $"tabla '{q.FieldCode}', columna '{c.Id}'",
                                        $"su calc referencia el encabezado {{{r}}} que no existe",
                                        "Usa {#field_code} de un campo real del encabezado."));
                                }
                            }
                            else if (!r.Contains('.') && !colIds.Contains(r))
                            {
                                issues.Add(new("error", $"tabla '{q.FieldCode}', columna '{c.Id}'",
                                    $"su calc referencia {{{r}}} que no es una columna de la tabla",
                                    "Referencia una columna existente {col} o el encabezado {#campo}."));
                            }
                        }
                    }
                    // 5b) columna con agregado: con rollup lo cotejamos contra el encabezado; sin rollup, avisa.
                    if (c.Agg != FormAggregate.None)
                    {
                        if (!string.IsNullOrWhiteSpace(c.Rollup)) { rollupTargets.Add((q.FieldCode, c.Id, c.Rollup!)); }
                        else
                        {
                            issues.Add(new("warn", $"tabla '{q.FieldCode}', columna '{c.Id}'",
                                "tiene agregado (agg) pero sin rollup: el total no cae en ningun campo",
                                "Pon rollup=<field_code de un Number del encabezado> para volcar el total."));
                        }
                    }
                }
            }
        }

        // 6) Coherencia de cada rollup contra el encabezado (destino real y SIN calc que lo pise).
        foreach (var (grid, col, target) in rollupTargets)
        {
            if (!byCode.TryGetValue(target, out var tf))
            {
                issues.Add(new("error", $"tabla '{grid}', columna '{col}'",
                    $"su rollup apunta a '{target}', que no existe como campo del formulario",
                    "Crea el campo destino (Number) en el encabezado o corrige el rollup al field_code correcto."));
            }
            else if (!string.IsNullOrWhiteSpace(tf.CalcExpression))
            {
                issues.Add(new("error", $"campo '{target}'",
                    "es destino de un rollup PERO tiene calc_expression, que pisa el total de la columna",
                    "Quita el calc_expression de este campo: el rollup de la columna lo llena."));
            }
        }

        // 7) NaturalKey: el campo que porta el numero de negocio debe existir.
        if (d.IsTransactional && d.IdentityMode == FormIdentityMode.NaturalKey
            && (string.IsNullOrWhiteSpace(d.IdentitySourceFieldCode) || !headerCodes.Contains(d.IdentitySourceFieldCode)))
        {
            issues.Add(new("error", "identidad (NaturalKey)",
                $"identity_source_field_code '{d.IdentitySourceFieldCode}' no es un campo del formulario",
                "Fija identity_source_field_code a un field_code existente (el que porta el numero de negocio)."));
        }

        return issues;
    }

    // Extrae los codigos referenciados {codigo} o {#codigo} de una expresion calc (sin las llaves). Las
    // funciones (SI/REDONDEAR/...) y los numeros no van en llaves, asi que no se capturan (no dan falso positivo).
    private static IEnumerable<string> Refs(string expr)
        => System.Text.RegularExpressions.Regex
            .Matches(expr, "\\{(#?[A-Za-z0-9_.]+)\\}")
            .Select(m => m.Groups[1].Value);

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

    // Solo hex (#rgb/#rrggbb/#rrggbbaa): el color va a un bloque <style> del renderer, se valida contra inyeccion.
    private static readonly System.Text.RegularExpressions.Regex HexColor =
        new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private async Task<AgentToolResult> SetThemeAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var tema = (Str(args, "tema") ?? "clasico").Trim().ToLowerInvariant();
        if (tema is not ("clasico" or "prototipo")) { tema = "clasico"; }
        var color = Str(args, "color")?.Trim();
        if (!string.IsNullOrWhiteSpace(color) && !HexColor.IsMatch(color!)) { return Err($"'color' debe ser hex (#RRGGBB). Recibido: {color}"); }
        var hero = Bool(args, "hero") ?? false;
        // El renderer muestra el eyebrow como texto plano (no decodifica entidades HTML). Si el
        // agente copia una entidad de la fuente (ej. "&#9889; Gestion") saldria literal, asi que
        // la decodificamos aqui para que "&#9889;" se guarde ya como el caracter real (rayo).
        var eyebrow = System.Net.WebUtility.HtmlDecode(Str(args, "eyebrow")?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(eyebrow)) { eyebrow = null; }
        var hideChips = Bool(args, "hide_chips") ?? false;
        var cards = Bool(args, "cards") ?? false;

        // Todo por defecto -> tema clasico (theme_json null). Igual que FormThemeJson.Build del renderer.
        string? themeJson;
        if (tema == "clasico" && string.IsNullOrWhiteSpace(color) && !hero && string.IsNullOrWhiteSpace(eyebrow) && !hideChips && !cards)
        {
            themeJson = null;
        }
        else
        {
            var o = new JsonObject { ["tema"] = tema };
            if (!string.IsNullOrWhiteSpace(color)) { o["color"] = color; }
            if (hero) { o["hero"] = true; }
            if (!string.IsNullOrWhiteSpace(eyebrow)) { o["eyebrow"] = eyebrow; }
            if (hideChips) { o["hideChips"] = true; }
            if (cards) { o["cards"] = true; }
            themeJson = o.ToJsonString(JsonOut);
        }

        var r = await _forms.SetThemeAsync(id, themeJson, ct);
        return FormResp(r, v => new { ok = true, theme_json = themeJson, form = v });
    }

    private async Task<AgentToolResult> SetStatusLadderAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var id)) { return Err("Falta un 'form_id' valido."); }
        var json = Str(args, "status_ladder_json");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { using var _ = JsonDocument.Parse(json); }
            catch (JsonException) { return Err("'status_ladder_json' no es un JSON valido."); }
        }
        var r = await _forms.SetStatusLadderAsync(id, string.IsNullOrWhiteSpace(json) ? null : json, ct);
        return FormResp(r, v => new { ok = true, form = v });
    }

    private async Task<AgentToolResult> ListActivityTypesAsync(CancellationToken ct)
    {
        var list = await _db.ActivityTypes.AsNoTracking()
            .Where(a => !a.IsArchived)
            .OrderBy(a => a.Name)
            .Select(a => new { id = a.Id, name = a.Name })
            .ToListAsync(ct);
        return Ok(new { ok = true, total = list.Count, activity_types = list });
    }

    private async Task<AgentToolResult> WireSubmitTaskRuleAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var formId)) { return Err("Falta un 'form_id' valido."); }
        if (!TryGuid(args, "activity_type_id", out var atId)) { return Err("Falta 'activity_type_id' (ver list_activity_types)."); }
        var fixedTitle = Str(args, "fixed_title");
        var tableField = Str(args, "table_field_code");
        if (string.IsNullOrWhiteSpace(fixedTitle) && string.IsNullOrWhiteSpace(tableField))
        {
            return Err("Define el origen del titulo: 'fixed_title' (una tarea) o 'table_field_code' (una tarea por fila).");
        }
        var req = new CreateFormSubmitTaskRuleRequest(
            DefinitionId: formId,
            ActivityTypeId: atId,
            AssigneeTenantUserId: TryGuid(args, "assignee_user_id", out var au) ? au : null,
            TableFieldCode: tableField,
            TitleKey: Str(args, "title_key"),
            FixedTitle: fixedTitle,
            TitlePrefix: Str(args, "title_prefix"),
            AutoComplete: Bool(args, "auto_complete") ?? false);
        var r = await _rules.CreateFormSubmitTaskRuleAsync(req, ct);
        if (!r.IsOk || r.Value is null) { return Err($"No se pudo crear la regla al enviar: {r.Error}"); }
        return Ok(new { ok = true, submit_rule = r.Value });
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

    private async Task<AgentToolResult> WireConvertButtonAsync(JsonElement args, CancellationToken ct)
    {
        if (!TryGuid(args, "form_id", out var formId)) { return Err("Falta un 'form_id' valido."); }
        var targetCode = Str(args, "target_code")?.Trim();
        if (string.IsNullOrWhiteSpace(targetCode)) { return Err("Falta 'target_code' (codigo del formulario destino)."); }
        var label = Str(args, "button_label") ?? "Convertir";

        var form = await _forms.GetAsync(formId, ct);
        if (form is null) { return Err("No se encontro el formulario."); }

        // Aviso (no bloqueante): el verbo exige que el destino este ACTIVO al hacer clic.
        var targetExists = await _db.FormDefinitions.AnyAsync(d => d.Code == targetCode, ct);

        // 1) Documento de reglas (estable por formulario): crea o REUSA el existente por codigo (re-ejecutar
        //    no debe fallar: CreateDocument da Conflict si el codigo ya existe).
        var docCode = $"CONVERSION-{form.Code}";
        var docReq = new SaveRuleDocumentRequest(docCode, $"Conversion {form.Title}", "Conversion");
        var docRes = await _rules.CreateDocumentAsync(docReq, ct);
        Guid docId;
        if (docRes.IsOk && docRes.Value is not null) { docId = docRes.Value.Id; }
        else
        {
            var docs = await _rules.ListDocumentsAsync(true, ct);
            var existing = docs.FirstOrDefault(d => string.Equals(d.DocumentCode, docCode, StringComparison.OrdinalIgnoreCase));
            if (existing is null) { return Err($"No se pudo crear/obtener el documento de reglas: {docRes.Error}"); }
            docId = existing.Id;
        }

        // 2) Regla CONVERTIR_A_FORMULARIO {targetCode, mapping?, gridMapping?, gridDerive?, defaults?, openMode?}.
        var paramsObj = new JsonObject { ["targetCode"] = targetCode };
        AddJsonParam(paramsObj, "mapping", Str(args, "mapping_json"));
        AddJsonParam(paramsObj, "gridMapping", Str(args, "grid_mapping_json"));
        AddJsonParam(paramsObj, "gridDerive", Str(args, "grid_derive_json"));
        AddJsonParam(paramsObj, "defaults", Str(args, "defaults_json"));
        var openMode = Str(args, "open_mode");
        if (!string.IsNullOrWhiteSpace(openMode)) { paramsObj["openMode"] = openMode!.Trim(); }
        var ruleReq = new SaveRuleRequest($"Convertir a {targetCode}", "CONVERTIR_A_FORMULARIO", ParamsJson: paramsObj.ToJsonString(JsonOut));
        var ruleRes = await _rules.CreateRuleAsync(docId, ruleReq, ct);
        if (!ruleRes.IsOk || ruleRes.Value is null) { return Err($"No se pudo crear la regla: {ruleRes.Error}"); }

        // 3) Pregunta tipo Button.
        var fieldCode = Str(args, "field_code");
        if (string.IsNullOrWhiteSpace(fieldCode)) { fieldCode = $"btn_convertir_{targetCode!.ToLowerInvariant().Replace('-', '_')}"; }
        var qReq = new SaveFormQuestionRequest(
            ContainerId: TryGuid(args, "container_id", out var cid) ? cid : null,
            FieldCode: fieldCode!.Trim(), Label: label, ControlType: FormControlType.Button);
        var qRes = await _forms.AddQuestionAsync(formId, qReq, ct);
        if (!qRes.IsOk || qRes.Value is null) { return FormResp(qRes, v => new { ok = true }); }

        // 4) Enlazar la regla al boton.
        var linkRes = await _rules.LinkToQuestionAsync(ruleRes.Value.Id, qRes.Value.Id, 0, ct);
        if (!linkRes.IsOk) { return Err($"Se creo el boton pero no se pudo enlazar la regla: {linkRes.Error}"); }

        return Ok(new
        {
            ok = true,
            document_id = docId,
            rule_id = ruleRes.Value.Id,
            question_id = qRes.Value.Id,
            field_code = fieldCode,
            target_code = targetCode,
            target_exists = targetExists,
            note = targetExists ? null : $"OJO: no existe (aun) un formulario con codigo '{targetCode}'. El boton fallara al usarse hasta que el destino exista y este ACTIVO."
        });
    }

    // Parsea un *_json del agente y lo embebe como JSON REAL en los params de la regla. Tolerante: si el
    // modelo mando "casi-JSON" con comillas simples ({'items':[...]}) lo normaliza a JSON valido. Si aun asi
    // no parsea, guarda el texto crudo (para no perderlo). Vacio/omitido -> no se agrega.
    private static void AddJsonParam(JsonObject target, string key, string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) { return; }
        var node = TryParseJsonLenient(rawJson);
        target[key] = node ?? (JsonNode)rawJson.Trim();
    }

    // Intenta parsear JSON; si falla, reintenta cambiando comillas simples por dobles (formato que a veces
    // emite el modelo para objetos/arreglos con claves/valores simples, sin apostrofes internos).
    private static JsonNode? TryParseJsonLenient(string s)
    {
        try { return JsonNode.Parse(s); } catch (JsonException) { }
        var t = s.Trim();
        if (t.Length > 1 && (t[0] is '{' or '[') && t.Contains('\''))
        {
            try { return JsonNode.Parse(t.Replace('\'', '"')); } catch (JsonException) { }
        }
        return null;
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
