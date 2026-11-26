---
tipo: fundamento
tema: formularios
---

# FO09 - Cambios seguros y versiones

Volver a [[00 - LEEME PRIMERO]].

## Antes de cambiar un formulario en uso

1. `guardar_version_formulario` con un nombre que diga que vas a hacer ("Antes de agregar seccion de pagos").
2. Haz los cambios con `apply_form_spec` (o `update_question`: solo cambia lo que envias).
3. `verify_form` hasta 0 errores.
4. Si algo salio mal: `listar_versiones_formulario` -> `restaurar_version_formulario` (confirma con el usuario:
   reemplaza la estructura actual).

## Que NO hacer en un formulario con registros

- Cambiar el `field_code` a mano. Usa `renombrar_campo` (ver abajo).
- Borrar un campo con datos: preferible `campo_oculto`.
- Cambiar el tipo de control de un campo con datos (texto -> numero): los valores viejos pueden no leerse.

## Renombrar el nombre interno de un campo

`renombrar_campo` cambia el `field_code` EN CASCADA dentro del formulario: respuestas guardadas, calculos,
condicionales, grillas, autollenado, reglas propias del formulario y sus propiedades (codigo automatico, cierre,
columnas del modulo, KPIs). Todo en una sola transaccion.

1. Primero SIMULA (sin `aplicar`): devuelve `cambios` (donde y cuantos) y `advertencias`.
2. Muestra la simulacion al usuario. Las `advertencias` son referencias FUERA del formulario que NO se cambian
   solas: plantillas de impresion `{{campo.x}}`, `{form.x}` en avisos de flujo, agentes o documentos, reportes
   `form:{codigo}`, reglas compartidas con otros formularios. Hay que revisarlas a mano.
3. Con su OK, repite con `aplicar=true`.

```json
{"name":"renombrar_campo","arguments":{"formulario":"COT-VENTA","campo":"vlr","nuevo_codigo":"valor_total"}}
```

La etiqueta visible NO es el nombre interno: se cambia con `update_question` (`label`).

## Enlaces publicos

- `listar_enlaces_formulario` muestra los emitidos y su estado (vigente, usado, vencido, revocado).
- `revocar_enlace_formulario` corta uno (por ejemplo, si se compartio por error). Confirma antes.
- El enlace (la URL) solo se ve al crearlo (`create_share_link`); despues no se puede recuperar, solo revocar.

## Reglas

Antes de borrar una regla mira donde esta (`ver_regla`). Si ya corrio, no se puede borrar: desactivala con
`estado_regla`. Quitarla de un campo (`quitar_regla_de_campo`) no la borra.
