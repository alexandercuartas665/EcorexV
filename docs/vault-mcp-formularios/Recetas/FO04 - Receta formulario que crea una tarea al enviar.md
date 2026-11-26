---
tipo: receta
tema: formularios
---

# FO04 - Receta formulario que crea una tarea al enviar

Volver a [[00 - LEEME PRIMERO]]. Base: [[FO02 - Reglas que son y cuando corren]].

Caso: un formulario publico de **registro de lead** (nombre, empresa, correo, telefono, necesidad). Al enviarlo se
crea una tarea del concepto "Lead web": cae en el tablero de ese concepto y, si el concepto tiene flujo, lo arranca.

## 1. Estructura (guia general R04)

`create_form` + `apply_form_spec` con los campos `nombre`, `empresa`, `correo`, `telefono`, `necesidad` y un campo
`tarea_creada` (texto) que servira de anti-duplicado. Luego:

```json
{"name":"campo_oculto","arguments":{"formulario":"REG-LEAD","campo":"tarea_creada","activo":true}}
```

## 2. El concepto destino

`listar_conceptos` -> elige el concepto (ej. codigo `LEAD-WEB`). Si no existe, se crea en pantalla (Conceptos); dilo
al usuario. Ese concepto define el tablero y, si lo tiene, el flujo que arranca.

## 3. La regla al enviar

```json
{"name":"listar_verbos_regla","arguments":{"verbo":"CREAR_TAREA"}}
{"name":"regla_al_enviar","arguments":{"formulario":"REG-LEAD","verbo":"CREAR_TAREA","nombre":"Crear tarea de lead","parametros":{"subcategoriaId":"LEAD-WEB","titleTemplate":"Lead - {nombre} - {empresa}","descriptionTemplate":"Necesidad: {necesidad}","requesterNameField":"nombre","requesterEmailField":"correo","requesterPhoneField":"telefono","marcaCreadaField":"tarea_creada","assigneeUserId":"comercial@empresa.test"}}}
```

- `subcategoriaId` acepta el codigo o nombre del concepto (el MCP lo traduce a su id).
- `requester*Field`: el contacto de la tarea sale de esos campos (asi los avisos al cliente del flujo funcionan).
- `marcaCreadaField`: si el campo ya tiene valor no crea otra tarea; tras crear escribe ahi el numero.
- `assigneeUserId` (correo): **obligatorio en formularios ANONIMOS** (enlace publico): ahi no hay usuario que la reciba.
- Repetir `regla_al_enviar` con el mismo verbo ACTUALIZA la regla (no duplica).

## 4. Activar, publicar y probar

```json
{"name":"activate","arguments":{"form_id":"<form_id>"}}
{"name":"create_share_link","arguments":{"form_id":"<form_id>","expiration_hours":720,"allow_anonymous":true}}
{"name":"reglas_de_formulario","arguments":{"formulario":"REG-LEAD"}}
```

Prueba con un registro (avisa al usuario: crea una tarea REAL) y revisa `historial_regla` de "Crear tarea de lead":
debe decir Success y 1 registro afectado.

## Variante: una tarea por fila de una grilla

`GENERAR_TAREAS_DESDE_TABLA` con `activityTypeId` (nombre del tipo de actividad) + `tableField` (la grilla) +
`titleKey` (columna para el titulo). El atajo de la guia general `wire_submit_task_rule` hace lo mismo.
