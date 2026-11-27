---
tipo: referencia
servidor: ecorex-tableros
modulo: formularios
herramientas: 73
fuente: catalogo vivo tools/list (generado)
---

# MCP Tableros - Formularios dinamicos

Servidor: `/api/mcp/tableros` - ver [[01 - Conectar los MCP]]. Volver a [[00 - LEEME PRIMERO]].

## Para que sirve

Autoria de formularios (DynamicFormRenderer). Estos nombres van en INGLES (es otra familia de herramientas).

Orden de trabajo recomendado:
1. `describe_components` SIEMPRE primero: devuelve tipos de control, fuentes de listas, sintaxis de calculos,
   tokens de prellenado, columnas de grilla, etc. Es la "documentacion viva" de los formularios.
2. `create_form` (cabecera, `code` unico) -> `apply_form_spec` (secciones + campos en UNA llamada, atomica e
   idempotente) -> `verify_form` hasta `errors=0` -> `activate`.
3. Opcional: `set_theme` (marca), `wire_submit_task_rule` (crear tarea al enviar), `wire_print_button`
   (plantilla imprimible), `create_share_link` (enlace publico /f/{token} - confirma antes).

Recetas: [[R04 - Crear un formulario completo]]

## Herramientas (73)

- **Lectura:** `describe_components`, `list_forms`, `get_form`, `verify_form`, `list_templates`, `list_data_containers`, `describe_data_container`, `list_tercero_fields`, `list_org_units`, `list_activity_types`, `export_form`, `get_render_urls`, `listar_verbos_regla`, `listar_reglas`, `ver_regla`, `historial_regla`, `reglas_de_formulario`, `listar_versiones_formulario`, `listar_enlaces_formulario`
- **Escritura:** `apply_form_spec`, `create_form`, `import_form`, `update_form_header`, `add_container`, `update_container`, `move_container`, `add_question`, `update_question`, `move_question`, `set_transactional`, `set_sequence_next`, `set_custom_css`, `set_theme`, `set_status_ladder`, `wire_submit_task_rule`, `activate`, `deactivate`, `create_template`, `update_template`, `set_default_template`, `wire_print_button`, `wire_convert_button`, `create_share_link`, `create_record`, `crear_regla`, `parametros_regla`, `estado_regla`, `renombrar_regla`, `regla_al_enviar`, `regla_en_campo`, `parametros_regla_en_campo`, `condicion_entre_campos`, `ocultar_barra_envio`, `formulario_reportable`, `codigo_automatico_formulario`, `registro_editable`, `cierre_automatico_formulario`, `guardar_version_formulario`, `campo_bloqueado`, `campo_oculto`, `valor_por_defecto_dinamico`, `permisos_campo_por_rol`, `subformulario_de_campo`, `diseno_campo`
- **Destructivas:** `delete_container`, `delete_question`, `archive`, `eliminar_regla`, `quitar_regla_al_enviar`, `quitar_regla_de_campo`, `restaurar_version_formulario`, `revocar_enlace_formulario`, `renombrar_campo`

### `describe_components`

**Tipo:** Lectura

> Catalogo AUTO-DESCRIPTIVO (legible por maquina) de los componentes del constructor de formularios: enums (tipos de control, origenes de datos, modos de identidad, presentaciones, agregados, tipos de contenedor, layouts de tarjeta), que controles aceptan OptionsJson / soportan lookup / resolve / calc / format, el esquema de columnas de una grilla (con sub-esquemas lookup y resolve), las claves de lookup a nivel de campo, los marcadores de plantilla de impresion y los verbos/formatos de impresion. Llamala PRIMERO para construir sin leer codigo.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "describe_components",
  "arguments": {}
}
```

### `list_forms`

**Tipo:** Lectura

> Lista los formularios del tenant: id, code, titulo, estado, #preguntas, Version (token de concurrencia optimista), si es modulo/transaccional.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `include_archived` | boolean |  | Incluir archivados (por defecto false) |

Llamada minima (solo obligatorios):

```json
{
  "name": "list_forms",
  "arguments": {}
}
```

### `get_form`

**Tipo:** Lectura

> Devuelve la definicion COMPLETA de un formulario (cabecera + contenedores + preguntas) incluyendo su Version, para hacer updates con concurrencia optimista.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI | Id (GUID) del formulario |

Llamada minima (solo obligatorios):

```json
{
  "name": "get_form",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `verify_form`

**Tipo:** Lectura

> AUTO-REVISION de coherencia de un formulario ya construido (read-only). Devuelve la lista de PROBLEMAS: columna calc sin formula, rollup que apunta a un campo inexistente, un CAMPO que suma una columna con {#...} (deberia ser rollup), un campo destino de rollup que ademas tiene calc (lo pisa), lookup sin source_ref, lista Options sin opciones, referencias {codigo} colgantes, NaturalKey a un campo inexistente. Llamala AL TERMINAR de construir y CORRIGE cada 'error' que reporte antes de cerrar.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "verify_form",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `apply_form_spec`

**Tipo:** Escritura

> PREFERIDA para CONSTRUIR: aplica de UNA vez una seccion completa (o el formulario entero) como una spec DECLARATIVA en lugar de decenas de add_container/add_question. 'containers' = lista ORDENADA (padres antes que hijos) de contenedores con una 'key' corta tuya; los hijos referencian a su padre por 'parent_key' (o 'parent_id' si el padre ya existe). 'fields' = campos con 'container_key' (o 'container_id'). El SERVIDOR asigna los ids (nunca los adivinas), lo aplica de forma ATOMICA (si algo falla se revierte TODO, nada queda a medias) e IDEMPOTENTE: re-aplicar la misma spec ACTUALIZA en vez de duplicar (contenedor = mismo padre + mismo nombre; campo = mismo field_code). Devuelve conteos y el mapa key->id. Los campos aceptan los mismos atributos que add_question (field_code, label, control_type, width, required, options_json, calc_expression, aggregate, format, source_kind/source_ref, placeholder_text, help_text, visible_when_json...).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `containers` | array de objetos |  |  |
| `containers[].key` | string | SI |  |
| `containers[].name` | string | SI |  |
| `containers[].container_type` | string |  | Section/Row/Col/Tabs/Segment |
| `containers[].parent_key` | string |  |  |
| `containers[].parent_id` | string |  |  |
| `containers[].width` | integer |  |  |
| `containers[].style` | string |  |  |
| `containers[].inline_labels` | boolean |  |  |
| `containers[].allowed_cargos_json` | string |  |  |
| `containers[].visible_when_json` | string |  |  |
| `fields` | array de objetos |  |  |
| `fields[].container_key` | string |  |  |
| `fields[].container_id` | string |  |  |
| `fields[].field_code` | string | SI |  |
| `fields[].label` | string | SI |  |
| `fields[].control_type` | string | SI |  |
| `fields[].width` | integer |  |  |
| `fields[].required` | boolean |  |  |
| `fields[].options_json` | string |  |  |
| `fields[].placeholder_text` | string |  |  |
| `fields[].help_text` | string |  |  |
| `fields[].default_value` | string |  |  |
| `fields[].calc_expression` | string |  |  |
| `fields[].aggregate` | string |  |  |
| `fields[].format` | string |  |  |
| `fields[].source_kind` | string |  |  |
| `fields[].source_ref` | string |  |  |
| `fields[].display_field` | string |  |  |
| `fields[].value_field` | string |  |  |
| `fields[].visible_when_json` | string |  |  |
| `fields[].validation_json` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "apply_form_spec",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "apply_form_spec",
  "arguments": {
    "form_id": "<form_id>",
    "containers": [
      {
        "key": "datos",
        "name": "Datos del solicitante",
        "container_type": "Section"
      }
    ],
    "fields": [
      {
        "container_key": "datos",
        "field_code": "nombre",
        "label": "Nombre",
        "control_type": "Text",
        "width": 6,
        "required": true
      },
      {
        "container_key": "datos",
        "field_code": "ciudad",
        "label": "Ciudad",
        "control_type": "Select",
        "width": 6,
        "options_json": "[{\"id\":\"BOG\",\"label\":\"Bogota\"},{\"id\":\"MED\",\"label\":\"Medellin\"}]"
      }
    ]
  }
}
```

### `delete_container`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Elimina un CONTENEDOR (Section/Row/Col/Tabs) por su id. Sus preguntas y sub-contenedores NO se pierden: suben al padre del contenedor borrado (o a la raiz). Usala para quitar una fila/seccion que quedo VACIA, duplicada o sobrante (antes no habia forma y quedaban cascarones).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `container_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "delete_container",
  "arguments": {
    "container_id": "<container_id>"
  }
}
```

### `list_templates`

**Tipo:** Lectura

> Lista las plantillas de impresion del tenant (id, nombre, si es la predeterminada, si se envia como imagen).

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "list_templates",
  "arguments": {}
}
```

### `list_data_containers`

**Tipo:** Lectura

> Lista los contenedores de datos del tenant (id, nombre). Usa el id como 'sourceRef' de un lookup/resolve con source=DataContainer.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "list_data_containers",
  "arguments": {}
}
```

### `describe_data_container`

**Tipo:** Lectura

> Devuelve el ESQUEMA de un contenedor de datos: sus columnas (nombre + tipo) y cuantas filas tiene. Llamala ANTES de enlazar un lookup/resolve para saber que columnas usar como displayField/valueField (lista) o match/return (VLOOKUP): esos campos van por NOMBRE de columna. Acepta 'container_id' (GUID) o 'name'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `container_id` | string |  | Id (GUID) del contenedor |
| `name` | string |  | Nombre exacto (alternativa al id) |

Llamada minima (solo obligatorios):

```json
{
  "name": "describe_data_container",
  "arguments": {}
}
```

### `list_tercero_fields`

**Tipo:** Lectura

> Lista los campos disponibles de Tercero (Directorio) para autofill de un lookup con source=Tercero: base (nombre, identificacion, ciudad, email, telefono, vendedor, sector, cargo, estado) + los campos de ficha configurados.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "list_tercero_fields",
  "arguments": {}
}
```

### `list_org_units`

**Tipo:** Lectura

> Lista las Dependencias y Cargos del organigrama del tenant (id, nombre, classifier Dependencia|Cargo, parent). Usa estos ids en 'allowed_cargos_json' de add_container/update_container para restringir el acceso a una seccion.

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "list_org_units",
  "arguments": {}
}
```

### `list_activity_types`

**Tipo:** Lectura

> Lista los tipos de actividad/tarea del tenant (id, nombre). Usa el id como 'activity_type_id' de wire_submit_task_rule (la tarea que se crea al enviar el formulario).

_Sin parametros._

Llamada minima (solo obligatorios):

```json
{
  "name": "list_activity_types",
  "arguments": {}
}
```

### `create_form`

**Tipo:** Escritura

> Crea un formulario NUEVO (cabecera). Devuelve su id, code y Version. 'code' debe ser unico en el tenant.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `code` | string | SI | Codigo unico (ej. ENC-CLIMA) |
| `title` | string | SI |  |
| `description` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "create_form",
  "arguments": {
    "code": "<code>",
    "title": "<title>"
  }
}
```

Ejemplo real (valores genericos):

```json
{
  "name": "create_form",
  "arguments": {
    "code": "SOL-VISITA",
    "title": "Solicitud de visita tecnica"
  }
}
```

### `import_form`

**Tipo:** Escritura

> Crea un formulario COMPLETO de una vez desde un JSON exportado (el mismo formato de export_form). Genera un code unico; nunca pisa otro.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `json` | string | SI | JSON exportado del formulario |

Llamada minima (solo obligatorios):

```json
{
  "name": "import_form",
  "arguments": {
    "json": "<json>"
  }
}
```

### `export_form`

**Tipo:** Lectura

> Serializa un formulario completo (cabecera + contenedores + preguntas) a un JSON portable para respaldarlo o clonarlo con import_form.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "export_form",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `update_form_header`

**Tipo:** Escritura

> Actualiza titulo/descripcion del formulario. Requiere 'version' (concurrencia optimista) que entrega get_form.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `title` | string | SI |  |
| `description` | string |  |  |
| `version` | integer | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "update_form_header",
  "arguments": {
    "form_id": "<form_id>",
    "title": "<title>",
    "version": 1
  }
}
```

### `add_container`

**Tipo:** Escritura

> Agrega un contenedor (seccion/tabla/fila/columna/tabs/modal). container_type: Segment,Table,Row,Col,Section,Tabs,Modal. width en la rejilla de 12. visible_when_json (muestra/oculta por valor de otra pregunta) y allowed_cargos_json (restringe a Cargos/Dependencias, ids de list_org_units).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `name` | string | SI |  |
| `container_type` | string |  |  |
| `parent_id` | string |  | Contenedor padre (opcional; raiz si se omite) |
| `width` | integer |  |  |
| `inline_labels` | boolean |  |  |
| `style` | string |  |  |
| `visible_when_json` | string |  | Condicion de visibilidad {"field":"codigo","op":"equals/notEquals/includes/empty/notEmpty","value":"x"} |
| `allowed_cargos_json` | string |  | Arreglo JSON de ids de OrgUnit (Cargo/Dependencia) de list_org_units; vacio/omitido = sin restriccion |

Llamada minima (solo obligatorios):

```json
{
  "name": "add_container",
  "arguments": {
    "form_id": "<form_id>",
    "name": "<name>"
  }
}
```

### `update_container`

**Tipo:** Escritura

> Actualiza un contenedor por su id (nombre/tipo/ancho/estilo/visibilidad/acceso por cargo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `container_id` | string | SI |  |
| `name` | string | SI |  |
| `container_type` | string |  |  |
| `width` | integer |  |  |
| `inline_labels` | boolean |  |  |
| `style` | string |  |  |
| `parent_id` | string |  |  |
| `visible_when_json` | string |  |  |
| `allowed_cargos_json` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "update_container",
  "arguments": {
    "container_id": "<container_id>",
    "name": "<name>"
  }
}
```

### `move_container`

**Tipo:** Escritura

> Mueve un contenedor a otro padre (o a la raiz con parent_id vacio) en la posicion 'index'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `container_id` | string | SI |  |
| `parent_id` | string |  |  |
| `index` | integer | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "move_container",
  "arguments": {
    "container_id": "<container_id>",
    "index": 1
  }
}
```

### `add_question`

**Tipo:** Escritura

> Agrega una pregunta/campo. control_type, capacidades (options_json de Select/MultiCheck/GridDetail, lookup de campo con source_kind/source_ref/display_field/value_field/autofill_map_json, calc_expression+aggregate, format, default_value con tokens de prellenado) en describe_components. field_code unico en el formulario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `container_id` | string |  | Contenedor destino (opcional; raiz si se omite) |
| `field_code` | string | SI |  |
| `label` | string | SI |  |
| `control_type` | string | SI |  |
| `required` | boolean |  |  |
| `options_json` | string |  | JSON de opciones (Select/Radio/MultiCheck) o de columnas (GridDetail) |
| `help_text` | string |  |  |
| `placeholder_text` | string |  |  |
| `default_value` | string |  |  |
| `width` | integer |  |  |
| `source_kind` | string |  |  |
| `source_ref` | string |  |  |
| `display_field` | string |  |  |
| `value_field` | string |  |  |
| `filter_json` | string |  |  |
| `autofill_map_json` | string |  |  |
| `presentation` | string |  | Autocomplete/Dropdown/Modal |
| `calc_expression` | string |  |  |
| `aggregate` | string |  | None/Sum/Count/Avg/Min/Max |
| `format` | string |  |  |
| `validation_json` | string |  |  |
| `visible_when_json` | string |  | Muestra/oculta el campo segun otra pregunta {"field":"codigo","op":"equals/notEquals/includes/empty/notEmpty","value":"x"} |
| `is_read_only` | boolean |  | No editable para todos (tambien en /f/): solo lo llenan default, prellenado o reglas |
| `style_json` | string |  | Diseno del control: {labelSize,labelBold,labelItalic,labelColor,hideLabel,boxBg,borderColor,borderWidth,radius,height,align(left/center/right),marginTop,marginBottom,blockBg,valueSize,valueBold,valueColor}; colores #hex, medidas en px |

Llamada minima (solo obligatorios):

```json
{
  "name": "add_question",
  "arguments": {
    "form_id": "<form_id>",
    "field_code": "<field_code>",
    "label": "<label>",
    "control_type": "<control_type>"
  }
}
```

### `update_question`

**Tipo:** Escritura

> Actualiza una pregunta por su id. Mismos campos que add_question; solo cambia lo que envias (lo demas del campo se conserva). Un field_code distinto RENOMBRA en cascada: mejor usa renombrar_campo, que simula primero y avisa lo que queda por revisar.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `question_id` | string | SI |  |
| `container_id` | string |  |  |
| `field_code` | string | SI |  |
| `label` | string | SI |  |
| `control_type` | string | SI |  |
| `required` | boolean |  |  |
| `options_json` | string |  |  |
| `help_text` | string |  |  |
| `placeholder_text` | string |  |  |
| `default_value` | string |  |  |
| `width` | integer |  |  |
| `source_kind` | string |  |  |
| `source_ref` | string |  |  |
| `display_field` | string |  |  |
| `value_field` | string |  |  |
| `filter_json` | string |  |  |
| `autofill_map_json` | string |  |  |
| `presentation` | string |  |  |
| `calc_expression` | string |  |  |
| `aggregate` | string |  |  |
| `format` | string |  |  |
| `validation_json` | string |  |  |
| `visible_when_json` | string |  |  |
| `is_read_only` | boolean |  | No editable para todos (tambien en /f/): solo lo llenan default, prellenado o reglas |
| `style_json` | string |  | Diseno del control: {labelSize,labelBold,labelItalic,labelColor,hideLabel,boxBg,borderColor,borderWidth,radius,height,align(left/center/right),marginTop,marginBottom,blockBg,valueSize,valueBold,valueColor}; colores #hex, medidas en px |

Llamada minima (solo obligatorios):

```json
{
  "name": "update_question",
  "arguments": {
    "question_id": "<question_id>",
    "field_code": "<field_code>",
    "label": "<label>",
    "control_type": "<control_type>"
  }
}
```

### `move_question`

**Tipo:** Escritura

> Mueve una pregunta a otro contenedor (o a la raiz con container_id vacio) en la posicion 'index'.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `question_id` | string | SI |  |
| `container_id` | string |  |  |
| `index` | integer | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "move_question",
  "arguments": {
    "question_id": "<question_id>",
    "index": 1
  }
}
```

### `delete_question`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Elimina una pregunta por su id.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `question_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "delete_question",
  "arguments": {
    "question_id": "<question_id>"
  }
}
```

### `set_transactional`

**Tipo:** Escritura

> Configura la transaccionalidad: is_transactional + identity_mode (None|NaturalKey|Sequence). Para Sequence, identity_prefix + identity_padding definen el numero (ej. COT-000001). Para NaturalKey, identity_source_field_code apunta al campo clave. card_layout: Normal|Ancho|Completo.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `is_transactional` | boolean | SI |  |
| `identity_mode` | string | SI |  |
| `identity_source_field_code` | string |  |  |
| `card_layout` | string |  |  |
| `identity_prefix` | string |  |  |
| `identity_padding` | integer |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "set_transactional",
  "arguments": {
    "form_id": "<form_id>",
    "is_transactional": true,
    "identity_mode": "<identity_mode>"
  }
}
```

### `set_sequence_next`

**Tipo:** Escritura

> Fija el proximo numero del consecutivo (Sequence). Anti-colision: no permite bajarlo por debajo de uno ya emitido.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `next` | integer | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "set_sequence_next",
  "arguments": {
    "form_id": "<form_id>",
    "next": 1
  }
}
```

### `set_custom_css`

**Tipo:** Escritura

> Guarda el CSS personalizado de todo el formulario (pestana Estilos del disenador). Para colores/marca usa MEJOR set_theme (aplica la variable --brand que el renderer si consume). Si usas CSS, apunta a las clases REALES del renderer: dfr-segment/.dfr-seg-head (secciones), form-control (inputs), dfr-tabbar/.dfr-tab (pestanas), dfr-formbtn (botones). NO uses .form-section/.btn-primary/.nav-tabs (no existen).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `custom_css` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "set_custom_css",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `set_theme`

**Tipo:** Escritura

> APARIENCIA/TEMA del formulario (la forma correcta de aplicar la identidad de marca). 'color' (hex) es el COLOR DE MARCA: fija la variable --brand que TODO el renderer usa (encabezados de seccion, acentos, opt-cards, chips) -> tematiza el formulario entero de una. tema: clasico|prototipo (prototipo activa el look hero+rotulo). hero: encabezado tipo hero. eyebrow: rotulo pequeno arriba del titulo. hide_chips: oculta chips tecnicos. cards: estilo tarjetas para opciones (Radio/MultiCheck). Enviar todo vacio/omitido deja el tema clasico por defecto.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `color` | string |  | Color de marca hex (#RRGGBB): fija --brand y tematiza todo |
| `tema` | string |  | clasico/prototipo |
| `hero` | boolean |  |  |
| `eyebrow` | string |  | Rotulo pequeno sobre el titulo. Si lleva icono, usa el EMOJI directo (ej.  Gestion), NO entidades HTML como &#9889; |
| `hide_chips` | boolean |  |  |
| `cards` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "set_theme",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `set_status_ladder`

**Tipo:** Escritura

> Escalon de ESTADOS calculados del registro: un campo 'estado' toma la etiqueta del escalon MAS ALTO cuyas condiciones se cumplen (solo avanza, nunca baja). status_ladder_json = {"field":"<field_code destino>","states":[{"label":"Inicial","when":[]},{"label":"Siguiente","when":[{"field":"otro_codigo","op":"equals|notEquals|includes|notEmpty|empty","value":"x"}]}, ...]}. Estados ORDENADOS de menor a mayor; el primero (when vacio) es el piso. El 'field' destino es un campo del formulario (Text) que muestra el estado.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `status_ladder_json` | string |  | JSON del escalon {field,states[]}; vacio/omitido lo quita |

Llamada minima (solo obligatorios):

```json
{
  "name": "set_status_ladder",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `wire_submit_task_rule`

**Tipo:** Escritura

> REGLA AL ENVIAR que crea una TAREA/actividad cuando se envia el formulario (incl. la ruta publica /f/). activity_type_id (de list_activity_types) es el tipo de actividad a crear. Titulo: 'fixed_title' (una sola tarea) O 'table_field_code' (una tarea POR FILA de esa grilla, con 'title_key' = columna que da el titulo). 'assignee_user_id' opcional (a quien se asigna); 'title_prefix' opcional; 'auto_complete' la deja completada.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `activity_type_id` | string | SI |  |
| `assignee_user_id` | string |  |  |
| `fixed_title` | string |  |  |
| `table_field_code` | string |  |  |
| `title_key` | string |  |  |
| `title_prefix` | string |  |  |
| `auto_complete` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "wire_submit_task_rule",
  "arguments": {
    "form_id": "<form_id>",
    "activity_type_id": "<activity_type_id>"
  }
}
```

### `activate`

**Tipo:** Escritura

> Activa el formulario (Draft/Inactive -> Active), validando su estructura. Empieza a aceptar respuestas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "activate",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `deactivate`

**Tipo:** Escritura

> Desactiva el formulario (Active -> Inactive): deja de aceptar respuestas nuevas.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "deactivate",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `archive`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Archiva o desarchiva el formulario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `archived` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "archive",
  "arguments": {
    "form_id": "<form_id>",
    "archived": true
  }
}
```

### `create_template`

**Tipo:** Escritura

> Crea una plantilla de impresion HTML (usa marcadores {{campo.codigo}}, {{#tabla.x}}...{{/tabla.x}}, {{numero}},{{tarea}},{{fecha}},{{empresa}}, y {{barcode:numero|tarea|campo.x}}; ver describe_components). send_as_image=true la envia como imagen en vez de PDF.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `name` | string | SI |  |
| `html` | string | SI |  |
| `send_as_image` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "create_template",
  "arguments": {
    "name": "<name>",
    "html": "<html>"
  }
}
```

### `update_template`

**Tipo:** Escritura

> Actualiza una plantilla de impresion por su id.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `template_id` | string | SI |  |
| `name` | string | SI |  |
| `html` | string | SI |  |
| `send_as_image` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "update_template",
  "arguments": {
    "template_id": "<template_id>",
    "name": "<name>",
    "html": "<html>"
  }
}
```

### `set_default_template`

**Tipo:** Escritura

> Marca una plantilla como la predeterminada del tenant.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `template_id` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "set_default_template",
  "arguments": {
    "template_id": "<template_id>"
  }
}
```

### `wire_print_button`

**Tipo:** Escritura

> En UNA operacion: crea (o reusa) un documento de reglas, una regla IMPRIMIR_PLANTILLA {template,format}, una pregunta tipo Button y los enlaza, dejando el boton de imprimir en el formulario. format: print|pdf|img.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `template_name` | string | SI |  |
| `format` | string |  | print/pdf/img |
| `button_label` | string |  |  |
| `container_id` | string |  | Contenedor donde poner el boton (opcional) |
| `field_code` | string |  | Codigo del campo Button (opcional; se genera si se omite) |

Llamada minima (solo obligatorios):

```json
{
  "name": "wire_print_button",
  "arguments": {
    "form_id": "<form_id>",
    "template_name": "<template_name>"
  }
}
```

### `wire_convert_button`

**Tipo:** Escritura

> En UNA operacion deja un boton 'Convertir a otro formulario' (CONVERTIR_A_FORMULARIO, ADR-0078): crea documento de reglas + regla + pregunta Button + enlace. Copia los datos mapeables del registro actual a un NUEVO registro del formulario destino (por codigo) y lo abre. target_code: codigo EXACTO del formulario destino (activo). mapping_json: {campoOrigen:campoDestino} solo para los que cambian de nombre (los de igual codigo se copian solos). grid_mapping_json: {grilla:{colOrigen:colDestino}}. grid_derive_json (AUTO-MARCADO de columnas de grilla al convertir): {grilla:[{target,from,when,set}]}; por fila, si 'when' se cumple sobre la columna 'from' pone 'set' en 'target' (si no, vacio). when: '>N' (numerico mayor que N), '=<valor>' (igualdad, ej '=SI'), 'notempty'. set por defecto 'X'. defaults_json: {campoDestino:valor} para rellenar lo que no viene del origen; admite tokens @usuario.nombre/@usuario.email/@fecha.hoy/@fecha.hora.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `target_code` | string | SI |  |
| `button_label` | string |  |  |
| `mapping_json` | string |  |  |
| `grid_mapping_json` | string |  |  |
| `grid_derive_json` | string |  | gridDerive {grilla:[{target,from,when,set}]} |
| `defaults_json` | string |  |  |
| `open_mode` | string |  |  |
| `container_id` | string |  |  |
| `field_code` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "wire_convert_button",
  "arguments": {
    "form_id": "<form_id>",
    "target_code": "<target_code>"
  }
}
```

### `create_share_link`

**Tipo:** Escritura

> Emite un enlace publico /f/{token} para llenar el formulario. Devuelve el token EN CLARO una sola vez (en BD va solo el hash). expiration_hours, single_use, allow_anonymous, reference opcionales. response_per_visit=true: cada visita crea su propia respuesta (captacion publica de leads; reference queda como rotulo del enlace).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `expiration_hours` | integer |  |  |
| `single_use` | boolean |  |  |
| `allow_anonymous` | boolean |  |  |
| `response_per_visit` | boolean |  |  |
| `reference` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "create_share_link",
  "arguments": {
    "form_id": "<form_id>"
  }
}
```

### `create_record`

**Tipo:** Escritura

> Crea un registro (respuesta) del formulario con 'data' (mapa field_code -> valor). submit=true valida y confirma (asigna record_number si es transaccional); submit=false lo deja en borrador.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `form_id` | string | SI |  |
| `data` | object | SI | Mapa field_code -> valor (escalar) o {value,type} |
| `submit` | boolean |  |  |
| `reference` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "create_record",
  "arguments": {
    "form_id": "<form_id>",
    "data": {}
  }
}
```

### `get_render_urls`

**Tipo:** Lectura

> Devuelve las URLs de vista/pdf/img de un registro (para verificar la impresion). template_id opcional (usa la predeterminada si se omite).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `response_id` | string | SI |  |
| `template_id` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "get_render_urls",
  "arguments": {
    "response_id": "<response_id>"
  }
}
```

### `listar_verbos_regla`

**Tipo:** Lectura

> Catalogo de VERBOS del motor de reglas (lo que una regla puede hacer: crear tarea, pasar campos, notificar, completar paso del flujo, imprimir, convertir a otro formulario...) con sus PARAMETROS (nombre, tipo, obligatorio, opciones). Leelo antes de crear_regla o regla_al_enviar.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `buscar` | string |  |  |
| `verbo` | string |  | Un verbo concreto (detalle completo) |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_verbos_regla",
  "arguments": {}
}
```

### `listar_reglas`

**Tipo:** Lectura

> Reglas del tenant (nombre, verbo, estado, documento). 'buscar' filtra por nombre, verbo o documento.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `buscar` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_reglas",
  "arguments": {}
}
```

### `ver_regla`

**Tipo:** Lectura

> Una regla completa: verbo, parametros, estado, donde esta vinculada (campos de formularios y nodos de flujo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `regla` | string | SI | Id o nombre (ver listar_reglas) |

Llamada minima (solo obligatorios):

```json
{
  "name": "ver_regla",
  "arguments": {
    "regla": "<regla>"
  }
}
```

### `historial_regla`

**Tipo:** Lectura

> Ultimas ejecuciones de una regla (estado, registros afectados, error). Sirve para verificar que corrio.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `regla` | string | SI | Id o nombre (ver listar_reglas) |
| `limite` | integer >=1 <=100 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "historial_regla",
  "arguments": {
    "regla": "<regla>"
  }
}
```

### `crear_regla`

**Tipo:** Escritura

> Crea una regla con un VERBO y sus parametros (valida que el verbo exista y los obligatorios). Con 'formulario' queda en el documento de reglas de ese formulario; si no, en 'documento' o en 'Reglas creadas por IA'. Nace ACTIVA salvo estado=desarrollo. Para que actue, vinculala: regla_en_campo (campo o boton) o agregar_regla_nodo. Para el ENVIO del formulario usa mejor regla_al_enviar.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `nombre` | string | SI |  |
| `verbo` | string | SI |  |
| `parametros` | object |  | Parametros del verbo (nombres de listar_verbos_regla) |
| `formulario` | string |  | Id, codigo o titulo (ver list_forms) |
| `documento` | string |  | Codigo o nombre de un documento de reglas existente |
| `descripcion` | string |  |  |
| `estado` | string (activa / desarrollo) |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "crear_regla",
  "arguments": {
    "nombre": "<nombre>",
    "verbo": "<verbo>"
  }
}
```

### `parametros_regla`

**Tipo:** Escritura

> REEMPLAZA los parametros de una regla (envia el objeto completo; valida contra el verbo).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `regla` | string | SI | Id o nombre (ver listar_reglas) |
| `parametros` | object | SI | Parametros del verbo (nombres de listar_verbos_regla) |

Llamada minima (solo obligatorios):

```json
{
  "name": "parametros_regla",
  "arguments": {
    "regla": "<regla>",
    "parametros": {}
  }
}
```

### `estado_regla`

**Tipo:** Escritura

> activa = corre; desarrollo = solo en prueba manual; inactiva = nunca corre.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `regla` | string | SI | Id o nombre (ver listar_reglas) |
| `estado` | string (activa / desarrollo / inactiva) | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "estado_regla",
  "arguments": {
    "regla": "<regla>",
    "estado": "activa"
  }
}
```

### `renombrar_regla`

**Tipo:** Escritura

> Cambia el nombre de una regla (max 100).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `regla` | string | SI | Id o nombre (ver listar_reglas) |
| `nombre` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "renombrar_regla",
  "arguments": {
    "regla": "<regla>",
    "nombre": "<nombre>"
  }
}
```

### `eliminar_regla`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Borra una regla y sus vinculos. Solo si nunca se ha ejecutado (si tiene historial, usa estado_regla inactiva). Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `regla` | string | SI | Id o nombre (ver listar_reglas) |

Llamada minima (solo obligatorios):

```json
{
  "name": "eliminar_regla",
  "arguments": {
    "regla": "<regla>"
  }
}
```

### `reglas_de_formulario`

**Tipo:** Lectura

> Todas las reglas de un formulario: las que corren AL ENVIAR y las de cada campo/boton (con sus parametros propios del vinculo, si los tiene).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |

Llamada minima (solo obligatorios):

```json
{
  "name": "reglas_de_formulario",
  "arguments": {
    "formulario": "<formulario>"
  }
}
```

### `regla_al_enviar`

**Tipo:** Escritura

> Al ENVIAR el formulario se ejecuta este verbo (ej. CREAR_TAREA, NOTIFICAR, GENERAR_TAREAS_DESDE_TABLA, COMPLETAR_PASO_FLUJO). Una regla por verbo y formulario: si ya existe, actualiza nombre y parametros. Corre tambien en los enlaces publicos /f/.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `verbo` | string | SI |  |
| `nombre` | string | SI |  |
| `parametros` | object |  | Parametros del verbo (nombres de listar_verbos_regla) |

Llamada minima (solo obligatorios):

```json
{
  "name": "regla_al_enviar",
  "arguments": {
    "formulario": "<formulario>",
    "verbo": "<verbo>",
    "nombre": "<nombre>"
  }
}
```

### `quitar_regla_al_enviar`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Quita una regla del envio (y la borra si nunca corrio). 'regla' = nombre, id o verbo. Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `regla` | string | SI | Id o nombre (ver listar_reglas) |

Llamada minima (solo obligatorios):

```json
{
  "name": "quitar_regla_al_enviar",
  "arguments": {
    "formulario": "<formulario>",
    "regla": "<regla>"
  }
}
```

### `regla_en_campo`

**Tipo:** Escritura

> Vincula una regla existente a un CAMPO (corre al cambiar su valor) o a un BOTON (corre al pulsarlo; un boton es un campo de tipo Button; agregalo con apply_form_spec). Crea la regla antes con crear_regla.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `regla` | string | SI | Id o nombre (ver listar_reglas) |

Llamada minima (solo obligatorios):

```json
{
  "name": "regla_en_campo",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>",
    "regla": "<regla>"
  }
}
```

### `quitar_regla_de_campo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Desvincula una regla de un campo o boton (la regla sigue existiendo). Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `regla` | string | SI | Id o nombre (ver listar_reglas) |

Llamada minima (solo obligatorios):

```json
{
  "name": "quitar_regla_de_campo",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>",
    "regla": "<regla>"
  }
}
```

### `parametros_regla_en_campo`

**Tipo:** Escritura

> Parametros PROPIOS de este vinculo (sobrescriben los de la regla solo en este campo/formulario; util para reusar una regla en varios formularios). Sin 'parametros' los quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `regla` | string | SI | Id o nombre (ver listar_reglas) |
| `parametros` | object |  | Parametros del verbo (nombres de listar_verbos_regla) |

Llamada minima (solo obligatorios):

```json
{
  "name": "parametros_regla_en_campo",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>",
    "regla": "<regla>"
  }
}
```

### `condicion_entre_campos`

**Tipo:** Escritura

> Regla de UI: si 'si_campo' cumple 'operador' ('valor'), entonces 'efecto' sobre 'campo' (ocultar, mostrar, requerir u opcional). Se evalua mientras se llena. Para solo ocultar/mostrar tambien sirve visible_when_json.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `si_campo` | string | SI |  |
| `operador` | string (igual / distinto / vacio / no_vacio) | SI |  |
| `valor` | string |  |  |
| `efecto` | string (ocultar / mostrar / requerir / opcional) | SI |  |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |

Llamada minima (solo obligatorios):

```json
{
  "name": "condicion_entre_campos",
  "arguments": {
    "formulario": "<formulario>",
    "si_campo": "<si_campo>",
    "operador": "igual",
    "efecto": "ocultar",
    "campo": "<campo>"
  }
}
```

### `ocultar_barra_envio`

**Tipo:** Escritura

> activo=true oculta Enviar/Imprimir/autoguardado cuando el formulario se llena dentro de la creacion de una tarea.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "ocultar_barra_envio",
  "arguments": {
    "formulario": "<formulario>",
    "activo": true
  }
}
```

### `formulario_reportable`

**Tipo:** Escritura

> activo=true lo vuelve fuente de reportes (form:{codigo}) sin hacerlo modulo del menu.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "formulario_reportable",
  "arguments": {
    "formulario": "<formulario>",
    "activo": true
  }
}
```

### `codigo_automatico_formulario`

**Tipo:** Escritura

> Al crear un registro llena 'campo' con iniciales del asignado + consecutivo (ancho de 1 a 12). activo=false lo quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | SI |  |
| `campo` | string |  | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `ancho` | integer >=1 <=12 |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "codigo_automatico_formulario",
  "arguments": {
    "formulario": "<formulario>",
    "activo": true
  }
}
```

### `registro_editable`

**Tipo:** Escritura

> activo=true: el registro queda siempre editable (autoguardado, nunca se cierra por envio).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "registro_editable",
  "arguments": {
    "formulario": "<formulario>",
    "activo": true
  }
}
```

### `cierre_automatico_formulario`

**Tipo:** Escritura

> Al guardar, si 'campo' cumple 'operador' ('valor'), el registro se ENVIA y cierra solo (ej. al firmar). Sin 'campo' quita el cierre automatico.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string |  | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `operador` | string (no_vacio / vacio / igual / distinto / contiene) |  |  |
| `valor` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "cierre_automatico_formulario",
  "arguments": {
    "formulario": "<formulario>"
  }
}
```

### `guardar_version_formulario`

**Tipo:** Escritura

> Guarda una VERSION del formulario (estructura completa) para poder volver a ella. Hazlo antes de cambios grandes.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `nombre` | string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "guardar_version_formulario",
  "arguments": {
    "formulario": "<formulario>"
  }
}
```

### `listar_versiones_formulario`

**Tipo:** Lectura

> Versiones guardadas del formulario (id, nombre, fecha).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_versiones_formulario",
  "arguments": {
    "formulario": "<formulario>"
  }
}
```

### `restaurar_version_formulario`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Vuelve el formulario a una version guardada ('version' = id de listar_versiones_formulario). Reemplaza la estructura actual: guarda una version antes y confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `version` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "restaurar_version_formulario",
  "arguments": {
    "formulario": "<formulario>",
    "version": "<version>"
  }
}
```

### `listar_enlaces_formulario`

**Tipo:** Lectura

> Enlaces publicos emitidos del formulario (create_share_link): vigencia, uso unico, usado, revocado.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |

Llamada minima (solo obligatorios):

```json
{
  "name": "listar_enlaces_formulario",
  "arguments": {
    "formulario": "<formulario>"
  }
}
```

### `revocar_enlace_formulario`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Revoca un enlace publico ('enlace' = id de listar_enlaces_formulario): deja de abrir. Confirma con el usuario.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `enlace` | string | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "revocar_enlace_formulario",
  "arguments": {
    "formulario": "<formulario>",
    "enlace": "<enlace>"
  }
}
```

### `campo_bloqueado`

**Tipo:** Escritura

> activo=true: el campo se ve pero NADIE lo edita (sin importar el rol, tambien en el enlace publico /f/); su valor solo lo ponen el valor por defecto, el prellenado o las reglas. Solo para algunos roles usa permisos_campo_por_rol.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "campo_bloqueado",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>",
    "activo": true
  }
}
```

### `campo_oculto`

**Tipo:** Escritura

> activo=true: el campo existe y guarda valor pero no se muestra (util para calculos o datos que pone una regla).

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `activo` | boolean | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "campo_oculto",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>",
    "activo": true
  }
}
```

### `valor_por_defecto_dinamico`

**Tipo:** Escritura

> Valor inicial que se calcula al abrir: hoy, usuario_actual, entidad_actual, usuario_asignado; ninguno lo quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `valor` | string (ninguno / hoy / usuario_actual / entidad_actual / usuario_asignado) | SI |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "valor_por_defecto_dinamico",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>",
    "valor": "ninguno"
  }
}
```

### `permisos_campo_por_rol`

**Tipo:** Escritura

> Por ROL del usuario: 'ocultar_a' = roles que no lo ven; 'solo_lectura_a' = roles que lo ven sin editar. Roles: Owner, Admin, Supervisor, Advisor. Listas vacias = sin restriccion. Reemplaza lo anterior.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `ocultar_a` | array de string |  |  |
| `solo_lectura_a` | array de string |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "permisos_campo_por_rol",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>"
  }
}
```

### `subformulario_de_campo`

**Tipo:** Escritura

> En un campo de tipo Subform: el formulario HIJO (maestro-detalle) que se llena dentro. Sin 'subformulario' lo quita.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `subformulario` | string |  | Formulario hijo (id, codigo o titulo) |

Llamada minima (solo obligatorios):

```json
{
  "name": "subformulario_de_campo",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>"
  }
}
```

### `diseno_campo`

**Tipo:** Escritura

> Diseno visual de UN control: etiqueta (tamano, negrita, cursiva, color, ocultar), caja (fondo, borde, grosor, redondeo, alto, alineacion), valor (tamano, negrita, color) y bloque (margenes, fondo). Solo cambia las claves que envias en 'estilo'; 'quitar' devuelve claves al tema y limpiar=true vuelve al estilo del tema. Colores #hex, medidas en px; lo invalido se descarta.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `estilo` | object |  |  |
| `estilo.labelSize` | integer |  | 8-48 px |
| `estilo.labelBold` | boolean |  |  |
| `estilo.labelItalic` | boolean |  |  |
| `estilo.labelColor` | string |  | #hex |
| `estilo.hideLabel` | boolean |  |  |
| `estilo.boxBg` | string |  | #hex |
| `estilo.borderColor` | string |  | #hex |
| `estilo.borderWidth` | integer |  | 0-8 px |
| `estilo.radius` | integer |  | 0-40 px |
| `estilo.height` | integer |  | 20-400 px |
| `estilo.align` | string (left / center / right) |  |  |
| `estilo.marginTop` | integer |  | 0-120 px |
| `estilo.marginBottom` | integer |  | 0-120 px |
| `estilo.blockBg` | string |  | #hex |
| `estilo.valueSize` | integer |  | 8-64 px |
| `estilo.valueBold` | boolean |  |  |
| `estilo.valueColor` | string |  | #hex |
| `quitar` | array de string |  | Claves que vuelven al tema |
| `limpiar` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "diseno_campo",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>"
  }
}
```

### `renombrar_campo`

**Tipo:** DESTRUCTIVA (confirma con el usuario)

> Cambia el NOMBRE INTERNO (field_code) de un campo EN CASCADA dentro del formulario: respuestas guardadas, calculos, condicionales, grillas, autollenado, reglas propias y propiedades. aplicar=false (por defecto) solo SIMULA y lista lo que cambiaria y las ADVERTENCIAS (plantillas, avisos de flujo, reportes y reglas compartidas que hay que revisar a mano). Muestra la simulacion al usuario y repite con aplicar=true. La etiqueta visible se cambia con update_question.

| Parametro | Tipo | Oblig. | Detalle |
|---|---|---|---|
| `formulario` | string | SI | Id, codigo o titulo (ver list_forms) |
| `campo` | string | SI | Codigo del campo (field_code), id o etiqueta (ver get_form) |
| `nuevo_codigo` | string | SI | minusculas, numeros y _ |
| `aplicar` | boolean |  |  |

Llamada minima (solo obligatorios):

```json
{
  "name": "renombrar_campo",
  "arguments": {
    "formulario": "<formulario>",
    "campo": "<campo>",
    "nuevo_codigo": "<nuevo_codigo>"
  }
}
```
