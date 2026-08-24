# ADR-0073: Cerrar/decidir/reabrir un paso SOLO el asignado o su cargo (se retira el override de Owner/Admin)

- Estado: Aceptado (reemplaza la parte de override de ADR-0064)
- Fecha: 2026-08-24
- Contexto: bandeja/motor de flujos (ADR-0014/0035/0068), diagrama del flujo en la tarea
  (ADR-0051/0070/0071/0072).

## Contexto

En ADR-0064 se permitio que un Owner/Admin cerrara CUALQUIER paso del flujo desde el diagrama
(gobierno del proceso). En la revision el usuario pidio lo contrario: **se debe respetar la
asignacion** -- solo el encargado asignado (o un candidato de su cargo, que primero reclama) puede
cerrar/decidir un paso. El override de Owner/Admin dejaba que un usuario distinto al asignado
cerrara pasos, lo que el usuario considera incorrecto.

## Decision

Se retira el override de Owner/Admin en las acciones sobre un paso del flujo. Autorizacion ahora:

- **Cerrar** (`CompletePendingStepAsync`) y **decidir ruta** (`CompleteGatewayChoiceAsync`): el
  ASIGNADO del paso, o -- si esta sin asignar -- un CANDIDATO de la policy/cargo del nodo (que
  primero RECLAMA con `ClaimStepAsync`). Sin override de rol.
- **Reabrir** (`ReopenStepAsync`): SOLO quien cerro el paso (`ExecutedByTenantUserId`).
- **Diagrama**: `mcanAct`/`canAct` requieren `IsMine` (ya no `viewerIsManager`); `CanReopen` solo
  para quien cerro. Se elimino `IsOwnerOrAdminAsync` / `ViewerIsManagerAsync`.

El modelo "cualquiera del cargo lo toma" se conserva: un candidato del cargo ve el paso como
RECLAMABLE, lo toma y entonces lo cierra. La herencia del encargado (InheritStart: el paso hereda
al usuario resuelto por el cargo del primer nodo) se mantiene sin cambios.

## Consecuencias

- Un Owner/Admin ya NO puede cerrar pasos ajenos desde el diagrama; ve el paso en solo lectura
  (puede leer y dejar notas de equipo, ADR-0071).
- Si un paso queda sin asignado y sin candidatos (p.ej. un nodo InheritStart cuando no hubo
  iniciador), nadie podra cerrarlo hasta reasignarlo. Es el costo de respetar la asignacion; la
  via de escape es la reasignacion, no un override de rol.

## Relacionado (misma version, visuales del diagrama)

- **Rama descartada en gris**: al decidir una compuerta, el/los destinos NO tomados (nodos ya no
  alcanzables desde ningun paso vigente y sin historial) se atenuan en gris con su arista punteada
  (`TaskFlowNodeDto.IsAbandoned`).
- **Eventos mas grandes**: los nodos de inicio/fin pasan de 38px a 72px (se re-centran para no
  descolocar las aristas), para guardar proporcion con las tareas.
