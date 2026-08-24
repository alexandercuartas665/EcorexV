# ADR-0073: Solo el asignado (o su cargo) cierra/decide un paso - se retira el override de Owner/Admin

- Estado: Aceptado (revierte ADR-0064)
- Fecha: 2026-08-24
- Contexto: bandeja de flujos (ADR-0036), diagrama del flujo en la tarea (ADR-0051/0070/0071/0072),
  cierre de pasos por Owner/Admin (ADR-0064).

## Contexto

ADR-0064 permitia que un Owner/Admin del tenant cerrara CUALQUIER paso desde el diagrama (gobierno
del proceso). En la revision del 2026-08-24 el usuario decidio lo contrario: el flujo debe RESPETAR
la asignacion. Un usuario distinto al asignado no debe poder dar "terminado" a un paso ajeno.

## Decision

La autorizacion para CERRAR un paso, DECIDIR en una compuerta atendida y REABRIR un paso queda
estrictamente ligada a la asignacion:

- **Cerrar / decidir** (`CompletePendingStepAsync`, `CompleteGatewayChoiceAsync`): solo el ASIGNADO
  del paso o, si el paso esta SIN asignar, un CANDIDATO de su cargo (que primero RECLAMA el paso).
  Se retira el `IsOwnerOrAdminAsync`.
- **Reabrir** (`ReopenStepAsync`, ADR-0070): solo QUIEN cerro el paso (`ExecutedByTenantUserId`). Se
  retira el override de Owner/Admin. `TaskFlowNodeDto.CanReopen` se calcula igual (sin `viewerIsManager`).
- **UI** (`TaskDetailModal`): el menu del nodo solo ofrece cerrar/rutas si el paso es del viewer
  (`IsMine`). Para los demas es de solo lectura. Se elimina `_viewerIsManager`.
- Las NOTAS del equipo (ADR-0071) NO se ven afectadas: siguen siendo colaborativas (cualquiera con
  acceso a la tarea deja recados en cualquier nodo).

## Consecuencias

- El diagrama respeta la asignacion: cada quien cierra lo suyo; el cargo reclama y cierra.
- **Requisito**: todo nodo ATENDIDO debe tener un encargado resoluble (asignado por su origen, o un
  cargo con candidatos). Un nodo sin asignado y sin cargo ya NO lo puede cerrar nadie (antes lo
  desatascaba un Owner/Admin). Se mitiga configurando la asignacion "por cargo" por nodo en el editor
  (AssigneeSource=Policy + cargo).
- Para gobernar/desatascar un caso, la via correcta es REASIGNAR (`ReassignStepAsync`, si el nodo lo
  permite) hacia el encargado que corresponda, no cerrar por encima de la asignacion.

## Nota

Si mas adelante se requiere un respaldo de gobierno, la opcion evaluada fue "Owner/Admin solo si el
paso esta sin atender"; se descarto por ahora a favor del modelo estricto. Reintroducirlo seria un
ADR nuevo.
