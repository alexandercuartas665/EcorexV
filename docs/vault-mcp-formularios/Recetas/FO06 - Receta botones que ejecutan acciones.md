---
tipo: receta
tema: formularios
---

# FO06 - Receta botones que ejecutan acciones

Volver a [[00 - LEEME PRIMERO]]. Base: [[FO02 - Reglas que son y cuando corren]].

Un boton es un CAMPO de tipo `Button` (la etiqueta es el texto del boton). Pulsarlo ejecuta las reglas vinculadas a
ese campo, en orden.

## Atajos de la guia general (crean boton + regla en una llamada)

- Imprimir/PDF de una plantilla: `wire_print_button`.
- Convertir en otro formulario (ej. cotizacion -> orden): `wire_convert_button`.

## Boton "Aprobar" que cierra el paso del flujo

1. El boton (estructura; `container_id` = la seccion donde va, de `get_form`):

```json
{"name":"apply_form_spec","arguments":{"form_id":"<form_id>","fields":[{"container_id":"<id_seccion>","field_code":"btn_aprobar","label":"Aprobar y enviar","control_type":"Button","width":4}]}}
```

2. La regla y el vinculo:

```json
{"name":"crear_regla","arguments":{"nombre":"Cerrar paso de aprobacion","verbo":"COMPLETAR_PASO_FLUJO","formulario":"SOL-GASTO","parametros":{"requireFields":["aprobador","valor_aprobado"],"requireMessage":"Indica quien aprueba y el valor antes de cerrar."}}}
{"name":"regla_en_campo","arguments":{"formulario":"SOL-GASTO","campo":"btn_aprobar","regla":"Cerrar paso de aprobacion"}}
```

- Si el paso actual es una COMPUERTA atendida, el boton debe decir a que salida va: `targetNodeBpmnId` = el id
  BPMN del nodo destino (`ver_flujo`, campo `bpmn`). Haz un boton por salida (ej. "Aprobar" y "Rechazar").
- El boton solo actua si el registro esta dentro de una tarea con flujo en curso.

## Boton que copia datos

```json
{"name":"crear_regla","arguments":{"nombre":"Copiar datos de facturacion","verbo":"PASAR_CAMPOS","formulario":"SOL-GASTO","parametros":{"mappings":[{"source":"nit","target":"nit_factura"},{"source":"razon_social","target":"nombre_factura"}]}}}
{"name":"regla_en_campo","arguments":{"formulario":"SOL-GASTO","campo":"btn_copiar","regla":"Copiar datos de facturacion"}}
```

## Reusar una regla con otros datos

Vincula la MISMA regla a otro boton/formulario y cambia solo lo necesario en ese vinculo:

```json
{"name":"parametros_regla_en_campo","arguments":{"formulario":"SOL-VIAJE","campo":"btn_aprobar","regla":"Cerrar paso de aprobacion","parametros":{"requireFields":["aprobador"]}}}
```

Verifica con `reglas_de_formulario` y, tras probar en pantalla, `historial_regla`.
