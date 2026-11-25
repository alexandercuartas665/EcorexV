---
tipo: receta
modulo: formularios
referencia: "[[T09 - Formularios dinamicos]]"
---

# R04 - Crear un formulario completo

Volver a [[00 - LEEME PRIMERO]]. Referencia: [[T09 - Formularios dinamicos]]. Las herramientas de formularios
tienen nombres en **ingles**.

> Esta receta arma la ESTRUCTURA. El COMPORTAMIENTO (reglas al enviar / en campos / en botones, campos que se exigen
> segun otros, registros que se cierran al firmar, permisos por rol, versiones) esta en el cerebro de formularios:
> `guia_ecorex` con `tema` = `formularios`.

Ejemplo: **Solicitud de visita tecnica** con datos del cliente, ciudad (lista), equipos (grilla con total) y tarea
automatica al enviar.

## Paso 0 - Aprender el catalogo vivo (OBLIGATORIO)

```json
{"name":"describe_components","arguments":{}}
```
Devuelve: tipos de control (`Text, TextArea, Select, Radio, MultiCheck, Number, Date, DateTime, GridDetail,
Signature, Photo, File, Gps, Subform, Geografia, FixedMatrix, Button, ...`), tipos de contenedor, fuentes de listas
(`Options, DataContainer, Tercero, Item, ExternalDataset, SystemUser`), formato de columnas de grilla, sintaxis de
calculos, tokens de prellenado, marcadores de plantillas. **Consultalo cuando dudes** en vez de adivinar.

Luego: `list_forms` (no dupliques un `code`).

## Paso 1 - Cabecera

```json
{"name":"create_form","arguments":{"code":"SOL-VISITA","title":"Solicitud de visita tecnica",
  "description":"La llena el cliente o el comercial"}}
```
-> `form_id`.

## Paso 2 - Estructura y campos en UNA llamada (`apply_form_spec`)

Rejilla de 12: `width` 12 = fila completa, 6 = media, 4 = un tercio. Contenedores en orden (padres antes que
hijos), cada uno con una `key` tuya; los campos apuntan con `container_key`.

```json
{"name":"apply_form_spec","arguments":{
  "form_id":"<form_id>",
  "containers":[
    {"key":"cli","name":"Datos del cliente","container_type":"Section"},
    {"key":"eq","name":"Equipos a revisar","container_type":"Section"}
  ],
  "fields":[
    {"container_key":"cli","field_code":"cliente","label":"Empresa","control_type":"Text","width":6,"required":true,
     "default_value":"{tareas.cliente}"},
    {"container_key":"cli","field_code":"contacto","label":"Persona de contacto","control_type":"Text","width":6},
    {"container_key":"cli","field_code":"ciudad","label":"Ciudad","control_type":"Select","width":4,"required":true,
     "options_json":"[{\"id\":\"BOG\",\"label\":\"Bogota\"},{\"id\":\"MED\",\"label\":\"Medellin\"},{\"id\":\"OTRA\",\"label\":\"Otra\"}]"},
    {"container_key":"cli","field_code":"ciudad_otra","label":"Cual ciudad?","control_type":"Text","width":4,
     "visible_when_json":"{\"field\":\"ciudad\",\"op\":\"equals\",\"value\":\"OTRA\"}"},
    {"container_key":"cli","field_code":"fecha_visita","label":"Fecha deseada","control_type":"Date","width":4,
     "default_value":"{hoy+3}"},
    {"container_key":"eq","field_code":"equipos","label":"Equipos","control_type":"GridDetail","width":12,
     "options_json":"[{\"id\":\"equipo\",\"label\":\"Equipo\",\"type\":\"text\"},{\"id\":\"cantidad\",\"label\":\"Cantidad\",\"type\":\"number\"},{\"id\":\"valor\",\"label\":\"Valor unitario\",\"type\":\"number\",\"format\":\"currency\"},{\"id\":\"subtotal\",\"label\":\"Subtotal\",\"type\":\"calc\",\"calc\":\"{cantidad}*{valor}\",\"agg\":\"Sum\",\"rollup\":\"total\"}]"},
    {"container_key":"eq","field_code":"total","label":"Total","control_type":"Number","width":4,"format":"currency"}
  ]}}
```

Puntos clave del ejemplo:
- **Listas** (`Select/Radio/MultiCheck`): `options_json = [{"id":..,"label":..}]` - `id` obligatorio.
- **Mostrar/ocultar:** `visible_when_json = {"field":"codigo","op":"equals|notEquals|includes|empty|notEmpty","value":"x"}`.
- **Prellenado:** `default_value` con tokens `{tareas.cliente}`, `{tareas.nit}`, `{tareas.titulo}`, `{hoy}`,
  `{hoy+N}`, `{ahora}`, `{numero}` (los de tarea quedan vacios fuera de una tarea).
- **Calculos:** refs siempre `{codigo}`; funciones `SI(c;a;b)`, `REDONDEAR`, `MIN`, `MAX`. **Sumar una columna de
  grilla** = columna con `agg:"Sum"` + `rollup:"<campo destino>"`; el destino (`total`) NO lleva `calc_expression`.
- **Porcentajes:** `format:"percent"` guarda 5 = 5; para aplicarlo divide: `{valor}*(1-{dcto}/100)`.

`apply_form_spec` es **atomico** (si algo falla no queda nada a medias) e **idempotente**: re-aplicar la misma
spec ACTUALIZA (contenedor = mismo padre + nombre; campo = mismo `field_code`). Es la mejor forma de editar.

## Paso 3 - Listas desde datos del sistema (lookups)

- Desde un **contenedor**: `list_data_containers` (cada item es una tabla de datos: su id es el `source_ref`) ->
  `describe_data_container` (columnas por NOMBRE) -> campo:
```json
{"container_key":"cli","field_code":"producto","label":"Producto","control_type":"Select","width":6,
 "source_kind":"DataContainer","source_ref":"<id_de_list_data_containers>","display_field":"nombre","value_field":"codigo"}
```
- Desde el **directorio**: `source_kind:"Tercero"` (campos en `list_tercero_fields`).
- Desde **usuarios**: `source_kind:"SystemUser"`.
- Presentacion: `presentation` = `Autocomplete | Dropdown | Modal`. Autocompletar otros campos: `autofill_map_json`.
- VLOOKUP multi-clave: NO es formula; es una **columna `resolve`** dentro de una grilla (ver `describe_components`
  -> `grid_column_schema.resolve`).

## Paso 4 - Revisar y activar

```json
{"name":"verify_form","arguments":{"form_id":"<form_id>"}}
```
Corrige cada `error` (re-aplicando la spec) hasta `errors=0`. Luego:
```json
{"name":"activate","arguments":{"form_id":"<form_id>"}}
```

## Paso 5 - Opcionales frecuentes

- **Marca:** `set_theme` con `color:"#1F6FEB"` (tematiza todo), `tema:"prototipo"`, `hero:true`, `eyebrow:"Servicio"`.
- **Numerar registros** (ej. VIS-000001): `set_transactional` con `is_transactional:true`,
  `identity_mode:"Sequence"`, `identity_prefix:"VIS-"`, `identity_padding:6`.
- **Crear una tarea al enviar:** `list_activity_types` -> 
```json
{"name":"wire_submit_task_rule","arguments":{"form_id":"<form_id>","activity_type_id":"<id>",
  "fixed_title":"Visita tecnica solicitada","title_prefix":"VIS"}}
```
  (Una tarea POR FILA de una grilla: `table_field_code:"equipos"` + `title_key:"equipo"`.)
- **Imprimir/PDF:** `create_template` (`name`, `html` con `{{campo.cliente}}`, `{{numero}}`, `{{fecha}}`,
  bloques `{{#tabla.equipos}}...{{col.cantidad}}...{{/tabla.equipos}}`) -> `wire_print_button` (`template_name`,
  `format:"pdf"`). Verifica con `create_record` (submit=false) + `get_render_urls`.
- **Estados calculados:** `set_status_ladder` (el campo estado sube de escalon segun condiciones; nunca baja).
- **Restringir una seccion por cargo:** `list_org_units` -> `allowed_cargos_json` en el contenedor.
- **Convertir en otro formulario:** `wire_convert_button` (ej. cotizacion -> orden de trabajo).
- **Enlace publico** (confirma antes; el token sale UNA vez): `create_share_link` (`expiration_hours`,
  `single_use`, `allow_anonymous`).
- **Usar el formulario en un proceso:** `poner_formulario_a_nodo` ([[R03 - Disenar y publicar un flujo]]).

## Editar un formulario existente

1. `get_form` (definicion completa + `Version`).
2. Preferible: **re-aplicar `apply_form_spec`** con los campos a cambiar (upsert por `field_code`).
3. `update_question` es parcial: solo cambia los atributos que envias (el resto del campo se conserva).
4. `verify_form` de nuevo.

## Copiar un formulario

`export_form` (JSON) -> `import_form` (genera `code` unico; nunca pisa otro).
