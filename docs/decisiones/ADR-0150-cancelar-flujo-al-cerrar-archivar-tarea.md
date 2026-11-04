# ADR-0150: Cancelar el flujo en marcha al cerrar o archivar una tarea (matar los flujos "zombi")

Fecha: 2026-11-04
Estado: Aceptada
Relacionada: ADR-0149 (resolver candidatos de nodo en lote / N+1), ADR-0123 (auto-archivado),
ADR-0122/0126 (hold de linea de WhatsApp por el flujo / FlowLineRelease), ADR-0090 (agentes en nodos).

## Contexto

Al diagnosticar la lentitud de prod (ADR-0149) se encontro que ~169 de 562 instancias de flujo en
estado `Running` (y ~233 de 826 pasos `is_current`) pertenecen a tareas que YA estan cerradas (Done/
Closed) o archivadas. Son flujos "zombi": la tarea se cerro, pero su `workflow_instance` quedo Running
con su paso vigente intacto.

La causa: cerrar o archivar una tarea por fuera del motor NO cancelaba su flujo. Los caminos que cierran
una tarea son:

- `ActivityBoardService` al mover la tarjeta a una columna de cierre (tablero),
- `TaskItemService.ChangeStatusAsync` (cambio de estado a Done/Closed),
- `TaskItemService.ArchiveAsync` (archivar a mano),
- `TaskAutoArchiveService` (auto-archivado por dias, ADR-0123).

Solo el camino del tablero llamaba a `FlowLineRelease.OnTaskClosedAsync`, y ese helper solo suelta el
hold de linea de WhatsApp (ADR-0126): no cancela la instancia ni resuelve el paso vigente. El motor
(`WorkflowEngine`) si completa/cancela la instancia cuando el FLUJO termina, pero no cuando es la TAREA
la que se cierra por fuera.

Consecuencia: todo barrido seguia tomando esos pasos para siempre:

- bandeja "mis pasos" y tablero (alcance "Mis tareas") los listan (`is_current + Pending`), engrosando
  cada consulta (el N+1 de ADR-0149 escalaba con este backlog muerto),
- el motor de agentes en nodos (`WorkflowAgentStepWorker`/Dispatcher) los recoge por `is_current +
  Pending + nodo con agente`, SIN filtrar el estado de la instancia -> agentes de seguimiento
  persiguiendo (y a veces reabriendo) casos ya cerrados.

## Decision

Nuevo helper estatico `WorkflowInstanceLifecycle` (en `Ecorex.Application/Workflows`), mismo patron que
`FlowLineRelease`: opera sobre las entidades RASTREADAS del `DbContext` del que cierra/archiva y NO llama
`SaveChanges` (el llamador persiste en su propia transaccion). Es idempotente (solo toca instancias
`Running`).

`CancelRunningForTaskAsync` (y la variante en lote `CancelRunningForTasksAsync` para el worker):

1. Busca las instancias `Running` ligadas a la(s) tarea(s).
2. Sus pasos vigentes (`is_current + Pending`) pasan a `IsCurrent=false`, `Status=Skipped`,
   `CompletedAt=now`, y se limpian las marcas de espera del agente (`AgentNextRetryAt`,
   `AgentDeadlineAt`, `PendingWhatsAppConversationId`; `AgentAttemptedAt ??= now`) para que el barrido no
   los recoja ni por un reintento programado. `Skipped` es la misma semantica de "rama pendiente sin
   efecto" que usa el motor al completar una instancia.
3. Suelta cualquier hold de linea de WhatsApp que apuntara a esos pasos (`FlowHoldStepId`/`FlowHoldNodeId`
   a null), para no dejar la linea muda apuntando a un paso ya no vigente. NO pone el candado de
   cierre-por-flujo (eso sigue siendo de `FlowLineRelease`, que ya corre en el camino del tablero).
4. Las instancias pasan a `Status=Cancelled`, `CompletedAt=now`.

Se cablea en los cuatro puntos terminales, cada uno ANTES de su `SaveChanges`:

- `ChangeStatusAsync`: cuando el nuevo estado es `Closed` o `Done`.
- `ArchiveAsync`: siempre (archivar es visibilidad, aplica aunque la tarea no estuviera Closed).
- `ActivityBoardService` (mover a columna de cierre): SOLO cuando la tarea REALMENTE paso a Done por el
  movimiento (`statusChanged`). Si un flujo solo PARQUEO una tarea VIVA en una columna de cierre (status
  sin cambiar, caso que ADR-0123 protege del auto-archivado), el flujo sigue legitimamente y no se toca.
- `TaskAutoArchiveService`: en lote sobre todas las tareas que se archivan (ya estan Done/Closed; lo
  normal es que su flujo ya este Completed/Cancelled -> no-op; atrapa los cierres legacy previos al fix).

## Por que resolver el paso y ademas cancelar la instancia

Poner `is_current=false` en el paso basta para sacarlo de bandeja, tablero y del motor de agentes (todos
filtran por `is_current`). Pero dejar la instancia `Running` sin frontera es un estado inconsistente (un
caso "en marcha" sin paso vigente). Marcar la instancia `Cancelled` refleja la realidad (tarea cerrada =
flujo cancelado) y es barato. Se hacen las dos cosas.

## Consecuencias

- Al cerrar/archivar una tarea, su flujo deja de existir para todos los barridos: menos carga y, sobre
  todo, los agentes de seguimiento dejan de perseguir casos cerrados.
- Sin migracion EF: solo cambia datos (estado de instancias y pasos) sobre columnas existentes.
  Concurrencia optimista intacta (las entidades se cargan rastreadas y guardan con el token).
- Un entrante de WhatsApp sobre una tarea ya cerrada ya no reactiva un paso muerto (el paso quedo
  Skipped y la linea liberada).
- Pasos HUERFANOS (vigentes sobre una instancia YA terminal): `WorkflowDesignService.CancelInstanceAsync`
  cancelaba la instancia pero NO resolvia su paso vigente, dejando un paso `is_current + Pending` sobre una
  instancia Cancelled (un frente vivo imposible que todo barrido sigue tomando). Se tapa la fuente: ese
  metodo ahora resuelve sus pasos al cancelar (helper comun `ResolveCurrentStepsForInstancesAsync`). Y para
  los que ya existian, la limpieza puntual barre ademas los pasos vigentes sobre instancias Completed o
  Cancelled (`ResolveOrphanCurrentStepsAsync`; NO toca Stuck, estado defensivo que puede esperar). El
  endpoint reporta `{ totalInstancias, totalPasosHuerfanos }`. (v0.16.273.)
- Para el backlog existente (~169 instancias Running de tareas ya cerradas/archivadas antes de este fix,
  que el auto-archivado solo ira cancelando al re-archivar y que las tareas YA archivadas no vuelven a
  tocar) se agrega una limpieza PUNTUAL que reusa este mismo helper: `IZombieFlowCleanupService` +
  endpoint `POST /admin/cleanup-zombie-flows` (gated a PlatformOperator). DRY-RUN por defecto (solo
  cuenta por tenant); con `?apply=true` cancela de verdad. Recorre todos los tenants acotando cada uno
  con `AmbientTenantContext` (el query filter aisla) y deja rastro: una actividad "Sistema" por tarea
  afectada y un log con los conteos. Idempotente: una segunda corrida no encuentra nada. Se corre UNA
  vez (dry-run primero) tras desplegar, con el OK del usuario.
