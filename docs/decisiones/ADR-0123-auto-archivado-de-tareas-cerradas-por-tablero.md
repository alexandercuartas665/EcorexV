# ADR-0123: Auto-archivado de tareas cerradas por tablero + concepto de cierre obligatorio

**Status:** Aceptado (implementado; sin desplegar)
**Date:** 2026-10-06
**Deciders:** Alexander Cuartas
**Extiende:** ADR-0020 (tableros de actividades: columna final IsDone, transicion oportunista a Done)

## Contexto

Las tarjetas llegan a la columna de cierre del tablero (columna con el flag "Cierra" =
`TaskBoardColumn.IsDone`) y se quedan ahi para siempre: al aterrizar pasan a `Status = Done`, dejan
de generar atrasos y -si el tablero tiene motivos- se ofrece un motivo de cierre OPCIONAL, pero NADA
las archiva. El archivado de tareas (`TaskItem.IsArchived`) es hoy 100% manual, tarjeta por tarjeta.
Resultado: la columna de cierre se vuelve un basurero que crece sin limite.

Hay DOS caminos por los que una tarjeta llega a la columna de cierre:

- **Manual**: una persona arrastra la tarjeta a una columna "Cierra". `MoveTaskAsync` la pone en Done
  y sella `ColumnEnteredAt`. El prompt de motivo de cierre ya existe (alimentado por
  `TaskBoard.CloseReasonsJson`), pero es opcional ("Cerrar sin motivo").
- **Flujo**: el motor de flujo (`WorkflowEngine`) mueve la tarjeta al tablero/columna destino del
  nodo (`WorkflowNode.TargetBoardId`/`TargetColumnId`) y, al completar la instancia, pone la tarea en
  Done. Tambien sella `ColumnEnteredAt`. El nodo NO tiene hoy un concepto de cierre configurable.

Riesgo detectado: un flujo puede PARQUEAR la tarjeta en una columna intermedia (reaperturas,
compuertas) que este marcada "Cierra". Si el archivado se disparara solo por "esta en una columna
Cierra N dias", se archivaria una actividad todavia VIVA.

## Decision

Un solo concepto de cierre por tablero, dos puertas para capturarlo, y una sola regla de desaparicion
que vive en el tablero.

1. **Regla de archivado por tablero.** Nuevo `TaskBoard.AutoArchiveDoneDays` (int, default **15**,
   `0` = nunca). Un worker diario archiva las tareas que cumplan: `IsArchived = false` **y**
   `Status in (Done, Closed)` **y** la tarjeta este en una columna `IsDone` **y**
   `now - ColumnEnteredAt >= AutoArchiveDoneDays`. Deja rastro en la bitacora de la tarea
   ("archivada automaticamente"). Reversible: la papelera del tablero ("Ver archivadas") ya restaura.

2. **Disparador anclado en "cerrada", no en "parada en una columna".** Se exige `Status = Done/Closed`
   ademas de estar en columna `IsDone`. El flujo marca Done SOLO al completarse / en el nodo de
   cierre; parquear la tarjeta en una columna intermedia NO la pone en Done, asi que una actividad en
   curso nunca se archiva. En la practica, para el Kanban manual, "columna Cierra + Done" coinciden
   en el mismo instante.

3. **Concepto de cierre OBLIGATORIO.** La lista por tablero (`CloseReasonsJson`) es el unico
   vocabulario. Si el tablero tiene conceptos configurados:
   - **Manual**: al soltar en una columna "Cierra" el sistema PREGUNTA el concepto y es obligatorio
     (se retira la salida "Cerrar sin motivo" cuando hay conceptos). Se guarda en `TaskItem.CloseReason`.
   - **Flujo**: cualquier nodo cuyo destino sea una columna "Cierra" lleva un campo nuevo
     `WorkflowNode.CloseReason` (el concepto, elegido de la lista del tablero destino). Al aterrizar /
     completar, el flujo lo estampa solo en `TaskItem.CloseReason` -sin preguntar, porque es automatico.

4. **UI del disenador de flujo.** Debajo del selector de tablero/columna que ya existe en el nodo,
   cuando la columna elegida es "Cierra" aparece el selector "Concepto de cierre" (obligatorio,
   opciones = `CloseReasons` del tablero destino). Aplica a CUALQUIER nodo que use una columna de
   cierre, no solo al EndEvent.

## Opciones consideradas

### A. Disparo por ubicacion pura (columna IsDone + N dias)
**Contra:** archiva actividades vivas que un flujo parqueo en una columna de cierre intermedia. Descartada.

### B. Disparo anclado en estado cerrado (Done/Closed) + N dias  (ELEGIDA)
**Pro:** un unico criterio cubre manual y flujo; el Done lo ponen ambas puertas; protege al flujo vivo.
Reusa `ColumnEnteredAt` como reloj (ya existe, se sella en los dos caminos).

### C. Knob de archivado por nodo del flujo
El flujo cargaria su propio "archivar a los N dias" en cada nodo de cierre. **Contra:** duplica la
config del tablero y crea dos lugares donde contradecirse. Descartada: la regla de desaparicion vive
solo en el tablero; el nodo solo aporta el CONCEPTO de cierre.

## Consecuencias

- La columna de cierre deja de acumular: lo cerrado desaparece solo a los N dias, con periodo de
  gracia para revisar lo recien cerrado. Configurable por tablero (`0` = nunca, respeta a quien quiera
  archivar a mano).
- Todo cierre queda con su concepto (obligatorio): reportes de "por que se cerro" quedan completos,
  venga de persona o de flujo.
- El flujo vivo nunca se archiva por error (anclaje en Done, no en ubicacion).
- Retrocompatible: tableros sin conceptos siguen pudiendo cerrar sin concepto; `AutoArchiveDoneDays`
  default 15 se aplica a los existentes (si se prefiere no tocar historico, sembrar `0` en backfill
  y que cada tablero lo active).
- Dos campos nuevos (migracion dual) + un worker; todo sobre primitivas que ya existen
  (`ColumnEnteredAt`, `IsDone`, `CloseReasonsJson`, papelera/restaurar, estado Done).

## Action Items

1. [x] `TaskBoard.AutoArchiveDoneDays` (int, default 15) + UI en config del tablero (migracion dual).
       Rollout: la migracion siembra los tableros EXISTENTES con 3 (defaultValue 3); los nuevos nacen 15.
2. [x] `WorkflowNode.CloseReason` (string) + selector "Concepto de cierre" en el nodo (FlowEditor) cuando
       la columna destino es "Cierra", para CUALQUIER nodo (no solo EndEvent). Migracion dual.
3. [x] Prompt manual: concepto OBLIGATORIO cuando el tablero tiene conceptos (retirado "Cerrar sin motivo"
       en TaskDetailModal, ActivityBoardDetail y MovilTablero; guard en servidor MoveTaskAsync).
4. [x] Flujo: estampa `WorkflowNode.CloseReason` en `TaskItem.CloseReason` al aterrizar/completar en
       columna de cierre (WorkflowEngine.MoveTaskToNodeTargetAsync).
5. [x] Worker diario de auto-archivado (`TaskAutoArchiveWorker` + `ITaskAutoArchiveService`): Done/Closed
       + columna IsDone + `ColumnEnteredAt >= N dias`, con rastro en bitacora. Tenant-scoped (AmbientTenantContext).
6. [x] E2E en local (AGROMETALICAS / GESTION COMERCIAL):
       (a) cierre manual OBLIGA concepto: arrastrar a "Cierre" abre el prompt (3 conceptos, SIN "sin motivo");
           elegir "Resuelto" -> Status=Done + close_reason="Resuelto" + reloj re-sellado.
       (b) auto-archivado por dias: con dias=5, el worker archivo T00075 (Done, 10 dias en cierre) y dejo
           rastro "archivada automaticamente (5 dias cerrada en el tablero)".
       (c) NO archiva: T00067 (Active en cierre, 10 dias) NO se archivo -> flujo vivo protegido (anclaje en
           Done, no en ubicacion); T00074 (Done, 2 dias < 5) NO se archivo -> umbral de dias respetado.
       Pendiente opcional: E2E del estampado por un FLUJO real al completar (se valido la ruta de codigo
       MoveTaskToNodeTargetAsync, no un flujo end-to-end en vivo).
