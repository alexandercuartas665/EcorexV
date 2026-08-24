# ADR-0070: Reabrir un paso de flujo cerrado + cierre directo del paso con formulario

- Estado: Aceptado
- Fecha: 2026-08-23
- Contexto: motor de flujos (WorkflowEngine, ADR-0014) + diagrama del flujo en el detalle de
  la tarea (ADR-0051) + compuertas/eventos atendidos (ADR-0068).

## Contexto

En el diagrama del flujo dentro de una tarea, el menu por nodo permitia cerrar el paso
actual y anotar. Faltaban tres cosas que pidio el usuario en la revision punto por punto:

1. El boton que abre el menu del nodo (`...`) era casi invisible: el usuario no lo ubicaba.
2. Un paso con formulario solo se podia cerrar "yendo a diligenciar y cerrar" (obligaba a
   enviar el formulario). El usuario quiere poder CERRAR la actividad directamente, dejando
   el formulario como algo OPCIONAL, sin perder la posibilidad de diligenciarlo.
3. No habia forma de REABRIR un paso ya cerrado cuando el cierre fue un error.

## Decision

1. **Boton del menu visible**: `.tk-flow-dots` pasa a ser una pildora con borde, fondo y
   sombra; se resalta en hover y se pinta en color de marca en el paso vigente (`current`).

2. **Cerrar actividad directo (formulario opcional)**: el menu de un paso vigente atendible
   (mio o soy Owner/Admin) SIEMPRE ofrece "Cerrar actividad" con una nota OPCIONAL, tenga o
   no formulario. Si el nodo tiene formulario, ademas se ofrece "Diligenciar formulario"
   (salta a la pestana de formularios). El motor ya no exigia formulario para cerrar; el
   cambio es de UI (se quito el gate `!HasForm`).

3. **Reabrir un paso cerrado** (nuevo `IWorkflowEngine.ReopenStepAsync`):
   - Reactiva EN SITIO el paso Task cerrado (Completed -> Pending + IsCurrent), limpiando el
     cierre (ejecutor, resultado, comentario, fecha) y CONSERVANDO el asignado.
   - DESHACE lo que ese cierre habia activado aguas abajo: los pasos vigentes de nodos
     posteriores se apagan (Pending -> Skipped). Al volver a cerrarse, el avance normal crea
     filas nuevas (append-only).
   - **Guarda dura**: solo procede si la instancia sigue `Running` y NINGUN nodo alcanzable
     hacia adelante tiene un cierre HUMANO (un Task/EndEvent `Completed`) ni un paso
     `Rejected`. Las compuertas AUTOMATICAS `Completed` (parte del avance) NO cuentan.
   - **Autorizacion** (en `WorkflowInboxService.ReopenStepAsync`): el ENCARGADO que lo cerro
     (`ExecutedByTenantUserId`) o un Owner/Admin del tenant.
   - Si el nodo reabierto tiene tablero/columna destino, la tarjeta regresa alli.

   El diagrama expone por nodo `CanReopen` + `ReopenStepId` (`TaskFlowNodeDto`), calculados
   con la misma guarda, para pintar el boton "Reabrir actividad" solo cuando aplica.

4. **Estado vigente robusto por nodo**: `GetTaskFlowDiagramAsync` desempata el paso de un
   nodo (cuando hay varias filas del mismo ciclo por rechazo/reapertura) por
   `CycleIndex desc, IsCurrent desc, CreatedAt desc`, para no mostrar una fila vieja
   (p.ej. Skipped) en vez de la vigente.

## Consecuencias

- El cierre de un paso deja de estar acoplado al envio del formulario; un formulario mal
  diligenciado ya no bloquea el avance. Contrapartida: se puede cerrar sin diligenciar (es
  una decision explicita del usuario).
- Reabrir es un "deshacer" acotado: nunca revierte trabajo humano posterior. No reabre
  compuertas/eventos ni pasos automaticos (solo Task).
- Historial: la reactivacion es en sitio (una sola fila logica por nodo/ciclo, coherente con
  como el diagrama resuelve el estado). La auditoria del "cerro y reabrio" queda en
  `TaskItemActivity`.

## Alternativas descartadas

- Reabrir creando una fila nueva (como el rechazo, ADR): generaba dos filas del mismo nodo y
  ciclo y complicaba el "estado vigente" del diagrama. Se prefirio reactivar en sitio.
- Mantener el gate de formulario para cerrar: el usuario lo pidio opcional explicitamente.
