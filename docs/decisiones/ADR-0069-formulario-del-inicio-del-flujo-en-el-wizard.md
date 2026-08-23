# ADR-0069: El formulario del primer paso del flujo se ofrece en el wizard al crear

- Estado: Aceptada
- Fecha: 2026-08-23
- Relacionado: ADR-0015 (formularios dinamicos), continuidad de formularios por paso (v0.15.60).

## Contexto

Hay dos puntos donde se asigna un formulario a una actividad:

1. **Formulario del CONCEPTO** (`ActividadSubcategoria.FormDefinitionId`): se ofrece en el paso 3
   "Formulario" del wizard de creacion (tarjetas + modal), anclado al numero de la tarea.
2. **Formularios de NODO** (`WorkflowNodeForm`): se atienden en el DETALLE de la tarea, en cada paso
   del flujo que tenga formulario.

El usuario suele asignar el formulario al **evento de inicio** del flujo (el "primer nodo"), esperando
diligenciarlo AL CREAR la actividad. Pero el inicio auto-completa (no se atiende) y el wizard paso 3
solo mira el formulario del concepto; si el concepto no tiene, muestra "esta actividad no tiene
formulario asociado", aunque el flujo si tenga formularios en sus nodos.

## Decision

Cuando el concepto NO define formulario propio, el **wizard paso 3 usa el formulario del PRIMER PASO
del flujo (evento de inicio)** con la misma funcionalidad (tarjeta + modal de llenado). Si el concepto
SI define formulario, gana el del concepto (comportamiento sin cambios: cero riesgo a lo existente).

- Nuevo `IFormResponseService.GetSubcategoriaCreationFlowFormsAsync(subId)`: devuelve los formularios
  Active del evento de inicio del flujo publicado de la subcategoria.
- Wizard: `EffectiveFormDefId = concepto ?? primer form del inicio`. Se reemplaza el uso directo de
  `ActividadSubcategoria.FormDefinitionId` por esa propiedad en el paso 3, el modal y los helpers.
- **Anclaje para continuidad:** cuando el formulario proviene del flujo, el borrador diligenciado en la
  creacion se ancla al numero EXACTO de la tarea (no `"{numero}-{n}"`). Asi el paso del flujo que usa el
  mismo formulario REUSA ese borrador (mismos datos) via la continuidad de v0.15.60. El del concepto y
  los extras siguen anclando como `"{numero}-{n}"`.

## Consecuencias

- (+) El formulario asignado al inicio del flujo "sale" en el wizard al crear, como el del concepto.
- (+) Los datos diligenciados en la creacion se cargan en los pasos siguientes con ese formulario.
- (+) Sin migracion; el caso "concepto con formulario" queda intacto.
- (-) Solo se ofrece el/los formulario(s) del EVENTO DE INICIO en el wizard (no los de pasos
  posteriores, que se atienden en su paso). Si el inicio tiene varios, el primero es el que garantiza
  continuidad; los demas se anclan como cotizaciones extra.
- (Deuda) La UI del paso 3 reusa el rotulo "cotizaciones" (del concepto); para un formulario de flujo
  el termino es generico. Cosmetico.
