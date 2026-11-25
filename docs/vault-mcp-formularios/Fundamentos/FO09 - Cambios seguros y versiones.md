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

- Cambiar el `field_code` de un campo: los registros viejos, reglas, calculos, plantillas y reportes lo pierden.
  Crea un campo nuevo y oculta el viejo.
- Borrar un campo con datos: preferible `campo_oculto`.
- Cambiar el tipo de control de un campo con datos (texto -> numero): los valores viejos pueden no leerse.

## Enlaces publicos

- `listar_enlaces_formulario` muestra los emitidos y su estado (vigente, usado, vencido, revocado).
- `revocar_enlace_formulario` corta uno (por ejemplo, si se compartio por error). Confirma antes.
- El enlace (la URL) solo se ve al crearlo (`create_share_link`); despues no se puede recuperar, solo revocar.

## Reglas

Antes de borrar una regla mira donde esta (`ver_regla`). Si ya corrio, no se puede borrar: desactivala con
`estado_regla`. Quitarla de un campo (`quitar_regla_de_campo`) no la borra.
