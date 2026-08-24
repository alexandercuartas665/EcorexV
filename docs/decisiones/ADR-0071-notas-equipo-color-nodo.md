# ADR-0071: Notas colaborativas del equipo por nodo + color configurado visible en el diagrama

- Estado: Aceptado
- Fecha: 2026-08-23
- Contexto: diagrama del flujo en el detalle de la tarea (ADR-0051) + apariencia de nodo
  (color/nota de configuracion, ADR-0022) + reapertura/cierre de pasos (ADR-0070).

## Contexto

En la revision punto por punto el usuario pidio dos cosas sobre el diagrama del flujo dentro
de la tarea:

1. **Notas del equipo**: hasta ahora solo el encargado del paso ACTUAL podia anotar (al cerrar).
   El usuario quiere que CUALQUIER miembro con acceso a la tarea deje notas en CUALQUIER nodo
   -- incluidos pasos FUTUROS de los que no es encargado -- para avisar algo a quien atienda
   ese paso ("dejo notas a mis companeros").
2. **Color del nodo**: los colores que se configuran en el editor del flujo no se percibian en
   el diagrama de la tarea (se pintaba solo una linea fina de 3px / un aro), asi que "no se ven".

## Decision

1. **Nueva entidad `WorkflowNodeNote`** (TENANT-SCOPED, append-only): nota colaborativa por
   `(InstanceId, NodeId)` con autor (`AuthorTenantUserId` + `AuthorName` capturado) y texto.
   Es distinta de `WorkflowNode.Note` (config del editor) y de
   `WorkflowStepHistory.ApprovalComment` (anotacion del cierre). Migracion dual (Postgres +
   SQL Server), tabla `workflow_node_notes`.
   - `IWorkflowInboxService.AddNodeNoteAsync(taskId, nodeId, tenantUserId, text)`: valida que el
     nodo pertenezca a la definicion de la instancia de la tarea y agrega la nota. Autorizacion:
     cualquier miembro con acceso a la tarea (abrir el modal ya esta gobernado); no exige ser el
     encargado ni que el paso sea el actual (se puede anotar un paso futuro).
   - `GetTaskFlowDiagramAsync` carga las notas por nodo (`TaskFlowNodeDto.TeamNotes`).
   - UI: el menu del nodo SIEMPRE muestra la seccion "NOTAS DEL EQUIPO" (hilo autor+fecha+texto
     + caja para agregar), para todos los nodos. Al agregar, el menu queda abierto para ver la
     nota recien creada.

2. **Color configurado visible**: en el diagrama de la tarea, un nodo con color configurado se
   pinta con un TINTE suave de ese color (via `color-mix`), no solo el borde: tarjeta Task con
   `background: color-mix(... 12% ...)` + barra de acento de 4px; compuerta (diamante) y evento
   (aro) con tinte 16-18%. Los nodos sin color mantienen el aspecto por defecto.

## Consecuencias

- El diagrama pasa a ser un canal de comunicacion del equipo, no solo un tablero de estado.
- Las notas son append-only (no se editan/borran desde la UI); si mas adelante se requiere
  moderacion (borrar/editar), es una ola futura.
- `color-mix` requiere navegador moderno (Chrome/Edge/Firefox recientes), consistente con el
  resto del front Blazor del producto.

## Alternativas descartadas

- Reusar `TaskItemActivity` con un `NodeId`: habria mezclado la bitacora general de la tarea con
  los recados por nodo y obligado igual a una migracion (columna nueva). Una tabla dedicada es
  mas clara y se consulta directo por `(InstanceId, NodeId)`.
- Pintar el nodo con el color como fondo pleno: rompia el contraste del texto y los estados
  (current/done). Se opto por un tinte suave + barra de acento.
