# ADR-0076: Un evento de FIN con "salto a otro flujo" crea una TAREA HIJA

- Estado: Aceptado (implementa la deuda de runtime del salto, ADR-0056)
- Fecha: 2026-08-24
- Contexto: motor de flujos (ADR-0014), evento de fin auto-finaliza (ADR-0075), enlace
  flujo<->tableros, `WorkflowNode.JumpToDefinitionId` (metadato del editor, sin runtime hasta ahora).

## Contexto

El editor permite marcar en un nodo un "salto a otro flujo" (`JumpToDefinitionId`), pero era solo
visual (deuda ADR-0056). El usuario definio la semantica: cuando un flujo llega a un evento de FIN
que tiene salto, debe **crear una TAREA HIJA** que corra ese otro flujo, heredando el enlace al padre
y las referencias de formularios/archivos del padre.

## Decision

1. **Punto de enganche**: `WorkflowEngine.FinalizeEndEventAsync` (donde el fin auto-finaliza, ADR-0075).
   Si `endNode.JumpToDefinitionId` esta seteado y hay tarea, se dispara el salto.

2. **Seam sin ciclo de DI** (`IChildTaskStarter` / `ChildTaskStarter`): `TaskItemService` ya depende de
   `IWorkflowEngine`, asi que el motor NO puede depender de `TaskItemService` (ciclo de construccion).
   Se define una interfaz estrecha que el motor resuelve PEREZOSAMENTE via `IServiceProvider` (inyectado
   opcional/nullable: DI da el real, los tests que construyen el motor a mano pasan null y el salto se
   omite). El starter usa el MISMO `IApplicationDbContext` scoped -> todo corre en la transaccion del
   avance del padre (atomico: si el hijo falla, se revierte el cierre del padre).

3. **Creacion de la hija** (`ChildTaskStarter.StartChildFromJumpAsync`):
   - `Number` por `ISequenceService` (T05/T/5); `ParentId = padre` (el enlace/"conexion" padre<->hija);
     `EntidadId` heredado del padre (conexion de negocio); tablero/columna inicial = los del padre (al
     arrancar el flujo hijo, su primer paso mueve la tarjeta al tablero que ese nodo tenga, ADR previo).
   - **Adjuntos heredados**: se copian las filas `TaskItemAttachment` apuntando a la hija reusando el
     mismo `Url` (el fichero vive en Url, no como blob) -> la hija referencia los mismos archivos sin
     duplicarlos.
   - Arranca el flujo destino: `IWorkflowEngine.StartInstanceAsync(jumpDefId, hija.Id, actor, ...)`.
   - **Idempotente**: si ya existe una hija del mismo padre corriendo ese flujo, no crea otra (el fin
     puede alcanzarse mas de una vez si el padre se reabre/re-cierra).
   - **Guarda**: el flujo destino debe estar PUBLICADO; si no, se omite el salto (no rompe el cierre
     del padre).

## Consecuencias

- Al terminar un flujo con salto, nace una tarea hija que continua el proceso en otro flujo, ligada al
  padre por `ParentId` y con sus mismos archivos.
- El encargado de la hija lo resuelve su propio flujo (por cargo / herencia robusta ADR-0075).

## Pendiente (siguiente paso enfocado)

- **Referencia de FORMULARIOS del padre en la hija** (solo lectura): hoy los formularios se anclan por
  `FormResponse.Reference == TaskItem.Number` (string), asi que la hija (otro Number) no los ve. Falta
  una seccion en el detalle de la hija que muestre, en solo lectura, las respuestas Submitted del padre
  (via `ParentId` -> `parent.Number`). Es UI + un metodo de servicio; se hace aparte. Los ADJUNTOS ya se
  heredan (copia de filas), los FORMULARIOS quedan para este paso.
