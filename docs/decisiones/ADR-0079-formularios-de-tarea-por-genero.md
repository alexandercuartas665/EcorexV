# ADR-0079: Formularios de la tarea agrupados por GENERO (concepto + flujo)

- Estado: Aceptado
- Fecha: 2026-08-25
- Contexto: formularios de concepto por tarea (ADR-0065, tarjetas con activo/agregar/copiar/eliminar),
  formularios del flujo por nodo (ADR-0015/0069, WorkflowNodeForm), visibilidad del formulario de
  creacion (v0.15.82).

## Contexto

Las tareas que vienen de un CONCEPTO mostraban su formulario en una tarjeta rica ("Formularios ·
<titulo>", con ★ Activo / Borrador, botones Editar / Copiar / Marcar activo / Reabrir / Eliminar y
+ Agregar formulario). Las tareas que vienen de un FLUJO solo mostraban sus formularios en una tarjeta
pobre ("Formularios de la actividad", v0.15.82) con un unico boton Abrir/Ver: no se podian agregar,
activar, copiar ni eliminar. El usuario pidio PARIDAD: los formularios del flujo deben presentarse y
gestionarse igual, agrupados por GENERO (una definicion = un genero), con un solo formulario activo por
genero, y con + Agregar por genero segun la configuracion del concepto y/o del flujo. Una tarea puede
tener varios generos a la vez.

Hallazgo: toda la maquinaria de los formularios de concepto YA es generica (numeracion "{tarea}-{n}"
por definicion, activo exclusivo por definicion, crear/copiar/borrar/reabrir/activar) — opera sobre
`(DefinitionId, taskNumber)`. Lo unico atado al concepto era de donde sale la definicion.

## Decision

1. **Conjunto de GENEROS de una tarea** (`IFormResponseService.GetTaskFormGenerosAsync`): union de
   - el genero del CONCEPTO (`ActividadSubcategoria.FormDefinitionId`, 0 o 1) -- siempre si existe;
   - los generos del FLUJO (`WorkflowNodeForm` de CUALQUIER nodo): los del evento de inicio siempre
     (para ofrecer + Agregar aunque esten vacios), los demas solo si ya tienen respuestas;
   - un catch-all de cualquier definicion con respuestas ancladas a la tarea (p.ej. una Orden de
     Trabajo derivada, ADR-0078).
   Cada genero trae sus respuestas ("{numero}"/"{numero}-{n}") con un activo efectivo, EXCLUYENDO la
   respuesta del paso ACTUAL (esa se ejecuta en "Formularios del proceso", no se duplica).

2. **Reuso total**: se extrae `BuildGeneroAsync(task, def, exclude)` del cuerpo de
   `GetTaskConceptFormsAsync` (listar respuestas por `(def, numero)`, calcular activo, mapear items). El
   genero del concepto pasa a ser un elemento mas de la lista. `CreateTaskFormAsync(taskId, defId)`
   generaliza `CreateTaskConceptFormAsync` (+ Agregar de cualquier genero). Copiar / Marcar activo /
   Reabrir / Eliminar NO cambian (ya operan por responseId; el activo ya se limita a `(def, numero)`).

3. **UI** (`TaskDetailModal`): la pestana Formularios recorre `_generos` y pinta la MISMA tarjeta por
   cada uno. Se conserva "Formularios del proceso" aparte para ejecutar el paso ACTUAL
   (Diligenciar / Cerrar / elegir ruta); no se toca la ejecucion del flujo.

## Consecuencias

- Presentacion y gestion uniformes para formularios de concepto y de flujo. Multi-tenant intacto
  (filtro global). Sin migracion (todo lectura + reuso de operaciones existentes).
- "Formularios de la actividad" (v0.15.82) queda SUBSUMIDA por las tarjetas de genero (el catch-all
  cubre derivados y formularios de pasos ya recorridos). `GetTaskRelatedFormsAsync` queda sin uso en el
  modal (se conserva por si se reusa).

## Evolucion (v0.15.84 / v0.15.85)

- v0.15.84: se dejo de agrupar por tarjeta-seccion (una por tipo). Una sola tarjeta "Formularios" con la lista
  UNIFORME de todos los formularios (cada item con una pildora del tipo). "+ Agregar formulario" con varios
  tipos abre un modal selector; con uno crea directo. `GetTaskFormGenerosAsync` devuelve TODOS los generos
  (incl. vacios) para poblar el selector.
- v0.15.85: se UNIFICA tambien el paso actual. `GetTaskFormGenerosAsync` ya no excluye el formulario del paso
  actual (se quito shownInStep): aparece como una tarjeta mas, cuya accion "Diligenciar"/"Ver" abre en modo
  PASO (su envio completa el paso del flujo, sin cambios en el motor). Se ELIMINA la seccion "Formularios del
  proceso": todo vive en la lista unica. La tarjeta de paso oculta Eliminar/Reabrir/Marcar-activo para no
  romper la ejecucion.

## Limitacion conocida

Marcar "activo" un genero de flujo afecta la presentacion y las columnas del tablero, pero la EJECUCION
del paso sigue reusando la respuesta anclada al numero BASE ("{numero}"), no la marcada activa. Si mas
adelante se quiere que el paso tome la activa, es un cambio aparte en `GetTaskStepFormsAsync`.
