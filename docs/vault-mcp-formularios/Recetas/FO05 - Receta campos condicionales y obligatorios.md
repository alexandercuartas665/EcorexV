---
tipo: receta
tema: formularios
---

# FO05 - Receta campos condicionales y obligatorios

Volver a [[00 - LEEME PRIMERO]].

## Opcion A: solo mostrar u ocultar (en la estructura)

En el campo (via `apply_form_spec` o `update_question`):
`visible_when_json = {"field":"ciudad","op":"equals","value":"OTRA"}`. Operadores: `equals`, `notEquals`,
`includes`, `empty`, `notEmpty`. Sirve tambien en secciones. Es lo mas simple y rapido.

## Opcion B: mostrar, ocultar, EXIGIR u opcional (regla de UI)

```json
{"name":"condicion_entre_campos","arguments":{"formulario":"SOL-CREDITO","si_campo":"tipo","operador":"igual","valor":"Rechazo","efecto":"requerir","campo":"motivo"}}
{"name":"condicion_entre_campos","arguments":{"formulario":"SOL-CREDITO","si_campo":"tipo","operador":"distinto","valor":"Rechazo","efecto":"opcional","campo":"motivo"}}
```

- `operador`: igual, distinto, vacio, no_vacio. `efecto`: ocultar, mostrar, requerir, opcional.
- Cada llamada crea UNA regla vinculada al campo `si_campo` (se ve en `reglas_de_formulario`).
- Un "requerir" no se deshace solo: si el valor cambia, agrega la regla inversa ("opcional") como en el ejemplo.

## Obligatorio solo al cerrar un paso del flujo

Si el campo solo debe exigirse cuando la persona cierra el paso (no al guardar), usa un boton con
`COMPLETAR_PASO_FLUJO` y `requireFields` ([[FO06 - Receta botones que ejecutan acciones]]) o, en una compuerta,
`formulario_obligatorio_por_salida` (cerebro de flujos).

## Secciones solo para ciertos cargos

En el contenedor: `allowed_cargos_json` (ids de `list_org_units`). La seccion no se muestra a quien no tenga el
cargo. Por ROL (Owner/Admin/Supervisor/Advisor) a nivel de campo: [[FO08 - Receta proteger campos por rol]].
