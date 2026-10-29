# ADR-0129: crear_actividad funciona con o sin formulario

Fecha: 2026-10-22
Estado: Aceptada

## Contexto

La herramienta del agente `crear_actividad` (ActividadesToolset) daba de alta una actividad de un CONCEPTO y
ademas LLENABA+ENVIABA el formulario del concepto. Exigia que el concepto tuviera formulario: si
`ActividadSubcategoria.FormDefinitionId` era null devolvia un error ("no tiene un formulario asociado; no se
puede crear la actividad por esta via") y `datos` era requerido en el spec.

Pedido (data-ops): que SARA (AGROMETALICAS) registre casos con `crear_actividad` usando el concepto
"Seguimiento comercial agente IA" para que dispare su FLUJO. Ese concepto NO tiene formulario y no lo necesita.

## Decision

`crear_actividad` funciona tambien cuando el concepto NO tiene formulario:

- `datos` pasa a ser OPCIONAL en el spec (`required: ["concepto"]`).
- Si `FormDefinitionId` es null: se saltan la carga de la definicion, la pre-validacion de `datos` y los pasos
  de crear+llenar+enviar el formulario. Se conservan el alta de la actividad (paso 1, por `SubcategoriaId`, que
  arranca el flujo del concepto) y el auto-adjuntar la media entrante (paso 4).
- Si el concepto SI tiene formulario: comportamiento IDENTICO al anterior (valida + llena + envia).

El candado de cierre-por-flujo y la de-duplicacion por conversacion+tablero no cambian.

## Consecuencias

- `crear_actividad(concepto="Seguimiento comercial agente IA", datos={})` crea la actividad y arranca su flujo,
  sin error.
- Conceptos con formulario: sin cambios.
- Sin migracion.
